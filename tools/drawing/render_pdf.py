#!/usr/bin/env python3
import argparse
import json
import math
from pathlib import Path

import fitz


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    parser.add_argument('--output')
    parser.add_argument('--output-dir')
    parser.add_argument('--manifest')
    parser.add_argument('--dpi', type=int, default=220)
    parser.add_argument('--max-edge', type=int, default=6500)
    parser.add_argument('--max-pixels', type=int, default=32000000)
    args = parser.parse_args()
    document = fitz.open(args.input)
    if document.page_count == 0:
        raise RuntimeError('PDF 没有页面')
    output_dir = Path(args.output_dir or Path(args.output).parent)
    output_dir.mkdir(parents=True, exist_ok=True)
    pages = []
    for index in range(document.page_count):
        page = document.load_page(index)
        scale = args.dpi / 72.0
        width = page.rect.width * scale
        height = page.rect.height * scale
        edge_limit = max(float(args.max_edge), 0.0)
        if edge_limit > 0:
            edge = max(width, height)
            if edge > edge_limit:
                scale *= edge_limit / edge

        width = page.rect.width * scale
        height = page.rect.height * scale
        pixel_limit = max(float(args.max_pixels), 0.0)
        if pixel_limit > 0 and width * height > pixel_limit:
            scale *= math.sqrt(pixel_limit / (width * height))

        pixmap = page.get_pixmap(matrix=fitz.Matrix(scale, scale), alpha=False)
        if pixel_limit > 0 and pixmap.width * pixmap.height > pixel_limit:
            correction = math.sqrt(pixel_limit / (pixmap.width * pixmap.height)) * 0.999
            scale *= correction
            pixmap = page.get_pixmap(matrix=fitz.Matrix(scale, scale), alpha=False)

        output = output_dir / f'drawing-page-{index + 1:03d}.jpg'
        pixmap.save(output, output='jpeg')
        pages.append({
            'pageNumber': index + 1,
            'path': str(output),
            'width': pixmap.width,
            'height': pixmap.height,
            'pixelCount': pixmap.width * pixmap.height,
        })
    if args.manifest:
        Path(args.manifest).write_text(json.dumps(pages, ensure_ascii=False), encoding='utf-8')


if __name__ == '__main__':
    main()
