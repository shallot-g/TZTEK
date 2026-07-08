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
            geometry = step_face_geometry(face, surface_type, center, area, BRepAdaptor_Surface)

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
                primitives.append(primitive)

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


def step_face_geometry(face: Any, surface_type: str, center: list[float], area: float, adaptor_type: Any) -> dict[str, Any]:
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
        geometry["axisPoint"] = ocp_point_to_list(axis.Location())
        geometry["axisDirection"] = ocp_direction_to_list(axis.Direction())
        geometry["radius"] = float(cylinder.Radius())
        return geometry

    if surface_type == "CONE":
        cone = adaptor.Cone()
        axis = cone.Axis()
        geometry["apex"] = ocp_point_to_list(cone.Apex())
        geometry["axisPoint"] = ocp_point_to_list(axis.Location())
        geometry["axisDirection"] = ocp_direction_to_list(axis.Direction())
        geometry["halfAngleRad"] = float(cone.SemiAngle())
        geometry["refRadius"] = float(cone.RefRadius())
        return geometry

    if surface_type == "SPHERE":
        sphere = adaptor.Sphere()
        geometry["center"] = ocp_point_to_list(sphere.Location())
        geometry["radius"] = float(sphere.Radius())
        return geometry

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

    if surface_type in {"BSPLINE", "BEZIER", "OFFSET", "OTHER", "UNKNOWN"}:
        center = geometry.get("center", [0.0, 0.0, 0.0])
        return {
            "id": element_id,
            "type": "Surface3D",
            "sourceElementId": element_id,
            "surfaceType": surface_type,
            "vertices": [center],
            "triangles": [],
            "area": geometry.get("area"),
        }

    return {
        "id": element_id,
        "type": "Surface3D",
        "sourceElementId": element_id,
        "surfaceType": surface_type,
        "vertices": [geometry.get("center", [0.0, 0.0, 0.0])],
        "triangles": [],
        "area": geometry.get("area"),
    }


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
