# 豆包图纸辅助识别测试说明

本文用于测试整页 PDF 图纸识别、STEP 基元推荐和人工审核流程。

## 本轮改动

- PDF 每页渲染成一张完整 JPEG，保留三视图、尺寸线和引出线关系，不使用固定分块。
- 豆包请求使用整页 `input_image`，`max_output_tokens` 保持 `81920`。
- 请求增加 `thinking.type = disabled`，要求模型只返回最终 JSON。
- AI 只能返回当前 STEP 中存在的 Feature ID，非法 ID 会被过滤。
- 高置信度和低置信度推荐都会自动勾选。
- 低置信度结果标记为 `NeedsReview`，用户可以取消或修改。
- AI 推荐不会自动生成测量路径，必须人工审核后点击“生成测量路径”。
- 增加请求 ID、运行 ID、图片哈希、响应状态、耗时和 token 诊断。
- 不修改测点生成、路径规划和碰撞检测逻辑。

## 环境要求

- Windows PowerShell
- .NET SDK
- Node.js 和 npm
- 项目目录：`D:\summer_stage\project\TZTEK`
- STEP 解析使用项目自带的 `.venv-step` 环境

## 配置 API Key

API Key 只设置在当前 PowerShell 会话，不要写入代码或提交到 Git。

```powershell
cd D:\summer_stage\project\TZTEK

$env:ARK_API_KEY = "在这里填入火山方舟APIKey"
$env:ARK_BASE_URL = "https://ark.cn-beijing.volces.com/api/v3"
$env:ARK_VISION_ENDPOINT_ID = "在这里填入豆包视觉模型接入点ID"

$env:ARK_TIMEOUT_SECONDS = "300"
$env:ARK_RENDER_DPI = "220"
$env:ARK_RENDER_MAX_EDGE = "6500"
$env:ARK_RENDER_MAX_PIXELS = "32000000"

"API Key 已设置: " + (-not [string]::IsNullOrWhiteSpace($env:ARK_API_KEY))
"接入点 ID: " + $env:ARK_VISION_ENDPOINT_ID
"请求超时: " + $env:ARK_TIMEOUT_SECONDS
```

正常结果应为：

```text
API Key 已设置: True
接入点 ID: ep-...
请求超时: 300
```

## 构建后端

先关闭占用 5078 端口的旧进程，然后重新构建和启动：

```powershell
cd D:\summer_stage\project\TZTEK

Get-NetTCPConnection -LocalPort 5078 -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Stop-Process -Id $_ -Force }

dotnet build .\TZTEK.VispecCMM.Demo.Api\TZTEK.VispecCMM.Demo.Api.csproj --nologo

dotnet run --project .\TZTEK.VispecCMM.Demo.Api\TZTEK.VispecCMM.Demo.Api.csproj --no-launch-profile --urls http://localhost:5078
```

看到以下内容后保持窗口运行：

```text
Now listening on: http://localhost:5078
```

## 启动前端

新开一个 PowerShell 窗口：

```powershell
cd D:\summer_stage\project\TZTEK\TZTEK.VispecCMM.Demo.Web
npm run dev -- --host localhost
```

浏览器打开：

```text
http://localhost:5173
```

## 图纸识别测试步骤

1. 导入与图纸对应的 `.stp` 或 `.step` 文件。
2. 打开顶部的“AI 辅助”。
3. 上传对应的 PDF 图纸。
4. 点击“开始 AI 推荐”。
5. 等待状态依次显示：渲染图纸、识别页面、匹配基元、推荐完成。
6. 检查基元列表中的自动勾选结果。
7. 检查低置信度基元是否带有 `NeedsReview` 或低置信度提示。
8. 人工取消错误推荐或补选遗漏基元。
9. 点击“保存选择”。
10. 点击“生成测量路径”。

AI 推荐完成后不会自动生成路径，这是预期行为。

## 后端日志检查

每页成功时应看到类似日志：

```text
Volcengine response page=1 status=200 responseStatus=completed
requestId=resp_...
textLength=...
inputTokens=...
outputTokens=...
cachedTokens=...
cacheHit=...
runId=run_..._page_1
imageHash=...
```

重点检查：

