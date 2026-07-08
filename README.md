# TZTEK Vispec CMM Import

本项目用于三坐标测量任务自动生成的第一阶段导入能力：

```text
DXF/STP 文件
→ RawDocument / RawElement
→ Primitive / Tolerance
→ PrimitiveToleranceItem
→ ImportResult
→ MeasurementTask / MeasurementStep / MeasurementPoint
```

当前对外入口仍然是 `IPrimitiveToleranceService`。导入解析内部通过 `IFileImportPipeline`
调度具体格式导入器。

## 当前支持范围

当前运行时只声明支持：

- `.dxf`：通过 Python sidecar 和 `ezdxf` 提取 2D 几何与标注候选。
- `.stp` / `.step`：通过 CadQuery/OCP 读取 STEP 模型，提取平面、圆柱、圆锥、球面和复杂曲面候选。

`.pdf`、`.dwg`、`.iges`、`.stl`、`.obj`、`MSOP` 暂未作为运行时支持格式，只保留后续扩展方向。

## 本地依赖

DXF 解析依赖：

```bash
pip install -r tools/import_parser/requirements.txt
```

STP/STEP 解析依赖 CadQuery，建议使用项目内专用环境：

```powershell
py -3.13 -m venv .venv-step
.\.venv-step\Scripts\python.exe -m pip install -r tools/import_parser/requirements-step.txt
$env:TZTEK_PARSER_PYTHON="D:\summer_stage\project\TZTEK\.venv-step\Scripts\python.exe"
```

PDF/OCR 扩展代码已保留，但当前默认不注册为可用输入格式。未来打开 PDF 支持时需要你本机部署好：

- DeepSeek-OCR
- Ollama
- `deepseek-r1:8b`

## 环境变量

不要在代码里写个人机器路径。需要通过环境变量配置：

```text
TZTEK_PARSER_PYTHON        Python 可执行文件，默认 python
TZTEK_IMPORT_PARSER_SCRIPT 可选，parse_file.py 的绝对路径
TZTEK_DEEPSEEK_OCR_SCRIPT  deepseek_ocr_runner.py 路径
TZTEK_DEEPSEEK_OCR_PYTHON  OCR 环境的 Python，可不同于普通 parser Python
TZTEK_DEEPSEEK_OCR_OUTPUT  可选，OCR 输出目录
TZTEK_OLLAMA_BASE_URL      Ollama 地址，默认 http://localhost:11434
TZTEK_OLLAMA_MODEL         Ollama 模型，默认 deepseek-r1:8b
DEEPSEEK_OCR_MODEL_PATH    DeepSeek-OCR 模型路径，默认 deepseek-ai/DeepSeek-OCR
```

Windows PowerShell 示例：

```powershell
$env:TZTEK_DEEPSEEK_OCR_SCRIPT="D:\summer_stage\project\TZTEK\tools\import_parser\deepseek_ocr_runner.py"
$env:TZTEK_DEEPSEEK_OCR_PYTHON="python"
$env:TZTEK_DEEPSEEK_OCR_OUTPUT="D:\summer_stage\project\TZTEK\tools\import_parser\output"
$env:DEEPSEEK_OCR_MODEL_PATH="deepseek-ai/DeepSeek-OCR"
```

WSL/Conda 用户建议用本机自己的启动脚本进入 OCR 环境，并把路径转换为 `/mnt/d/...`。
该启动脚本属于个人机器配置，不应提交到仓库。

## 手动测试 sidecar

```bash
python tools/import_parser/parse_file.py --format dxf --input sample.dxf
.\.venv-step\Scripts\python.exe tools/import_parser/parse_file.py --format step --input sample.stp
```

STP 第一版面向三坐标测量候选基元提取，输出平面、圆柱、圆锥、球面和自由曲面候选；不解析完整 PMI/GD&T 公差。

## 测量计划数据输出

导入完成后，可以通过 `IPrimitiveToleranceService.GenerateMeasurementTasks()` 生成路径规划可用的数据：

```text
PrimitiveToleranceItem
→ 可测特征过滤/初步分组
→ 元素命名
→ 拟合方法
→ 测点坐标和法向
→ 探针、逼近/回退/搜索距离
→ MeasurementTask
```

当前默认规则：

- 平面：默认 5 个测点。
- 圆/圆弧：默认 8 个圆周测点。
- 圆柱：默认 2 层，每层 8 个测点。
- 圆锥：默认圆周测点。
- 球面：默认 6 个测点。
- 自由曲面：第一版只输出代表测点。
- 默认距离：逼近 5 mm，回退 5 mm，搜索 2 mm，安全余量 10 mm。
- 默认命名：`PL1`、`CY1`、`CN1`、`SP1`、`C1`、`LN1`。

这一步只生成路径规划输入数据，不承诺已经完成 TSP/RRT 最短路径和真实碰撞避障。

可调整参数在 `MeasurementPlanOptions` 中，包括测点数量、最小平面面积、最小圆柱半径、默认安全距离、是否启用同特征分组等。

## 协作规则

可以提交：

- C# 接口、导入器、提取器、prompt、README。
- Python sidecar 源码、schema、requirements。

不要提交：

- DeepSeek-OCR / R1 模型权重。
- `.safetensors`、`.pt`、`.pth`、`.bin`、`.onnx` 等大模型文件。
- OCR 输出目录、缓存、虚拟环境、本机绝对路径配置。

新增文件和修改应尽量限制在本仓库内，便于 GitHub 同步协作。
