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

### STEP 有限圆柱解析

圆柱不再只保存无限解析曲面的轴原点。STEP sidecar 现在从 OCP 曲面参数中提取：

```text
axisStart / axisEnd / axisCenter / length
uMin / uMax / angularSpanRad
radialReference / surfaceOrientation / isInnerSurface
```

这些字段用于保证解析、测点、碰撞包围盒和前端显示使用同一份有限几何数据：

- `axisCenter` 是圆柱有效轴段的中心，不能用 OpenCascade 的轴原点代替。
- 被 STEP 拆分的互补圆柱面会按轴线、半径、轴向区间、周向区间和内外属性合并。
- 同轴同半径但轴向分离的孔不会被错误合并。
- `REVERSED` 实体面作为内孔壁候选，`FORWARD` 作为外圆柱面候选；无法确认时保留未确定状态。
- 圆柱默认在有效长度的 `20% / 50% / 80%` 位置生成三层测点，每层 8 点。
- 外圆柱法向指向实体外部，内孔法向指向孔腔；接近点和回退点沿自由空间法向生成。

`圆柱.stp` 当前解析结果为两个最终圆柱测量特征：内圆柱半径约 `16.056 mm`、外圆柱半径约 `38.715 mm`，二者长度均约 `256 mm`、轴向中心均约为 `Z=-128 mm`，每个特征生成 24 个测点。

注意：默认竖直探针无法直接完成圆柱径向接触。当前系统会保留几何测点用于规划演示，并在界面标记“需转角测头或侧向探针”；不能将该路径直接宣称为真实设备可执行程序。

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

- 线：默认均匀 3 点。
- 圆/圆弧：默认 8 个圆周测点。
- 圆弧：默认弧段均匀 5 点。
- 平面：默认 5 x 5 网格测点。
- 圆柱：默认 3 层，每层 8 个测点。
- 圆锥：默认 2 层，每层 8 个测点。
- 球面：默认球面均匀 15 点。
- 自由曲面：第一版只输出代表测点。
- 默认距离：逼近 5 mm，回退 5 mm，搜索 2 mm，安全余量 10 mm。
- 默认命名：`PL1`、`CY1`、`CN1`、`SP1`、`C1`、`LN1`。

这一步生成路径规划输入数据，并提供第一版原型级安全移动与碰撞风险标记；不承诺已经完成 TSP/RRT 最短路径和工业级真实碰撞避障。

可调整参数在 `MeasurementPlanOptions` 中，包括测点数量、最小平面面积、最小圆柱半径、默认安全距离、是否启用同特征分组等。

## 安全路径与连续测量

当前已经实现第一版安全移动、GOTO 锚点避障和特征级连续测量，入口仍是：

```csharp
IPrimitiveToleranceService.GenerateMeasurementTasks(options)
```

当 `MeasurementPlanOptions.EnableCollisionAvoidance = true` 时，`DefaultMeasurementPlanner`
会调用 `ISafePathPlanner`，把原始测量步骤展开为包含 `Movement` 和 `Measurement` 的执行步骤。

当前路径策略：

```text
安全平面
→ 进入某个测量特征
→ 连续测完该特征的所有测点
→ 退出该特征
→ 回安全平面
→ 进入下一个测量特征
```

### 新路径规划算法总结

当前路径规划不是直接做完整 TSP/RRT，而是先生成一条适合三坐标原型演示的安全测量路径。整体流程如下：

```text
MeasurementTask 原始测量特征
→ 为每个特征生成测点
→ 为每个特征生成入口/出口 GOTO 锚点
→ 特征内部连续测量
→ 特征之间优先直线连接
→ 直线连接有碰撞风险时，使用自动全局安全 GOTO 绕行
→ 自动 GOTO 不可用时，使用用户/测量软件预设 GOTO
→ 仍不可用时，标记 Needs manual GOTO point
```

核心设计原则：

- **特征内部连续测量**：圆柱、平面、圆、圆弧、线、圆锥、球等基元会尽量在一个特征内连续测完，避免每个测点都回安全平面。
- **特征之间直连优先**：从一个特征的退出锚点到下一个特征的入口锚点，如果碰撞检测通过，就直接移动，减少无意义绕行。
- **自动全局安全 GOTO**：如果直连有碰撞风险，系统会把路径抬到工件整体安全高度上方，再水平移动到目标特征上方，最后进入目标特征。
- **人工 GOTO 只是兜底**：只有自动全局安全 GOTO 和用户预设 GOTO 都无法证明安全时，才标记需要人工设置 GOTO。
- **当前不是工业级真实碰撞检测**：现在使用 AABB 粗筛加部分基元窄相检查，适合原型演示；夹具、侧孔、横向探针、机床行程仍需要后续增强。