- `status=200`：HTTP 调用成功。
- `responseStatus=completed`：模型完成响应。
- `textLength` 大于 0：程序成功提取最终文本。
- `outputTokens` 有值：模型产生了输出。
- `runId` 不为空：使用的是最新诊断代码。
- `imageHash` 不为空：整页图片已经进入当前请求。
- `requestId` 可用于火山方舟控制台查询。

客户端超过 300 秒时，日志应明确写出 `ClientTimeout`，不能直接判断为 max token 到达。

## 低置信度验收

低置信度但拥有合法 Feature ID 的推荐应当：

```text
自动加入勾选集合
状态显示 NeedsReview
显示黄色或低置信度提示
允许用户取消
不自动生成路径
```

没有合法 Feature ID 的结果不能勾选，因为 AI 不允许创建不存在的 STEP 基元。

## 失败场景

### 未配置 API Key

STEP 导入和人工选择仍应可用，AI 识别提示缺少 `ARK_API_KEY`。

### HTTP 400

检查接入点是否支持视觉输入，以及接入点 ID 是否正确。

### HTTP 401

检查 API Key 是否正确、是否过期，以及 Key 和接入点是否属于同一火山方舟账号/区域。

### HTTP 429

检查额度、并发限制和调用频率。

### 只有 thinking 没有最终 JSON

应显示响应协议错误，不应误认为 STEP 解析失败。

### AI 识别失败

人工选择、STEP 基元和已有测量路径不能被清空。

## 构建验证

后端：

```powershell
cd D:\summer_stage\project\TZTEK
dotnet build .\TZTEK.VispecCMM.Demo.Api\TZTEK.VispecCMM.Demo.Api.csproj --nologo
```

前端：

```powershell
cd D:\summer_stage\project\TZTEK\TZTEK.VispecCMM.Demo.Web
npm run build
```

## 测试结束后清理环境变量

```powershell
Remove-Item Env:ARK_API_KEY -ErrorAction SilentlyContinue
Remove-Item Env:ARK_VISION_ENDPOINT_ID -ErrorAction SilentlyContinue
Remove-Item Env:ARK_BASE_URL -ErrorAction SilentlyContinue
Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue
Remove-Item Env:OPENAI_BASE_URL -ErrorAction SilentlyContinue
Remove-Item Env:OPENAI_TIMEOUT_SECONDS -ErrorAction SilentlyContinue
Remove-Item Env:OPENAI_REASONING_EFFORT -ErrorAction SilentlyContinue
```

不要把真实 API Key 写入本文件。

## GPT-5.6 供应商测试

在启动后端的同一个 PowerShell 窗口设置 OpenAI 配置：

```powershell
Set-Location D:\summer_stage\project\TZTEK

$env:OPENAI_API_KEY = "在这里填入OpenAI API Key"
$env:OPENAI_MODEL = "gpt-5.6-sol"
$env:OPENAI_BASE_URL = "https://api.openai.com/v1"
$env:OPENAI_TIMEOUT_SECONDS = "300"
$env:OPENAI_REASONING_EFFORT = "medium"

dotnet run --project .\TZTEK.VispecCMM.Demo.Api\TZTEK.VispecCMM.Demo.Api.csproj --urls http://localhost:5078
```

检查后端识别到的供应商状态：

```powershell
Invoke-RestMethod http://localhost:5078/api/demo/drawing-assist/providers | Format-Table id,displayName,isConfigured,model,unavailableReason
```

预期 `openai` 行显示：

```text
isConfigured = True
model = gpt-5.6-sol
```

打开前端后，在 AI 辅助栏选择 `GPT-5.6`，再点击“开始 AI 推荐”。日志应包含 `OpenAI response`、HTTP 状态、request ID、模型和 token 用量，但不得出现 API Key、Base64 图片或完整响应。

也可以直接调用识别接口验证供应商参数：

```powershell
$sessionId = "替换为页面URL中的session参数"
$body = @{ provider = "openai" } | ConvertTo-Json
Invoke-RestMethod "http://localhost:5078/api/demo/sessions/$sessionId/drawing-assist" -Method Post -ContentType "application/json" -Body $body
```

将 `provider` 改为 `volcengine` 应只调用豆包。传入其他值应返回 HTTP 400，并且不能自动调用另一个供应商。
