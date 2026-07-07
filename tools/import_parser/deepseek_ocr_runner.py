#!/usr/bin/env python3
"""Standard DeepSeek-OCR runner for TZTEK PDF/image parsing.

This file is intentionally portable: it does not contain local install paths,
model cache paths, WSL user names, or machine-specific directories.
"""

from __future__ import annotations

import argparse
import os
import sys
import tempfile
from pathlib import Path
from typing import Iterable


def main() -> int:
    args = parse_args()
    input_path = Path(args.input)
    if not input_path.exists():
        raise FileNotFoundError(f"Input file does not exist: {input_path}")

    output_dir = Path(args.output) if args.output else Path(tempfile.mkdtemp(prefix="tztek_ocr_"))
    output_dir.mkdir(parents=True, exist_ok=True)

    model_path = args.model_path or os.environ.get("DEEPSEEK_OCR_MODEL_PATH") or "deepseek-ai/DeepSeek-OCR"
    pages = list(load_pages(input_path, output_dir, args.dpi))
    if not pages:
        raise RuntimeError(f"No image pages were generated from input: {input_path}")

    tokenizer, model, torch = load_model(model_path)
    prompt = args.prompt
    page_texts: list[str] = []

    for page_index, image_path in enumerate(pages, start=1):
        page_output_dir = output_dir / f"page_{page_index:04d}"
        page_output_dir.mkdir(parents=True, exist_ok=True)

        result = model.infer(
            tokenizer,
            prompt=prompt,
            image_file=str(image_path),
            output_path=str(page_output_dir),
            base_size=args.base_size,
            image_size=args.image_size,
            crop_mode=args.crop_mode,
            save_results=True,
            test_compress=args.test_compress,
        )

        text = normalize_result_text(result)
        if not text:
            text = read_text_outputs(page_output_dir)

        page_texts.append(f"<--- Page {page_index} --->\n{text}".strip())

    print("\n\n".join(page_texts))
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run DeepSeek-OCR on a PDF or image and print OCR text.")
    parser.add_argument("--input", required=True, help="Input PDF/PNG/JPG/JPEG file.")
    parser.add_argument("--output", help="Optional output directory for OCR artifacts.")
    parser.add_argument("--model-path", help="Model path or HuggingFace id. Defaults to DEEPSEEK_OCR_MODEL_PATH or deepseek-ai/DeepSeek-OCR.")
    parser.add_argument("--base-size", type=int, default=1024)
    parser.add_argument("--image-size", type=int, default=640)
    parser.add_argument("--dpi", type=int, default=144)
    parser.add_argument("--crop-mode", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--test-compress", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--prompt", default="<image>\n<|grounding|>Convert the document to markdown. ")
    return parser.parse_args()


def load_pages(input_path: Path, output_dir: Path, dpi: int) -> Iterable[Path]:
    suffix = input_path.suffix.lower()
    if suffix == ".pdf":
        return render_pdf_to_images(input_path, output_dir / "pages", dpi)
    if suffix in {".png", ".jpg", ".jpeg"}:
        return [input_path]
    raise ValueError(f"Unsupported OCR input format: {input_path.suffix}")


def render_pdf_to_images(pdf_path: Path, pages_dir: Path, dpi: int) -> list[Path]:
    try:
        import fitz  # type: ignore
    except ModuleNotFoundError as exc:
        raise RuntimeError("PDF OCR requires PyMuPDF. Install it in the OCR environment: pip install pymupdf") from exc

    pages_dir.mkdir(parents=True, exist_ok=True)
    document = fitz.open(str(pdf_path))
    zoom = dpi / 72.0
    matrix = fitz.Matrix(zoom, zoom)
    page_paths: list[Path] = []

    try:
        for page_index in range(document.page_count):
            page = document[page_index]
            pixmap = page.get_pixmap(matrix=matrix, alpha=False)
            page_path = pages_dir / f"page_{page_index + 1:04d}.png"
            pixmap.save(str(page_path))
            page_paths.append(page_path)
    finally:
        document.close()

    return page_paths


def load_model(model_path: str):
    try:
        import torch  # type: ignore
        from transformers import AutoModel, AutoTokenizer  # type: ignore
    except ModuleNotFoundError as exc:
        raise RuntimeError(
            "DeepSeek-OCR runner requires torch and transformers in the OCR Python environment."
        ) from exc

    tokenizer = AutoTokenizer.from_pretrained(model_path, trust_remote_code=True)
    model = AutoModel.from_pretrained(
        model_path,
        attn_implementation="eager",
        trust_remote_code=True,
        use_safetensors=True,
    )
    model = model.eval()

    if torch.cuda.is_available():
        model = model.cuda().to(torch.bfloat16)
    else:
        model = model.to(torch.float32)

    return tokenizer, model, torch


def normalize_result_text(result: object) -> str:
    if result is None:
        return ""
    if isinstance(result, str):
        return result.strip()
    if isinstance(result, (list, tuple)):
        return "\n".join(str(item) for item in result if item is not None).strip()
    return str(result).strip()


def read_text_outputs(output_dir: Path) -> str:
    chunks: list[str] = []
    for suffix in ("*.mmd", "*.md", "*.txt"):
        for path in sorted(output_dir.glob(suffix)):
            try:
                chunks.append(path.read_text(encoding="utf-8"))
            except UnicodeDecodeError:
                chunks.append(path.read_text(encoding="gb18030", errors="ignore"))
    return "\n".join(chunk.strip() for chunk in chunks if chunk.strip())


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:  # noqa: BLE001 - CLI reports clear errors to C# stderr.
        print(str(exc), file=sys.stderr)
        raise SystemExit(1)
