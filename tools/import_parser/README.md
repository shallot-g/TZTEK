# TZTEK Import Parser Sidecar

This folder contains the Python sidecar used by the C# import pipeline.

Current scope:

- DXF: parses `LINE`, `CIRCLE`, `ARC`, `POINT`, `TEXT`, `MTEXT`, and `DIMENSION` with `ezdxf`.
- PDF: delegates to `deepseek_ocr_runner.py`, which wraps the local DeepSeek-OCR HF model with a stable CLI.
- STEP/STP: keeps the parser entry point and supports a temporary sidecar JSON file next to the model.

Install DXF dependency:

```bash
pip install -r tools/import_parser/requirements.txt
```

Run manually:

```bash
python tools/import_parser/parse_file.py --format dxf --input sample.dxf
python tools/import_parser/parse_file.py --format pdf --input sample.pdf
python tools/import_parser/parse_file.py --format step --input sample.stp
```

Run OCR directly:

```bash
python tools/import_parser/deepseek_ocr_runner.py --input sample.pdf --output tools/import_parser/output
```

Environment variables used by the C# pipeline:

- `TZTEK_PARSER_PYTHON`: Python executable. Defaults to `python`.
- `TZTEK_IMPORT_PARSER_SCRIPT`: Optional absolute path to `parse_file.py`.
- `TZTEK_DEEPSEEK_OCR_SCRIPT`: Path to `deepseek_ocr_runner.py`.
- `TZTEK_DEEPSEEK_OCR_PYTHON`: Python executable for the OCR environment. Defaults to `TZTEK_PARSER_PYTHON`, then `python`.
- `TZTEK_DEEPSEEK_OCR_OUTPUT`: Optional OCR output directory.
- `TZTEK_OLLAMA_BASE_URL`: Ollama base URL. Defaults to `http://localhost:11434`.
- `TZTEK_OLLAMA_MODEL`: Ollama model. Defaults to `deepseek-r1:8b`.
- `DEEPSEEK_OCR_MODEL_PATH`: Model path or HuggingFace id. Defaults to `deepseek-ai/DeepSeek-OCR`.

Windows PowerShell example:

```powershell
$env:TZTEK_DEEPSEEK_OCR_SCRIPT="D:\summer_stage\project\TZTEK\tools\import_parser\deepseek_ocr_runner.py"
$env:TZTEK_DEEPSEEK_OCR_PYTHON="python"
$env:TZTEK_DEEPSEEK_OCR_OUTPUT="D:\summer_stage\project\TZTEK\tools\import_parser\output"
$env:DEEPSEEK_OCR_MODEL_PATH="deepseek-ai/DeepSeek-OCR"
```

WSL users should point `TZTEK_DEEPSEEK_OCR_PYTHON` to a wrapper executable or script that runs
the OCR conda environment, and pass paths that are visible inside WSL, such as `/mnt/d/...`.
Do not commit that local wrapper or absolute machine path unless it is generic for the whole team.

Do not commit model weights, OCR output folders, virtual environments, or cache files.