默认连续测量的基元：

- `Line`
- `Circle`
- `Arc`
- `Plane`
- `Cylinder`
- `Cone`
- `Sphere`

`Surface3D` 目前只生成代表点，仍按保守策略处理。

相关开关：

```csharp
new MeasurementPlanOptions
{
    EnableCollisionAvoidance = true,
    EnableContinuousFeaturePath = true,
    EnableContinuousCylinderPath = true,
    EnableSinglePointSafetyPath = true,
    EnableCollisionCheck = true,
    CollisionSafetyMarginMm = 2.0,
    EnableGotoAvoidance = true,
    EnableDirectTransitionShortcut = true,
    EnableAutoGlobalSafeGoto = true,
    AutoSafeGotoExtraClearanceMm = 5.0,
    EnablePrimitiveNarrowPhaseCollisionCheck = true,
    RequireUserGotoWhenAnchorTransitionCollides = true
}
```

含义：

- `EnableCollisionAvoidance`：是否展开安全移动步骤。
- `EnableContinuousFeaturePath`：平面、圆、圆弧、线、圆锥、球等是否按特征连续测量。
- `EnableContinuousCylinderPath`：圆柱是否使用连续测量；空心圆柱会用“同轴小半径为内孔候选”的第一版启发式修正接近方向。
- `EnableSinglePointSafetyPath`：当不适合连续测量时，是否退回“每个测点回安全平面”的保守策略。
- `EnableCollisionCheck`：是否启用第一版保守碰撞检测。
- `CollisionSafetyMarginMm`：碰撞体额外膨胀余量。
- `EnableGotoAvoidance`：检测到碰撞时是否尝试插入 GOTO 点。
- `UserGotoPoints`：用户或测量软件预设的安全 GOTO 点列表。
- `EnableDirectTransitionShortcut`：特征锚点之间优先尝试无碰撞直线移动。
- `EnableAutoGlobalSafeGoto`：锚点直连有碰撞时，是否自动尝试全局安全高度 GOTO。
- `AutoSafeGotoExtraClearanceMm`：自动全局安全 GOTO 在安全高度之上的额外余量。
- `EnablePrimitiveNarrowPhaseCollisionCheck`：AABB 粗筛命中后，是否继续执行基元级窄相检查以降低误报。
- `RequireUserGotoWhenAnchorTransitionCollides`：自动 GOTO 和用户 GOTO 都不可用时，是否标记需要人工 GOTO。

已验证的 `圆柱.stp` 测试结果：

```text
原始 STP 解析：Cylinder 4, Plane 2
特征识别后：CY1 24点, CY2 24点, PL1 25点, PL2 25点

旧版单点安全路径：
Movement 392, Measurement 98, TotalPath 43419.592 mm

仅圆柱连续：
Movement 256, Measurement 98, TotalPath 17329.210 mm

当前默认特征级连续：
Movement 114, Measurement 98, TotalPath 3902.866 mm

开启第一版碰撞检测 + GOTO 避障：
无碰撞的特征锚点移动直接保留。
锚点移动有碰撞时，先尝试自动全局安全 GOTO。
自动全局安全 GOTO 不可用时，再尝试用户/测量软件预设 GOTO。
两者都不可用时，保留原路径并标记 Needs manual GOTO point。

当前圆柱回归：
Movement 118, Measurement 98, AutoGlobalSafeGoto 6, ManualGoto 0, TotalPath 4014.866 mm
```

## 第一版碰撞检测

当前实现了原型级碰撞检测，核心接口位于 `Interfaces.Pipeline` 命名空间，普通调用方不需要直接调用：

- `ICollisionChecker`
- `IPathCollisionResolver`

默认实现位于 Core 层：

- `DefaultCollisionChecker`
- `DefaultPathCollisionResolver`

运行逻辑：

