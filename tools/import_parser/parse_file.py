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

    raise RuntimeError(
        "STEP parsing is reserved for FreeCAD/OpenCascade/pythonocc integration. "
        f"For now, provide a sidecar JSON next to the model: {sidecar_json.name}"
    )


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
