# TZTEK 本地测试启动指南

本文档记录本项目在 Windows PowerShell 中的正确测试方式。执行 STEP 导入、工件实体显示或工程图辅助测试前，先按本文档进行环境预检。

> Agent 执行规则：测试本项目时必须先阅读本文档，先绑定并验证 `.venv-step`，再配置 AI 供应商，最后在同一个 PowerShell 窗口启动后端。不要自行安装或替换 CadQuery，不要猜测环境变量名称，也不要把真实 API Key 写入仓库。

## 关键环境

项目已经包含安装好 CadQuery 的专用 Python 环境：

```text
D:\summer_stage\project\TZTEK\.venv-step\Scripts\python.exe
```

不要因为系统 Python 报 `No module named 'cadquery'` 就重新安装 CadQuery。该错误通常表示后端使用了错误的 Python 解释器。

后端通过以下环境变量选择 STEP 解析和 STL 导出使用的 Python：

```text
TZTEK_PARSER_PYTHON
```

环境变量必须在启动 `dotnet run` 的同一个 PowerShell 窗口中设置。

## 标准启动指令

打开一个新的 PowerShell 窗口，完整执行：

```powershell
Set-Location D:\summer_stage\project\TZTEK

# 绑定项目已有的 CadQuery Python，禁止回退到系统 python。
$env:TZTEK_PARSER_PYTHON = (
    Resolve-Path .\.venv-step\Scripts\python.exe
).Path

# 启动前必须通过 CadQuery 预检。
& $env:TZTEK_PARSER_PYTHON -c @"
import sys
import cadquery
print("Python:", sys.executable)
print("CadQuery:", cadquery.__version__)
"@

# 构建 React/TypeScript 前端到 API 的 wwwroot。
Set-Location .\TZTEK.VispecCMM.Demo.Web
npm run build

# 从项目根目录启动 API 和已构建前端。
Set-Location ..
dotnet run `
  --project .\TZTEK.VispecCMM.Demo.Api\TZTEK.VispecCMM.Demo.Api.csproj `
  --urls http://localhost:5078
```

预检成功应显示类似：

```text
Python: D:\summer_stage\project\TZTEK\.venv-step\Scripts\python.exe
CadQuery: 2.8.0
```

服务启动成功应显示：

```text
Now listening on: http://localhost:5078
Application started
```

浏览器访问：

```text
http://localhost:5078
```

## STEP 与工件实体验证

1. 在页面中重新上传 `.stp` 或 `.step` 文件。
2. 等待会话完成解析。
3. 确认左侧出现识别基元。
4. 确认 3D 视图显示灰色工件实体。
5. 打开图层菜单，确认“工件本体”已勾选。
6. 勾选若干基元，测试“显示勾选基元”隔离模式。

如果此前会话在 STL 导出失败时已经完成，修复环境变量后必须重新上传 STEP。旧会话不会自动补生成缺失的 STL。

## 常见错误

### `ModuleNotFoundError: No module named 'cadquery'`

原因：`export_visual_mesh.py` 或 `parse_file.py` 使用了系统 Python。

检查：

```powershell
$env:TZTEK_PARSER_PYTHON
& $env:TZTEK_PARSER_PYTHON -c "import sys, cadquery; print(sys.executable); print(cadquery.__version__)"
```

正确解释器必须是：

```text
D:\summer_stage\project\TZTEK\.venv-step\Scripts\python.exe
```

不要执行新的 `pip install`。先关闭错误启动的服务，在新的 PowerShell 中按“标准启动指令”重新启动。

### 有基元但没有灰色工件实体

依次检查：

1. PowerShell 是否出现 CadQuery 或 `export_visual_mesh.py` 错误。
2. 后端是否在设置 `TZTEK_PARSER_PYTHON` 后启动。
3. 是否重新上传了 STEP，而不是继续查看失败的旧会话。
4. 图层菜单中的“工件本体”是否开启。
5. 浏览器开发者工具 Network 中 STL 请求是否为 HTTP 200。

### `5078` 端口已被占用

先查看监听进程：

```powershell
Get-NetTCPConnection -State Listen -LocalPort 5078 |
  Select-Object LocalAddress, LocalPort, OwningProcess
```

确认该 PID 属于旧的 TZTEK 测试服务后再关闭：

```powershell
$listener = Get-NetTCPConnection -State Listen -LocalPort 5078
Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" |
  Select-Object ProcessId, Name, CommandLine

Stop-Process -Id $listener.OwningProcess
```

不要在未确认进程命令行前强制结束其他程序。

## 停止测试

在运行 `dotnet run` 的 PowerShell 窗口按：

```text
Ctrl+C
```

随后确认端口已释放：

```powershell
if (Get-NetTCPConnection -State Listen -LocalPort 5078 -ErrorAction SilentlyContinue) {
    Write-Warning "5078 端口仍被占用"
} else {
    Write-Host "5078 端口已释放" -ForegroundColor Green
}
```

## AI 供应商配置

豆包或 OpenAI 的环境变量应在 CadQuery 预检通过后、执行 `dotnet run` 前设置。前端不会读取或保存 API Key，所有配置只供后端进程使用。

### 安全输入密钥

推荐使用下面的 PowerShell 函数输入密钥，避免密钥直接出现在命令历史中：