```text
MeasurementTask
→ 生成 Movement / Measurement 路径
→ 收集工件基元并生成简化 AABB 碰撞体
→ 按默认探针半径和安全余量膨胀 AABB
→ 检测 Movement 线段是否穿过碰撞体
→ 无碰撞：保留当前直线移动
→ 有碰撞：回到特征入口/出口 GOTO 锚点语义
→ 优先尝试自动全局安全 GOTO
→ 自动 GOTO 不可用时尝试用户或测量软件预设 GOTO 点
→ 仍没有可用 GOTO：保留原路径并标记需要人工 GOTO 点
```

当前 GOTO 锚点策略：

```text
1. 特征入口/出口 GOTO：
   每个测量特征进入前有 EntryGoto，测完退出后有 ExitGoto。
   特征内部连续测量路径被视为已知测量路径。

2. 直连优先：
   当前 GOTO → 下一特征 EntryGoto 若无碰撞，直接移动。

3. 自动全局安全 GOTO：
   若锚点直连有碰撞，自动生成 current → current 上方 safeZ → target 上方 safeZ → target。
   高空水平段必须通过碰撞检测；若高度不够，会按安全余量逐级抬升。

4. 用户/测量软件预设 GOTO：
   若锚点直连有碰撞，从 UserGotoPoints 中选择 current → goto → target 两段均无碰撞且总距离最短的点。

5. 人工 GOTO：
   如果自动 GOTO 和用户 GOTO 都不可用，保留原 Movement，并将 GotoTarget.Reason 标记为 Needs manual GOTO point。
```

当前算法不会凭空生成主轴方向的未知 GOTO，也不会使用局部安全平面法向抬高绕行。自动 GOTO 只面向默认竖直探针的全局上方安全移动；对于侧孔、横向探针等复杂情况，推荐由用户或测量软件提前提供侧向安全 GOTO 点；如果系统无法可靠判断，则标记为需要人工设置 GOTO 点。

当前碰撞检测仍是保守近似，不等价于真实 CAD 实体碰撞：

- 使用 AABB 包围盒，可能误报碰撞。
- AABB 粗筛后会对圆柱、球等基元做第一版窄相检查，降低简单圆柱误报。
- 默认使用 2 mm 触发式探针近似，不做多探针选择。
- 特征内部接近、测量、退出段按已知测量路径处理，避免把正常接触误报为绕障。
- 自动全局安全 GOTO 只解决普通上方绕行；锚点直连、自动 GOTO、用户 GOTO 都失败时才要求人工设置。
- 暂不处理夹具、工作台、机床行程、测头体积、探针杆精确模型。

后续如需升级为更真实的避障，优先替换 `IPathCollisionResolver`：

```text
直线 Movement
→ 网格/实体碰撞检测
→ 无碰撞：保留直线移动
→ 有碰撞：选择预设 GOTO、多 GOTO 搜索、A*、RRT 或其它采样路径
```

底面不可测、夹具遮挡、探针是否能进入小孔，目前还没有作为强规则过滤。后续建议在 `IFeatureRecognizer`
或独立的“可达性分析”服务中处理，再进入测点生成、安全路径规划和碰撞检测。

## 可视化演示平台

仓库内新增本地 Web 演示台：

```text
TZTEK.VispecCMM.Demo.Api   ASP.NET Core 会话 API 和静态页面服务
TZTEK.VispecCMM.Demo.Web   React / TypeScript / Three.js 工作台
```

演示平台当前只公开支持 `.stp/.step`，DXF 暂不作为汇报演示输入。它通过现有
`IPrimitiveToleranceService` 完成导入和测量任务生成，不直接调用内部 Pipeline 接口。

### 本次可视化改动摘要

- 新增 ASP.NET Core Demo API，负责文件上传、异步解析会话、测量任务生成、临时 STL 导出和结果查询。
- 新增 React / TypeScript / Three.js 工作台，以浏览器方式展示工件、识别基元、测点、探针和运动路径。
- 同一次 STEP 导入分别生成基础路径和优化路径，用于对比连续特征测量、碰撞检测和自动安全 GOTO 的效果。
- 新增 `tools/import_parser/export_visual_mesh.py`，使用 CadQuery 将 STEP 临时转换为 STL；STL 仅用于显示，测量数据仍来自原有基元解析流水线。
- 新增会话隔离与临时文件清理，避免多个演示任务互相覆盖；上传文件和生成资源默认在两小时后清理。
- 新增 Playwright 页面回归测试，覆盖 `1366x768`、`1440x900` 和 `1920x1080` 三种汇报分辨率。
- Demo 上传接口和界面当前只接受 `.stp/.step`；DXF 核心代码仍保留，但本阶段不对外展示或承诺支持。

