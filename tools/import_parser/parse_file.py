#!/usr/bin/env python3
"""Format parser sidecar for TZTEK Vispec CMM import.

The script prints one JSON document to stdout. Heavy local models and caches are
configured outside the repository by environment variables.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
import sys
from pathlib import Path
from typing import Any


def main() -> int:
    parser = argparse.ArgumentParser(description="Parse DXF/PDF/STEP into TZTEK intermediate JSON.")
    parser.add_argument("--format", required=True, choices=["dxf", "pdf", "step"])
    parser.add_argument("--input", required=True)
    parser.add_argument("--unit", default="Millimeter")
    args = parser.parse_args()

    input_path = Path(args.input)
    if not input_path.exists():
        raise FileNotFoundError(f"Input file does not exist: {input_path}")

    if args.format == "dxf":
        result = parse_dxf(input_path)
    elif args.format == "pdf":
        result = parse_pdf(input_path)
    else:
        result = parse_step(input_path)

    result["filePath"] = str(input_path)
    result["unit"] = args.unit
    print(json.dumps(result, ensure_ascii=False))
    return 0


def parse_dxf(path: Path) -> dict[str, Any]:
    try:
        import ezdxf  # type: ignore
    except ModuleNotFoundError as exc:
        raise RuntimeError("DXF parsing requires ezdxf. Install with: pip install -r tools/import_parser/requirements.txt") from exc

    doc = ezdxf.readfile(path)
    modelspace = doc.modelspace()
    raw_elements: list[dict[str, Any]] = []
    primitives: list[dict[str, Any]] = []
    tolerances: list[dict[str, Any]] = []

    for index, entity in enumerate(modelspace):
        dxftype = entity.dxftype()
        element_id = safe_entity_id(entity, index)
        geometry = geometry_from_dxf_entity(entity)
        annotations = annotations_from_dxf_entity(entity)

        raw_elements.append(
            {
                "id": element_id,
                "elementType": dxftype,
                "geometry": geometry,
                "annotations": annotations,
            }
        )

        primitive = primitive_from_dxf_entity(element_id, dxftype, geometry)
        if primitive:
            primitives.append(primitive)

        text = annotations.get("text")
        if text:
            tolerance = dimensional_tolerance_from_text(f"{element_id}_tol", element_id, text)
            if tolerance:
                tolerances.append(tolerance)

    links = link_tolerances_to_nearest_primitive(primitives, tolerances, raw_elements)

    return {
        "sourceType": "Drawing2D",
        "rawElements": raw_elements,
        "structuredItems": {
            "primitives": primitives,
            "tolerances": tolerances,
            "links": links,
            "datums": [],
            "coordinateSystems": [],
            "uncertainItems": [],
        },
    }


def parse_pdf(path: Path) -> dict[str, Any]:
    ocr_script = os.environ.get("TZTEK_DEEPSEEK_OCR_SCRIPT")
    if not ocr_script:
        raise RuntimeError("PDF parsing requires TZTEK_DEEPSEEK_OCR_SCRIPT to point to your local DeepSeek-OCR runner.")

    python = os.environ.get("TZTEK_DEEPSEEK_OCR_PYTHON") or os.environ.get("TZTEK_PARSER_PYTHON") or sys.executable
    command = [python, ocr_script, "--input", str(path)]
    output_dir = os.environ.get("TZTEK_DEEPSEEK_OCR_OUTPUT")
    if output_dir:
        command.extend(["--output", output_dir])

    completed = subprocess.run(command, check=True, capture_output=True, text=True, encoding="utf-8")
    ocr_text = completed.stdout
    return {
        "sourceType": "Drawing2D",
        "rawElements": [
            {
                "id": "pdf_ocr_text",
                "elementType": "OCR_TEXT",
                "geometry": {"textLength": len(ocr_text)},
                "annotations": {"text": ocr_text, "source": "DeepSeek-OCR"},
            }
        ],
        "structuredItems": empty_structured_items(),
    }


def parse_step(path: Path) -> dict[str, Any]:
    sidecar_json = Path(f"{path}.json")
    if sidecar_json.exists():
        data = json.loads(sidecar_json.read_text(encoding="utf-8"))
        if "sourceType" not in data:
            data["sourceType"] = "Model3D"
        return data

    try:
        import cadquery as cq  # type: ignore
        from OCP.BRepAdaptor import BRepAdaptor_Surface  # type: ignore
        from OCP.TopAbs import TopAbs_FORWARD, TopAbs_REVERSED  # type: ignore
    except ModuleNotFoundError as exc:
        raise RuntimeError(
            "STEP parsing requires CadQuery. Install with: "
            "pip install -r tools/import_parser/requirements-step.txt"
        ) from exc

    workplane = cq.importers.importStep(str(path))
    shape = workplane.val()
    solids = list(shape.Solids()) if hasattr(shape, "Solids") else []
    face_sources = solids if solids else [shape]

    raw_elements: list[dict[str, Any]] = []
    primitives: list[dict[str, Any]] = []
    global_face_index = 0

    for solid_index, source in enumerate(face_sources):
        for face in source.Faces():
            face_index = global_face_index
            global_face_index += 1

            element_id = f"step_face_{face_index:04d}"
            surface_type = safe_face_geom_type(face)
            center = cq_vector_to_list(face.Center())
            area = safe_float(lambda: face.Area(), 0.0)

            # Skip faces whose area is below the minimum measurable threshold —
            # these are typically construction artifacts, sliver faces at
            # feature boundaries, or faces that are too small for the probe.
            if area < 1.0:
                continue

            geometry = step_face_geometry(
                face, surface_type, center, area, BRepAdaptor_Surface, TopAbs_FORWARD, TopAbs_REVERSED
            )

            annotations = {
                "source": "STEP",
                "surfaceType": surface_type,
                "solidIndex": str(solid_index),
                "faceIndex": str(face_index),
                "candidateRole": candidate_role(surface_type),
            }

            raw_elements.append(
                {
                    "id": element_id,
                    "elementType": surface_type,
                    "geometry": geometry,
                    "annotations": annotations,
                    "area": area,
                    "solidIndex": solid_index,
                    "faceIndex": face_index,
                }
            )

            primitive = primitive_from_step_face(element_id, surface_type, geometry)
            if primitive:
                primitive["solidIndex"] = solid_index
                primitives.append(primitive)

    # In multi-solid assemblies, faces at the interface between two solids
    # are internal / non-exposed — the probe cannot reach them.
    if len(solids) > 1:
        primitives = _remove_mating_faces(primitives)

    return {
        "sourceType": "Model3D",
        "rawElements": raw_elements,
        "structuredItems": {
            "primitives": primitives,
            "tolerances": [],
            "links": [],
            "datums": [],
            "coordinateSystems": [],
            "uncertainItems": [],
        },
    }


def safe_face_geom_type(face: Any) -> str:
    try:
        return str(face.geomType()).upper()
    except Exception:
        return "UNKNOWN"


def step_face_geometry(
    face: Any,
    surface_type: str,
    center: list[float],
    area: float,
    adaptor_type: Any,
    topabs_forward: Any,
    topabs_reversed: Any,
) -> dict[str, Any]:
    geometry: dict[str, Any] = {
        "surfaceType": surface_type,
        "center": center,
        "area": area,
    }

    if surface_type == "PLANE":
        geometry["point"] = center
        geometry["normal"] = cq_vector_to_list(face.normalAt())
        return geometry

    adaptor = adaptor_type(face.wrapped, True)

    if surface_type == "CYLINDER":
        cylinder = adaptor.Cylinder()
        axis = cylinder.Axis()
        axis_point = ocp_point_to_list(axis.Location())
        axis_direction = ocp_direction_to_list(axis.Direction())
        u_min = float(adaptor.FirstUParameter())
        u_max = float(adaptor.LastUParameter())
        v_min = float(adaptor.FirstVParameter())
        v_max = float(adaptor.LastVParameter())
        axis_start = add_scaled(axis_point, axis_direction, v_min)
        axis_end = add_scaled(axis_point, axis_direction, v_max)
        orientation = face.wrapped.Orientation()

        geometry["axisPoint"] = axis_point
        geometry["axisDirection"] = axis_direction
        geometry["radialReference"] = ocp_direction_to_list(cylinder.Position().XDirection())
        geometry["axisStart"] = axis_start
        geometry["axisEnd"] = axis_end
        geometry["axisCenter"] = midpoint(axis_start, axis_end)
        geometry["length"] = distance(axis_start, axis_end)
        geometry["uMin"] = u_min
        geometry["uMax"] = u_max
        geometry["angularSpanRad"] = max(0.0, u_max - u_min)
        geometry["surfaceOrientation"] = (
            "Reversed" if orientation == topabs_reversed
            else "Forward" if orientation == topabs_forward
            else "Unknown"
        )
        geometry["isInnerSurface"] = (
            True if orientation == topabs_reversed
            else False if orientation == topabs_forward
            else None
        )
        geometry["radius"] = float(cylinder.Radius())
        return geometry

    if surface_type == "CONE":
        cone = adaptor.Cone()
        axis = cone.Axis()
        apex = ocp_point_to_list(cone.Apex())
        axis_point = ocp_point_to_list(axis.Location())
        axis_direction = ocp_direction_to_list(axis.Direction())
        half_angle = float(cone.SemiAngle())
        ref_radius = float(cone.RefRadius())

        v_min = float(adaptor.FirstVParameter())
        v_max = float(adaptor.LastVParameter())
        u_min = float(adaptor.FirstUParameter())
        u_max = float(adaptor.LastUParameter())
        # OpenCASCADE cone parameterisation:
        #   P(U,V) = Location
        #          + (RefRadius + V·sin(Ang))·(cos(U)·XDir + sin(U)·YDir)
        #          + V·cos(Ang)·ZDir
        # V is the distance along the *generatrix* (surface line), NOT along
        # the axis.  The axial distance corresponding to V is V·cos(Ang).
        # The apex is at V = -RefRadius / sin(Ang), i.e. 0 when RefRadius=0.
        cos_ang = math.cos(half_angle)
        sin_ang = math.sin(half_angle)

        # Trim-bound axis positions: centre of the circular cross-section at V.
        # V is along the generatrix; v_min < v_max always, so the smaller-V end
        # has the smaller radius (closer to the apex).
        v_small_end = add_scaled(axis_point, axis_direction, v_min * cos_ang)
        v_large_end = add_scaled(axis_point, axis_direction, v_max * cos_ang)
        r_small = ref_radius + v_min * sin_ang
        r_large = ref_radius + v_max * sin_ang

        # CMM convention: measurement proceeds from the larger face (base /
        # 下面) toward the smaller face (tip / 上面).  Therefore axisStart
        # holds the larger-radius end and axisEnd the smaller-radius end.
        axis_start = v_large_end   # larger radius → 下面 (base)
        axis_end   = v_small_end   # smaller radius → 上面 (tip)
        r_start    = r_large
        r_end      = r_small

        axis_center = midpoint(axis_start, axis_end)
        face_length = distance(axis_start, axis_end)

        geometry["apex"] = apex
        geometry["axisPoint"] = axis_point
        geometry["axisDirection"] = axis_direction
        geometry["halfAngleRad"] = half_angle
        geometry["refRadius"] = ref_radius
        geometry["axisStart"] = axis_start
        geometry["axisEnd"] = axis_end
        geometry["axisCenter"] = axis_center
        geometry["length"] = face_length
        # Radii at the face trim boundaries: r(V) = RefRadius + V·sin(Ang)
        geometry["radiusStart"] = r_start
        geometry["radiusEnd"]   = r_end
        geometry["uMin"] = u_min
        geometry["uMax"] = u_max
        geometry["angularSpanRad"] = max(0.0, u_max - u_min)
        orientation = face.wrapped.Orientation()
        geometry["surfaceOrientation"] = (
            "Reversed" if orientation == topabs_reversed
            else "Forward" if orientation == topabs_forward
            else "Unknown"
        )
        geometry["isInnerSurface"] = (
            True if orientation == topabs_reversed
            else False if orientation == topabs_forward
            else None
        )
        return geometry

    if surface_type == "SPHERE":
        sphere = adaptor.Sphere()
        geometry["center"] = ocp_point_to_list(sphere.Location())
        geometry["radius"] = float(sphere.Radius())
        return geometry

    # Freeform / extrusion / torus: sample the trimmed face with local normals
    # so approach points can leave the surface along the real contact direction.
    sample_u, sample_v = (8, 3) if surface_type in {"EXTRUSION", "REVOLUTION", "TORUS"} else (5, 5)
    full_v = surface_type in {"EXTRUSION", "REVOLUTION", "TORUS"}
    pts, normals, closed_u = sample_surface_grid(
        adaptor, face, topabs_reversed, sample_u, sample_v, full_v=full_v
    )
    if pts:
        geometry["samplePoints"] = pts
        geometry["sampleNormals"] = normals
        if len(pts) == sample_u * sample_v:
            geometry["sampleU"] = sample_u
            geometry["sampleV"] = sample_v
            geometry["sampleClosedU"] = closed_u

    orientation = face.wrapped.Orientation()
    geometry["surfaceOrientation"] = (
        "Reversed" if orientation == topabs_reversed
        else "Forward" if orientation == topabs_forward
        else "Unknown"
    )
    inward = infer_inward_tube(pts, normals) if pts else None
    if inward is True:
        geometry["isInnerSurface"] = True
    elif inward is False:
        geometry["isInnerSurface"] = False
    else:
        geometry["isInnerSurface"] = (
            True if orientation == topabs_reversed
            else False if orientation == topabs_forward
            else None
        )

    return geometry


def primitive_from_step_face(element_id: str, surface_type: str, geometry: dict[str, Any]) -> dict[str, Any] | None:
    if surface_type == "PLANE":
        return {
            "id": element_id,
            "type": "Plane",
            "sourceElementId": element_id,
            "point": geometry.get("point"),
            "normal": geometry.get("normal", [0.0, 0.0, 1.0]),
            "area": geometry.get("area"),
        }

    if surface_type == "CYLINDER":
        return {
            "id": element_id,
            "type": "Cylinder",
            "sourceElementId": element_id,
            "axisPoint": geometry.get("axisPoint"),
            "axisDirection": geometry.get("axisDirection"),
            "radialReference": geometry.get("radialReference"),
            "axisStart": geometry.get("axisStart"),
            "axisEnd": geometry.get("axisEnd"),
            "axisCenter": geometry.get("axisCenter"),
            "length": geometry.get("length"),
            "startAngleRad": geometry.get("uMin"),
            "endAngleRad": geometry.get("uMax"),
            "angularSpanRad": geometry.get("angularSpanRad"),
            "surfaceOrientation": geometry.get("surfaceOrientation"),
            "isInnerSurface": geometry.get("isInnerSurface"),
            "sourceElementIds": [element_id],
            "radius": geometry.get("radius"),
            "area": geometry.get("area"),
        }

    if surface_type == "CONE":
        return {
            "id": element_id,
            "type": "Cone",
            "sourceElementId": element_id,
            "apex": geometry.get("apex") or geometry.get("axisPoint"),
            "axisDirection": geometry.get("axisDirection"),
            "halfAngleRad": geometry.get("halfAngleRad"),
            "axisStart": geometry.get("axisStart"),
            "axisEnd": geometry.get("axisEnd"),
            "axisCenter": geometry.get("axisCenter"),
            "length": geometry.get("length"),
            "radiusStart": geometry.get("radiusStart"),
            "radiusEnd": geometry.get("radiusEnd"),
            "startAngleRad": geometry.get("uMin"),
            "endAngleRad": geometry.get("uMax"),
            "angularSpanRad": geometry.get("angularSpanRad"),
            "refRadius": geometry.get("refRadius"),
            "surfaceOrientation": geometry.get("surfaceOrientation"),
            "isInnerSurface": geometry.get("isInnerSurface"),
            "area": geometry.get("area"),
        }

    if surface_type == "SPHERE":
        return {
            "id": element_id,
            "type": "Sphere",
            "sourceElementId": element_id,
            "center": geometry.get("center"),
            "radius": geometry.get("radius"),
            "area": geometry.get("area"),
        }

    center = geometry.get("center", [0.0, 0.0, 0.0])
    vertices = geometry.get("samplePoints") or [center]
    return {
        "id": element_id,
        "type": "Surface3D",
        "sourceElementId": element_id,
        "surfaceType": surface_type,
        "vertices": vertices,
        "vertexNormals": geometry.get("sampleNormals") or [],
        "triangles": [],
        "area": geometry.get("area"),
        "isInnerSurface": geometry.get("isInnerSurface"),
        "surfaceOrientation": geometry.get("surfaceOrientation"),
        "sampleU": geometry.get("sampleU"),
        "sampleV": geometry.get("sampleV"),
        "sampleClosedU": geometry.get("sampleClosedU"),
    }


def sample_surface_grid(
    adaptor: Any,
    face: Any,
    topabs_reversed: Any,
    sample_u: int,
    sample_v: int,
    full_v: bool = False,
) -> tuple[list[list[float]], list[list[float]], bool]:
    pts: list[list[float]] = []
    normals: list[list[float]] = []
    closed_u = False
    try:
        from OCP.gp import gp_Pnt, gp_Vec  # type: ignore

        u_min = float(adaptor.FirstUParameter())
        u_max = float(adaptor.LastUParameter())
        v_min = float(adaptor.FirstVParameter())
        v_max = float(adaptor.LastVParameter())
        orientation = face.wrapped.Orientation()
        u_span = u_max - u_min
        v_span = v_max - v_min
        closed_u = False
        try:
            closed_u = bool(adaptor.IsUClosed()) or bool(adaptor.IsUPeriodic())
        except Exception:
            closed_u = False
        closed_u = closed_u or abs(abs(u_span) - math.tau) < 0.05 or abs(abs(u_span) - 2 * math.pi) < 0.05
        use_full_v = full_v or closed_u

        for ui in range(sample_u):
            u_frac = ui / sample_u if closed_u else ui / max(sample_u - 1, 1)
            u = u_min + u_span * u_frac
            for vi in range(sample_v):
                if sample_v <= 1:
                    v_frac = 0.5
                elif use_full_v:
                    v_frac = vi / max(sample_v - 1, 1)
                else:
                    v_frac = 0.2 + 0.6 * vi / max(sample_v - 1, 1)
                v = v_min + v_span * v_frac
                try:
                    point = gp_Pnt()
                    d1u = gp_Vec()
                    d1v = gp_Vec()
                    adaptor.D1(u, v, point, d1u, d1v)
                    normal = d1u.Crossed(d1v)
                    if normal.Magnitude() < 1e-12:
                        continue
                    normal.Normalize()
                    if orientation == topabs_reversed:
                        normal.Reverse()
                    pts.append([point.X(), point.Y(), point.Z()])
                    normals.append([normal.X(), normal.Y(), normal.Z()])
                except Exception:
                    continue
    except Exception:
        return [], [], False

    return pts, normals, closed_u


def infer_inward_tube(points: list[list[float]], normals: list[list[float]]) -> bool | None:
    """Return True when sample normals point toward the vertex centroid (inner wall)."""
    if len(points) < 6 or len(points) != len(normals):
        return None

    centroid = [sum(point[index] for point in points) / len(points) for index in range(3)]
    inward = 0
    for point, normal in zip(points, normals):
        radial = [point[index] - centroid[index] for index in range(3)]
        if radial[0] * normal[0] + radial[1] * normal[1] + radial[2] * normal[2] < 0:
            inward += 1

    ratio = inward / len(points)
    if ratio >= 0.8:
        return True
    if ratio <= 0.2:
        return False
    return None


def candidate_role(surface_type: str) -> str:
    return {
        "PLANE": "CandidateDatumOrPlane",
        "CYLINDER": "CandidateHoleOrCylinder",
        "CONE": "CandidateCone",
        "SPHERE": "CandidateSphere",
        "BSPLINE": "CandidateFreeformSurface",
    }.get(surface_type, "CandidateSurface")


def safe_float(factory: Any, fallback: float) -> float:
    try:
        return float(factory())
    except Exception:
        return fallback


def add_scaled(point: list[float], direction: list[float], scale: float) -> list[float]:
    return [point[index] + direction[index] * scale for index in range(3)]


def midpoint(start: list[float], end: list[float]) -> list[float]:
    return [(start[index] + end[index]) / 2.0 for index in range(3)]


def distance(start: list[float], end: list[float]) -> float:
    return math.sqrt(sum((end[index] - start[index]) ** 2 for index in range(3)))


def _remove_mating_faces(
    primitives: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    """Remove planar faces that are mating (internal) surfaces between solids.

    In a STEP assembly, touching solids share mating faces that are not
    exposed to the outside — a CMM probe cannot access them.  We detect such
    pairs by checking whether two planes from different solids are co-planar,
    have opposite normals, and overlap spatially.
    """
    planes = [p for p in primitives if p.get("type") == "Plane"]
    if len(planes) < 2:
        return primitives

    plane_data: list[tuple[int, list[float], list[float], int]] = []
    for idx, p in enumerate(planes):
        n = p.get("normal")
        pt = p.get("point")
        si = p.get("solidIndex", 0)
        if n is None or pt is None:
            continue
        nn = math.sqrt(n[0]**2 + n[1]**2 + n[2]**2) or 1.0
        n = [n[0]/nn, n[1]/nn, n[2]/nn]
        plane_data.append((idx, n, pt, si))

    mating_ids: set[str] = set()
    _EPS_NORMAL = 0.9999
    _EPS_DIST = 0.05

    for i in range(len(plane_data)):
        for j in range(i + 1, len(plane_data)):
            idx_i, ni, pti, si_i = plane_data[i]
            idx_j, nj, ptj, si_j = plane_data[j]

            if si_i == si_j:
                continue

            dot = ni[0]*nj[0] + ni[1]*nj[1] + ni[2]*nj[2]
            if dot > -_EPS_NORMAL:
                continue

            d = abs(ni[0]*(ptj[0]-pti[0]) + ni[1]*(ptj[1]-pti[1]) + ni[2]*(ptj[2]-pti[2]))
            if d > _EPS_DIST:
                continue

            mating_ids.add(planes[idx_i]["id"])
            mating_ids.add(planes[idx_j]["id"])

    if not mating_ids:
        return primitives

    return [p for p in primitives if p.get("id") not in mating_ids]


def cq_vector_to_list(value: Any) -> list[float]:
    if hasattr(value, "toTuple"):
        values = value.toTuple()
        return [float(values[0]), float(values[1]), float(values[2])]
    return point3(value)


def ocp_point_to_list(point: Any) -> list[float]:
    return [float(point.X()), float(point.Y()), float(point.Z())]


def ocp_direction_to_list(direction: Any) -> list[float]:
    return [float(direction.X()), float(direction.Y()), float(direction.Z())]


def geometry_from_dxf_entity(entity: Any) -> dict[str, Any]:
    dxftype = entity.dxftype()
    if dxftype == "POINT":
        return {"point": point3(entity.dxf.location)}
    if dxftype == "LINE":
        return {"start": point3(entity.dxf.start), "end": point3(entity.dxf.end)}
    if dxftype == "CIRCLE":
        return {"center": point3(entity.dxf.center), "radius": float(entity.dxf.radius), "normal": [0.0, 0.0, 1.0]}
    if dxftype == "ARC":
        return {
            "center": point3(entity.dxf.center),
            "radius": float(entity.dxf.radius),
            "startAngleRad": math.radians(float(entity.dxf.start_angle)),
            "endAngleRad": math.radians(float(entity.dxf.end_angle)),
            "normal": [0.0, 0.0, 1.0],
        }
    if dxftype in {"TEXT", "MTEXT"}:
        insert = getattr(entity.dxf, "insert", None)
        return {"insert": point3(insert) if insert is not None else [0.0, 0.0, 0.0]}
    if dxftype == "DIMENSION":
        return {"definitionPoint": point3(getattr(entity.dxf, "defpoint", (0, 0, 0)))}
    return {}


def annotations_from_dxf_entity(entity: Any) -> dict[str, str]:
    dxftype = entity.dxftype()
    annotations: dict[str, str] = {"layer": str(getattr(entity.dxf, "layer", ""))}
    if dxftype == "TEXT":
        annotations["text"] = str(entity.dxf.text)
    elif dxftype == "MTEXT":
        annotations["text"] = str(entity.text)
    elif dxftype == "DIMENSION":
        text = getattr(entity.dxf, "text", "")
        if text:
            annotations["text"] = str(text)
    return annotations


def primitive_from_dxf_entity(element_id: str, dxftype: str, geometry: dict[str, Any]) -> dict[str, Any] | None:
    if dxftype == "POINT":
        return {"id": element_id, "type": "Point", "sourceElementId": element_id, "point": geometry.get("point")}
    if dxftype == "LINE":
        start = geometry.get("start")
        end = geometry.get("end")
        direction = None
        if start and end:
            direction = [end[0] - start[0], end[1] - start[1], end[2] - start[2]]
        return {"id": element_id, "type": "Line", "sourceElementId": element_id, "start": start, "end": end, "direction": direction}
    if dxftype == "CIRCLE":
        return {
            "id": element_id,
            "type": "Circle",
            "sourceElementId": element_id,
            "center": geometry.get("center"),
            "radius": geometry.get("radius"),
            "normal": geometry.get("normal", [0.0, 0.0, 1.0]),
        }
    if dxftype == "ARC":
        return {
            "id": element_id,
            "type": "Arc",
            "sourceElementId": element_id,
            "center": geometry.get("center"),
            "radius": geometry.get("radius"),
            "startAngleRad": geometry.get("startAngleRad"),
            "endAngleRad": geometry.get("endAngleRad"),
            "normal": geometry.get("normal", [0.0, 0.0, 1.0]),
        }
    return None


def dimensional_tolerance_from_text(tolerance_id: str, source_id: str, text: str) -> dict[str, Any] | None:
    normalized = text.replace(" ", "")
    nominal = first_number(normalized)
    if nominal is None:
        return None

    dimension_type = "Diameter" if "Φ" in normalized or "⌀" in normalized else "Radius" if "R" in normalized else "Linear"
    upper = 0.0
    lower = 0.0
    if "±" in normalized:
        deviation = first_number(normalized.split("±", 1)[1])
        if deviation is not None:
            upper = deviation
            lower = -deviation

    return {
        "id": tolerance_id,
        "type": "Dimensional",
        "sourceElementId": source_id,
        "dimensionType": dimension_type,
        "nominalValue": nominal,
        "upperDeviation": upper,
        "lowerDeviation": lower,
        "toleranceValue": max(abs(upper), abs(lower)),
    }


def link_tolerances_to_nearest_primitive(
    primitives: list[dict[str, Any]],
    tolerances: list[dict[str, Any]],
    raw_elements: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    if not primitives or not tolerances:
        return []

    text_points = {
        element["id"]: element.get("geometry", {}).get("insert")
        or element.get("geometry", {}).get("definitionPoint")
        for element in raw_elements
    }

    links_by_primitive: dict[str, list[str]] = {}
    for tolerance in tolerances:
        source_point = text_points.get(tolerance["sourceElementId"])
        nearest = nearest_primitive(primitives, source_point)
        if nearest:
            links_by_primitive.setdefault(nearest["id"], []).append(tolerance["id"])

    return [
        {"primitiveId": primitive_id, "toleranceIds": tolerance_ids}
        for primitive_id, tolerance_ids in links_by_primitive.items()
    ]


def nearest_primitive(primitives: list[dict[str, Any]], source_point: list[float] | None) -> dict[str, Any] | None:
    if source_point is None:
        return primitives[0]

    def representative_point(primitive: dict[str, Any]) -> list[float]:
        return (
            primitive.get("center")
            or primitive.get("point")
            or primitive.get("start")
            or [0.0, 0.0, 0.0]
        )

    return min(primitives, key=lambda primitive: distance(source_point, representative_point(primitive)))


def distance(a: list[float], b: list[float]) -> float:
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(min(len(a), len(b), 3))))


def first_number(text: str) -> float | None:
    chars: list[str] = []
    started = False
    for ch in text:
        if ch.isdigit() or ch in {"-", "."}:
            chars.append(ch)
            started = True
        elif started:
            break
    if not chars:
        return None
    try:
        return float("".join(chars))
    except ValueError:
        return None


def point3(value: Any) -> list[float]:
    if value is None:
        return [0.0, 0.0, 0.0]
    return [float(value[0]), float(value[1]), float(value[2]) if len(value) > 2 else 0.0]


def safe_entity_id(entity: Any, index: int) -> str:
    handle = getattr(entity.dxf, "handle", None)
    return f"{entity.dxftype().lower()}_{handle or index}"


def empty_structured_items() -> dict[str, list[Any]]:
    return {
        "primitives": [],
        "tolerances": [],
        "links": [],
        "datums": [],
        "coordinateSystems": [],
        "uncertainItems": [],
    }


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:  # noqa: BLE001 - CLI must report clear failures to C# stderr.
        print(str(exc), file=sys.stderr)
        raise SystemExit(1)
