from __future__ import annotations

import argparse
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export a STEP model to STL for the local demo viewer.")
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.input.exists():
        raise FileNotFoundError(f"Input STEP file does not exist: {args.input}")

    try:
        import cadquery as cq  # type: ignore
    except ModuleNotFoundError as exc:
        raise RuntimeError("CadQuery is required. Install requirements-step.txt first.") from exc

    shape = cq.importers.importStep(str(args.input))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    cq.exporters.export(shape, str(args.output), exportType="STL", tolerance=0.1, angularTolerance=0.1)
    print(args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