主要展示内容：

- STEP 工件 STL 外壳和识别基元。
- 测量点、测点法向和选中元素的测点编号。
- 默认触发式探针及路径逐步播放。
- Movement、Measurement、自动 GOTO 和风险路径分色。
- 基础路径与优化路径切换。
- 基元数、测点数、路径长度、预计时间和 GOTO 数量对比。
- 顶视、前视、侧视、轴测视角和图层开关。

### 界面数据说明

左侧“识别元素”同时保留了最终测量特征和原始 STEP 曲面，二者含义不同：

- `CY1`、`CY2`、`PL1`、`PL2` 等名称表示经过筛选、分组和命名后进入测量计划的特征；其下方会显示已分配的测点数量。
- `step_face_0005` 等名称表示 STEP 文件中的原始 CAD 面编号，用于追溯解析结果，并不代表一种新的基元类型。
- 原始面显示 `0 点` 表示该面被成功解析并保留，但没有独立进入当前测量计划，探针不会执行该元素。

一个真实孔或圆柱通常由多个 STEP 拓扑面组成，因此原始面数量可能多于最终测量特征数量。后续界面可进一步拆分为“测量特征”和“原始 CAD 曲面”两个分组。

当前界面已经按“测量特征”和“原始 CAD 曲面”分组。圆柱详情额外显示真实长度、轴向起止点、内孔/外圆柱属性和参与合并的来源面；Three.js 使用有限长度和 STEP 径向参考方向绘制，不再通过面积猜测圆柱长度。

首次运行：

```powershell
cd D:\summer_stage\project\TZTEK
$env:TZTEK_PARSER_PYTHON="D:\summer_stage\project\TZTEK\.venv-step\Scripts\python.exe"

cd TZTEK.VispecCMM.Demo.Web
npm install
npm run build

cd ..
dotnet run --project TZTEK.VispecCMM.Demo.Api
```

如果提示 `address already in use`，说明 `5078` 端口上的 Demo 服务已经启动，不需要重复运行，直接在浏览器访问即可。需要重启服务时可先执行：

```powershell
Get-NetTCPConnection -LocalPort 5078 |
    Select-Object -ExpandProperty OwningProcess |
    ForEach-Object { Stop-Process -Id $_ -Force }
```

浏览器打开：

```text
http://localhost:5078
```

页面内置两个示例入口：

- `圆柱.stp`：98 个测点、自动安全 GOTO、人工 GOTO 为 0。
- `B23CD83-05-02V1.0.stp`：复杂工件、多种基元和大规模路径。

开发模式可分别启动 API 和 Vite：

```powershell
dotnet run --project TZTEK.VispecCMM.Demo.Api

cd TZTEK.VispecCMM.Demo.Web
npm run dev
```

前端构建与浏览器验证：

```powershell
cd TZTEK.VispecCMM.Demo.Web
npm run build
npm run test:e2e
```

上传文件、会话数据和生成的 STL 存放在系统临时目录，默认两小时后清理，不提交到 GitHub。
当前显示的 STL 只用于可视化，碰撞检测仍是 AABB 粗筛加部分基元窄相检查，不能宣称为工业级实体碰撞。

### 已验证结果

- `dotnet build TZTEK.VispecCMM.sln --nologo`：0 个警告，0 个错误。
- `npm run build`：前端生产构建通过。
- `npm run test:e2e`：三个桌面分辨率的浏览器测试全部通过。
- `圆柱.stp`：识别 6 个基元、4 个测量特征、98 个测点，优化路径约 `4014.9 mm`，人工 GOTO 为 0。

## 协作规则

可以提交：

- C# 接口、导入器、提取器、prompt、README。
- Python sidecar 源码、schema、requirements。

不要提交：

- DeepSeek-OCR / R1 模型权重。
- `.safetensors`、`.pt`、`.pth`、`.bin`、`.onnx` 等大模型文件。
- OCR 输出目录、缓存、虚拟环境、本机绝对路径配置。

新增文件和修改应尽量限制在本仓库内，便于 GitHub 同步协作。