```powershell
function Set-SecretEnvironmentVariable {
    param([Parameter(Mandatory)][string]$Name)

    $secureValue = Read-Host "请输入 $Name" -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
    try {
        [Environment]::SetEnvironmentVariable(
            $Name,
            [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer),
            'Process'
        )
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}
```

这里设置的是当前 PowerShell 进程级环境变量。关闭窗口后密钥自动失效，不写入系统环境变量。

### 配置豆包

豆包需要火山方舟 API Key 和视觉模型推理接入点 ID。`ARK_VISION_ENDPOINT_ID` 应是控制台创建的 `ep-...` 接入点，不是普通模型显示名称。

```powershell
Set-Location D:\summer_stage\project\TZTEK

Set-SecretEnvironmentVariable -Name 'ARK_API_KEY'

$env:ARK_VISION_ENDPOINT_ID = 'ep-请替换为自己的接入点ID'
$env:ARK_BASE_URL = 'https://ark.cn-beijing.volces.com/api/v3'
$env:ARK_TIMEOUT_SECONDS = '300'
```

代码会在 `ARK_BASE_URL` 后调用 `/responses`，因此不要把 Base URL 写成完整的 `/responses` 地址。

豆包必填项检查：

```powershell
if ([string]::IsNullOrWhiteSpace($env:ARK_API_KEY)) {
    throw 'ARK_API_KEY 未设置'
}
if ([string]::IsNullOrWhiteSpace($env:ARK_VISION_ENDPOINT_ID)) {
    throw 'ARK_VISION_ENDPOINT_ID 未设置'
}

Write-Host "豆包接入点：$env:ARK_VISION_ENDPOINT_ID"
Write-Host "豆包 Base URL：$env:ARK_BASE_URL"
Write-Host '豆包 API Key：已设置（不显示内容）'
```

不要使用 `Write-Host $env:ARK_API_KEY`，也不要把真实 Key 粘贴进本文档、截图或聊天记录。

### 配置 GPT / OpenAI 兼容接口

官方 OpenAI 和第三方 OpenAI 兼容中转都使用同一组环境变量：

```powershell
Set-Location D:\summer_stage\project\TZTEK

Set-SecretEnvironmentVariable -Name 'OPENAI_API_KEY'

# 当前项目测试使用的 OpenAI 兼容服务：
$env:OPENAI_BASE_URL = 'https://testvideo.site/v1'

$env:OPENAI_MODEL = 'gpt-5.6-sol'
$env:OPENAI_REASONING_EFFORT = 'low'
$env:OPENAI_TIMEOUT_SECONDS = '300'
```

注意：

- 当前项目测试 URL 是 `https://testvideo.site/v1`，对应 Key 必须由该服务提供，不能混用其他中转或官方 OpenAI 的 Key。
- Base URL 应停在 `/v1`，不要追加 `/responses`，项目代码会自动追加。
- `OPENAI_MODEL` 必须使用该服务商实际开放的模型 ID。默认值是 `gpt-5.6-sol`；如果服务商只开放 `gpt-5.6`，应按其文档改成 `gpt-5.6`。
- 推理强度可选 `none`、`low`、`medium`、`high`、`xhigh`、`max`。代码不接受 `light`，无法识别的值会回退到 `medium`。
- 第三方中转是否完整兼容 Responses API 需要由供应商确认。能调用 Chat Completions 不代表一定支持 `/responses`。

GPT 必填项检查：

```powershell
if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    throw 'OPENAI_API_KEY 未设置'
}
if ([string]::IsNullOrWhiteSpace($env:OPENAI_BASE_URL)) {
    throw 'OPENAI_BASE_URL 未设置'
}
if ([string]::IsNullOrWhiteSpace($env:OPENAI_MODEL)) {
    throw 'OPENAI_MODEL 未设置'
}

Write-Host "GPT Base URL：$env:OPENAI_BASE_URL"
Write-Host "GPT 模型：$env:OPENAI_MODEL"
Write-Host "GPT 推理强度：$env:OPENAI_REASONING_EFFORT"
Write-Host 'GPT API Key：已设置（不显示内容）'
```

### 与 CadQuery 一起启动

无论使用豆包还是 GPT，都不能省略 STEP/CadQuery 配置。最终应在同一个 PowerShell 窗口中按以下顺序执行：

```text
1. Set-Location 到项目根目录
2. 设置并验证 TZTEK_PARSER_PYTHON
3. 设置豆包和/或 GPT 环境变量
4. npm run build
5. dotnet run
```

后端启动后检查供应商状态：

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri 'http://localhost:5078/api/demo/drawing-assist/providers' |
  ConvertTo-Json -Depth 5
```

预期结果中，相应供应商的 `isConfigured` 应为 `true`。该接口不会返回 API Key。

页面测试时：

1. 上传 STEP 并确认工件实体正常显示。
2. 上传 PDF 工程图。
3. 打开“AI 辅助”。
4. 在“豆包 / GPT-5.6”中选择当前已配置的供应商。
5. 点击开始推荐，并根据页面诊断核对模型、request ID、耗时和 token。

每次识别只调用页面当前选择的供应商，失败时不会自动切换另一家，避免重复计费。
