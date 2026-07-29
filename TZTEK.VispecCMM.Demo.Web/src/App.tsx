import { useEffect, useMemo, useRef, useState } from 'react'
import {
  Box,
  ChevronLeft,
  ChevronRight,
  Download,
  Eye,
  FileBox,
  FolderOpen,
  Gauge,
  Layers3,
  Pause,
  Play,
  RotateCcw,
  Search,
  Sparkles,
  Trash2,
  Upload,
} from 'lucide-react'
import { clearMeasurementPlan, createExampleSession, createUploadSession, deleteDrawing, generateMeasurementPlan, getExamples, getSession, saveFeatureSelection, setAiAssist, startDrawingAssist, uploadDrawing } from './api'
import Viewer from './Viewer'
import type { DemoExample, DemoSession, LayerState, VisualizationFeature, VisualizationResult } from './types'

const defaultLayers: LayerState = {
  workpiece: true,
  features: true,
  points: true,
  normals: false,
  path: true,
  goto: true,
  safety: false,
}

export default function App() {
  const [examples, setExamples] = useState<DemoExample[]>([])
  const [exampleId, setExampleId] = useState('cylinder')
  const [file, setFile] = useState<File>()
  const [session, setSession] = useState<DemoSession>()
  const [result, setResult] = useState<VisualizationResult>()
  const [error, setError] = useState('')
  const [query, setQuery] = useState('')
  const [selectedFeatureId, setSelectedFeatureId] = useState<string>()
  const [selectedFeatureIds, setSelectedFeatureIds] = useState<string[]>([])
  const [savedFeatureIds, setSavedFeatureIds] = useState<string[]>([])
  const [aiAssistEnabled, setAiAssistEnabled] = useState(false)
  const [drawingFile, setDrawingFile] = useState<File>()
  const [drawingMessage, setDrawingMessage] = useState('')
  const [isUploadingDrawing, setIsUploadingDrawing] = useState(false)
  const [isDrawingAssistRunning, setIsDrawingAssistRunning] = useState(false)
  const [drawingAssistProgress, setDrawingAssistProgress] = useState(0)
  const [drawingAssistPhase, setDrawingAssistPhase] = useState('')
  const [isGeneratingPlan, setIsGeneratingPlan] = useState(false)
  const [planProgress, setPlanProgress] = useState(0)
  const [currentStep, setCurrentStep] = useState(0)
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState(1)
  const [layers, setLayers] = useState(defaultLayers)
  const [layerMenu, setLayerMenu] = useState(false)
  const [cameraView, setCameraView] = useState('iso')
  const fileInput = useRef<HTMLInputElement>(null)
  const drawingInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    getExamples().then(values => {
      setExamples(values)
      if (values.length && !values.some(value => value.id === exampleId)) setExampleId(values[0].id)
    }).catch(() => setExamples([]))
  }, [])

  useEffect(() => {
    const id = new URLSearchParams(window.location.search).get('session')
    if (!id) return
    getSession(id).then(value => {
      setSession(value)
      setAiAssistEnabled(value.aiAssistEnabled)
      setDrawingAssistProgress(value.drawingAssistProgress ?? 0)
      setDrawingAssistPhase(describeDrawingAssistPhase(value.drawingAssistStatus, value.drawingAssistProgress ?? 0))
      setIsDrawingAssistRunning(isDrawingAssistActive(value.drawingAssistStatus))
      if (value.status === 'Completed' && value.result) {
        setResult(value.result)
        setSelectedFeatureId(value.result.features[0]?.id)
        setSelectedFeatureIds(value.selectedFeatureIds ?? [])
        setSavedFeatureIds(value.selectedFeatureIds ?? [])
      }
    }).catch(reason => setError(reason instanceof Error ? reason.message : '无法加载演示会话'))
  }, [])

  useEffect(() => {
    const aiAssistActive = isDrawingAssistRunning || ['RenderingDrawing', 'AnalyzingPages', 'MatchingFeatures'].includes(session?.drawingAssistStatus ?? '')
    if (!session || (!aiAssistActive && (session.status === 'Completed' || session.status === 'Failed'))) return
    const timer = window.setInterval(async () => {
      try {
        const next = await getSession(session.id)
        setSession(next)
        setAiAssistEnabled(next.aiAssistEnabled)
        setDrawingAssistProgress(next.drawingAssistProgress ?? 0)
        setDrawingAssistPhase(describeDrawingAssistPhase(next.drawingAssistStatus, next.drawingAssistProgress ?? 0))
        setIsDrawingAssistRunning(isDrawingAssistActive(next.drawingAssistStatus))
        if (next.status === 'Completed' && next.result) {
          setResult(next.result)
          setSelectedFeatureId(next.result.features[0]?.id)
          setSelectedFeatureIds(next.selectedFeatureIds ?? [])
          setSavedFeatureIds(next.selectedFeatureIds ?? [])
          setCurrentStep(0)
        }
        if (next.status === 'Failed') setError(next.error ?? '处理失败')
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : '无法读取会话状态')
      }
    }, 700)
    return () => window.clearInterval(timer)
  }, [session, isDrawingAssistRunning])

  const plan = result?.optimizedPlan ?? {
    name: '尚未生成路径',
    segments: [],
    statistics: {
      primitiveCount: 0,
      featureCount: 0,
      measurementPointCount: 0,
      movementCount: 0,
      measurementCount: 0,
      gotoCount: 0,
      autoGotoCount: 0,
      manualGotoCount: 0,
      totalPathLengthMm: 0,
      estimatedTimeSeconds: 0,
    },
  }
  const selectedFeature = result?.features.find(value => value.id === selectedFeatureId)
  const filteredFeatures = useMemo(() => {
    if (!result) return []
    const value = query.trim().toLowerCase()
    return value.length === 0
      ? result.features
      : result.features.filter(feature => `${feature.name} ${feature.type}`.toLowerCase().includes(value))
  }, [query, result])
  const measurementFeatures = filteredFeatures.filter(feature => feature.isMeasurementFeature)
  const rawFeatures = filteredFeatures.filter(feature => !feature.isMeasurementFeature)

  useEffect(() => { setCurrentStep(0); setPlaying(false) }, [result])

  useEffect(() => {
    if (!playing || !plan || plan.segments.length === 0) return
    const timer = window.setInterval(() => {
      setCurrentStep(value => {
        if (value >= plan.segments.length - 1) {
          setPlaying(false)
          return value
        }
        return value + 1
      })
    }, 420 / speed)
    return () => window.clearInterval(timer)
  }, [playing, plan, speed])

  const startUpload = async () => {
    if (!file) return
    await startSession(() => createUploadSession(file))
  }

  const startExample = async () => {
    await startSession(() => createExampleSession(exampleId))
  }

  const startSession = async (factory: () => Promise<DemoSession>) => {
    setError('')
    setResult(undefined)
    setPlaying(false)
    setSelectedFeatureIds([])
    setSavedFeatureIds([])
    setAiAssistEnabled(false)
    setDrawingFile(undefined)
    setDrawingMessage('')
    try {
      setSession(await factory())
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法创建演示会话')
    }
  }

  const toggleFeature = (id: string) => setSelectedFeatureIds(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id])
  const saveSelection = async () => {
    if (!session) return
    try {
      await saveFeatureSelection(session.id, selectedFeatureIds)
      setSavedFeatureIds([...selectedFeatureIds])
      setError('')
    } catch (reason) { setError(reason instanceof Error ? reason.message : '保存选择失败') }
  }
  const selectAllFeatures = () => { if (result) setSelectedFeatureIds(result.features.map(feature => feature.id)) }
  const clearSelection = () => setSelectedFeatureIds([])
  const generatePlan = async () => {
    if (!session || selectedFeatureIds.length === 0) return
    try {
      setError('')
      setIsGeneratingPlan(true)
      setPlanProgress(10)
      await new Promise(resolve => window.setTimeout(resolve, 120))
      setPlanProgress(30)
      const next = await generateMeasurementPlan(session.id, selectedFeatureIds)
      setPlanProgress(90)
      setSession(next)
      if (next.result) {
        setResult(next.result)
        setSelectedFeatureIds(next.selectedFeatureIds ?? selectedFeatureIds)
        setSavedFeatureIds(next.selectedFeatureIds ?? selectedFeatureIds)
        setSelectedFeatureId(next.result.features.find(feature => (next.selectedFeatureIds ?? selectedFeatureIds).includes(feature.id))?.id ?? next.result.features[0]?.id)
      }
      setPlanProgress(100)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '生成路径失败')
    } finally {
      window.setTimeout(() => setIsGeneratingPlan(false), 350)
    }
  }
  const clearPlan = async () => { if (session) { await clearMeasurementPlan(session.id); const next = await getSession(session.id); setSession(next); setResult(next.result) } }

  const toggleAiAssist = async (enabled: boolean) => {
    if (!session) return
    try {
      await setAiAssist(session.id, enabled)
      setAiAssistEnabled(enabled)
      setSession(current => current ? { ...current, aiAssistEnabled: enabled } : current)
      setDrawingMessage('')
    } catch (reason) { setError(reason instanceof Error ? reason.message : '无法设置 AI 辅助') }
  }

  const submitDrawing = async () => {
    if (!session || !drawingFile) return
    try {
      setIsUploadingDrawing(true)
      const uploaded = await uploadDrawing(session.id, drawingFile)
      setSession(current => current ? { ...current, drawingFile: uploaded, drawingAssistStatus: 'Ready', drawingAssistProgress: 0, aiRecommendations: [] } : current)
      setDrawingMessage('图纸已上传，尚未进行 AI 推荐')
      setError('')
    } catch (reason) { setError(reason instanceof Error ? reason.message : '上传图纸失败') }
    finally { setIsUploadingDrawing(false) }
  }

  const removeDrawing = async () => {
    if (!session) return
    try {
      await deleteDrawing(session.id)
      setDrawingFile(undefined)
      setSession(current => current ? { ...current, drawingFile: undefined, drawingAssistStatus: 'Idle', drawingAssistProgress: 0, aiRecommendations: [] } : current)
      setDrawingMessage('')
    } catch (reason) { setError(reason instanceof Error ? reason.message : '移除图纸失败') }
  }

  const requestDrawingAssist = async () => {
    if (!session) return
    try {
      setIsDrawingAssistRunning(true)
      setDrawingAssistProgress(5)
      setDrawingAssistPhase('渲染完整 PDF 页面')
      setSession(current => current ? { ...current, drawingAssistStatus: 'RenderingDrawing', drawingAssistProgress: 5 } : current)
      const response = await startDrawingAssist(session.id)
      setSession(current => current ? { ...current, drawingAssistStatus: response.status, drawingAssistProgress: response.progress, aiRecommendations: response.recommendations } : current)
      setDrawingAssistProgress(response.progress)
      setDrawingAssistPhase('推荐完成')
      const recommendedIds = response.recommendations.map(item => item.featureId)
      setSelectedFeatureIds(current => Array.from(new Set([...current, ...recommendedIds])))
      const pageDiagnostics = response.pageDiagnostics ?? []
      const successfulPages = pageDiagnostics.filter(page => page.success).length
      const failedPages = pageDiagnostics.length - successfulPages
      const requestSummary = response.requestId ? ` request ID：${response.requestId}` : ''
      const inputTokens = pageDiagnostics.reduce((sum, page) => sum + (page.inputTokens ?? 0), 0)
      const outputTokens = pageDiagnostics.reduce((sum, page) => sum + (page.outputTokens ?? 0), 0)
      const cachedTokens = pageDiagnostics.reduce((sum, page) => sum + (page.cachedTokens ?? 0), 0)
      const elapsed = pageDiagnostics.reduce((sum, page) => sum + page.elapsedMilliseconds, 0)
      const statuses = pageDiagnostics.filter(page => page.responseStatus || page.incompleteDetails).map(page => `第${page.pageNumber}页 ${page.responseStatus || ''}${page.incompleteDetails ? `（${page.incompleteDetails}）` : ''}`).join('；')
      setDrawingMessage(`${response.message} 模型：${response.model || 'Doubao'}；成功页面 ${successfulPages}，失败页面 ${failedPages}，已自动勾选 ${response.recommendedCount} 个候选，低置信 ${response.lowConfidenceCount} 个；耗时 ${elapsed}ms，输入 token ${inputTokens}，输出 token ${outputTokens}，缓存 token ${cachedTokens}。${statuses ? `状态：${statuses}。` : ''}${requestSummary}`)
      setError('')
    } catch (reason) {
      setDrawingAssistPhase('AI 推荐失败')
      setDrawingAssistProgress(0)
      setError(reason instanceof Error ? reason.message : 'AI 辅助请求失败')
    } finally {
      setIsDrawingAssistRunning(false)
    }
  }

  const exportJson = () => {
    if (!result) return
    const url = URL.createObjectURL(new Blob([JSON.stringify(result, null, 2)], { type: 'application/json' }))
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `${result.fileName}.visualization.json`
    anchor.click()
    URL.revokeObjectURL(url)
  }

  const currentSegment = plan?.segments[currentStep]
  const optimized = result?.optimizedPlan.statistics

  return (
    <main className={`app-shell ${result ? 'drawing-assist-visible' : ''}`}>
      <header className="topbar">
        <div className="brand-block">
          <Box size={21} />
          <div>
            <strong>Vispec CMM</strong>
            <span>测量任务演示台</span>
          </div>
        </div>

        <div className="file-actions">
          <input
            ref={fileInput}
            type="file"
            accept=".stp,.step"
            hidden
            onChange={event => setFile(event.target.files?.[0])}
          />
          <button className="icon-text-button" onClick={() => fileInput.current?.click()} title="选择文件">
            <FolderOpen size={17} />
            <span>{file?.name ?? '选择文件'}</span>
          </button>
          <button className="primary-button" disabled={!file || session?.status === 'Processing'} onClick={startUpload}>
            <Upload size={17} />
            开始解析
          </button>
          <label className="ai-toggle" title={!result ? '请先导入 STEP' : '开启 PDF 图纸辅助入口'}><input type="checkbox" checked={aiAssistEnabled} disabled={!result} onChange={event => toggleAiAssist(event.target.checked)} /> AI 辅助</label>
          <button className="icon-text-button" disabled={!result} onClick={saveSelection}>保存选择</button>
          <button className="primary-button" disabled={!result || selectedFeatureIds.length === 0 || session?.status === 'Processing' || isGeneratingPlan} onClick={generatePlan}>{isGeneratingPlan ? '正在生成…' : '生成测量路径'}</button>
          <button className="icon-button" disabled={!result?.optimizedPlan.statistics.featureCount} onClick={clearPlan} title="清除路径"><RotateCcw size={17} /></button>
          <div className="example-control">
            <select value={exampleId} onChange={event => setExampleId(event.target.value)} aria-label="示例文件">
              {examples.map(example => <option key={example.id} value={example.id}>{example.name}</option>)}
            </select>
            <button className="icon-button" disabled={!examples.length} onClick={startExample} title="加载示例">
              <FileBox size={18} />
            </button>
          </div>
        </div>

        <div className="topbar-tools">
          <button className="icon-button" disabled={!result} onClick={exportJson} title="导出可视化数据">
            <Download size={18} />
          </button>
          <span className="prototype-badge">原型级离线碰撞检测</span>
        </div>
      </header>

      {result && (
        <section className="drawing-assist-bar">
          <div className="drawing-assist-title"><Sparkles size={17} /><div><strong>2D 图纸辅助</strong><span>{aiAssistEnabled ? 'AI 辅助已开启，可上传对应 PDF' : '请先开启顶部 AI 辅助，再使用图纸推荐'}</span></div></div>
          <input ref={drawingInput} type="file" accept="application/pdf,.pdf" hidden onChange={event => setDrawingFile(event.target.files?.[0])} />
          <button className="icon-text-button" disabled={!aiAssistEnabled} onClick={() => drawingInput.current?.click()} title={!aiAssistEnabled ? '请先开启 AI 辅助' : '选择与 STEP 对应的 PDF 图纸'}><FolderOpen size={16} /><span>{drawingFile?.name ?? '选择 PDF'}</span></button>
          <button className="icon-text-button" disabled={!aiAssistEnabled || !drawingFile || isUploadingDrawing} onClick={submitDrawing}><Upload size={16} />{isUploadingDrawing ? '上传中…' : '上传图纸'}</button>
          {session?.drawingFile && <div className="drawing-file-state"><strong>{session.drawingFile.fileName}</strong><span>{formatFileSize(session.drawingFile.sizeBytes)} · 已上传</span></div>}
          <button className="primary-button" disabled={!session?.drawingFile || isDrawingAssistRunning} onClick={requestDrawingAssist}><Sparkles size={16} />{isDrawingAssistRunning ? 'AI 推荐中…' : '开始 AI 推荐'}</button>
          <button className="icon-button" disabled={!session?.drawingFile} onClick={removeDrawing} title="移除图纸"><Trash2 size={16} /></button>
          <div className="drawing-assist-status">
            <span className="drawing-assist-message">{drawingMessage || drawingAssistPhase || '等待 AI 推荐'}</span>
            {(isDrawingAssistRunning || drawingAssistProgress > 0 || session?.drawingAssistStatus === 'Failed') && (
              <>
                <div className="drawing-assist-progress-track"><i style={{ width: `${drawingAssistProgress}%` }} /></div>
                <small>{drawingAssistPhase || describeDrawingAssistPhase(session?.drawingAssistStatus ?? '', drawingAssistProgress)}</small>
              </>
            )}
          </div>
        </section>
      )}

      <section className="summary-strip">
        <Metric label="基元" value={optimized?.primitiveCount ?? 0} />
        <Metric label="测量特征" value={optimized?.featureCount ?? 0} />
        <Metric label="测点" value={optimized?.measurementPointCount ?? 0} />
        <Metric label="自动 GOTO" value={optimized?.autoGotoCount ?? 0} accent="blue" />
        <Metric label="人工 GOTO" value={optimized?.manualGotoCount ?? 0} accent={optimized?.manualGotoCount ? 'red' : undefined} />
        <Metric label="路径长度" value={optimized ? `${optimized.totalPathLengthMm.toFixed(1)} mm` : '0 mm'} />
        <Metric label="已选择" value={selectedFeatureIds.length} accent="amber" />
      </section>

      <section className="workspace">
        <aside className="feature-panel">
          <div className="panel-heading">
            <div>
              <h2>识别元素</h2>
              <span>已识别：{result?.features.length ?? 0} · 已选择：{selectedFeatureIds.length}</span>
            </div>
            <div className="feature-selection-tools">
              <button onClick={selectAllFeatures} disabled={!result?.features.length}>全选</button>
              <button onClick={clearSelection} disabled={selectedFeatureIds.length === 0}>清空</button>
            </div>
          </div>
          <div className="selection-status">
            {selectedFeatureIds.length === 0 ? '请勾选需要测量的基元' : selectedFeatureIds.join('|') !== savedFeatureIds.join('|') ? '有未保存的基元选择' : '选择已保存'}
          </div>
          <label className="search-box">
            <Search size={15} />
            <input value={query} onChange={event => setQuery(event.target.value)} placeholder="搜索名称或类型" />
          </label>
          <div className="feature-list">
            {measurementFeatures.length > 0 && <div className="feature-group-label">测量特征</div>}
            {measurementFeatures.map(feature => <FeatureRow key={feature.id} feature={feature} selected={feature.id === selectedFeatureId} checked={selectedFeatureIds.includes(feature.id)} onSelect={setSelectedFeatureId} onToggle={toggleFeature} />)}
            {rawFeatures.length > 0 && <div className="feature-group-label">原始 CAD 曲面</div>}
            {rawFeatures.map(feature => <FeatureRow key={feature.id} feature={feature} selected={feature.id === selectedFeatureId} checked={selectedFeatureIds.includes(feature.id)} onSelect={setSelectedFeatureId} onToggle={toggleFeature} />)}
            {!result && <EmptyList />}
          </div>
        </aside>

        <section className="viewport-panel">
          <div className="viewport-toolbar">
            <div className="segmented-control">
              <span>人工选择路径</span>
            </div>
            <div className="camera-controls">
              {['top', 'front', 'side', 'iso'].map(view => (
                <button key={view} className={`view-button ${cameraView === view ? 'active' : ''}`} onClick={() => setCameraView(view)}>
                  {view === 'top' ? '顶' : view === 'front' ? '前' : view === 'side' ? '侧' : '轴测'}
                </button>
              ))}
              <div className="layer-menu-wrap">
                <button className="icon-button" onClick={() => setLayerMenu(value => !value)} title="图层">
                  <Layers3 size={17} />
                </button>
                {layerMenu && (
                  <div className="layer-menu">
                    {Object.entries(layers).map(([key, value]) => (
                      <label key={key}>
                        <input type="checkbox" checked={value} onChange={() => setLayers(current => ({ ...current, [key]: !current[key as keyof LayerState] }))} />
                        {layerLabel(key)}
                      </label>
                    ))}
                  </div>
                )}
              </div>
              <button className="icon-button" onClick={() => setCameraView('iso')} title="复位视角"><RotateCcw size={17} /></button>
            </div>
          </div>

          <div className="viewport-stage">
            {result ? (
              <Viewer
                result={result}
                plan={plan}
                currentStep={currentStep}
                selectedFeatureId={selectedFeatureId}
                layers={layers}
                cameraView={cameraView}
                onSelectFeature={setSelectedFeatureId}
              />
            ) : (
              <WorkspaceEmpty />
            )}
            {isGeneratingPlan && (
              <div className="plan-generation-overlay">
                <div className="plan-generation-panel">
                  <strong>正在生成测量路径</strong>
                  <span>已选择 {selectedFeatureIds.length} 个基元</span>
                  <div className="plan-generation-track"><i style={{ width: `${planProgress}%` }} /></div>
                  <small>{planProgress < 30 ? '准备已选基元' : planProgress < 90 ? '生成测点并执行安全路径与碰撞检查' : '整理路径结果'}</small>
                  <em>{planProgress}%</em>
                </div>
              </div>
            )}
            {session && session.status !== 'Completed' && session.status !== 'Failed' && (
              <div className="processing-overlay">
                <Gauge size={28} />
                <strong>{session.stage}</strong>
                <div className="progress-track"><span style={{ width: `${session.progress}%` }} /></div>
                <span>{session.progress}%</span>
              </div>
            )}
            {error && <div className="error-banner">{error}</div>}
            <div className="legend">
              <Legend color="#36c5b4" label="普通移动" />
              <Legend color="#f3c64e" label="测量" />
              <Legend color="#4d8dff" label="GOTO" />
              <Legend color="#ef5b5b" label="风险" />
            </div>
          </div>
        </section>

        <aside className="detail-panel">
          <div className="panel-heading">
            <div>
              <h2>元素详情</h2>
              <span>{selectedFeature?.name ?? '未选择'}</span>
            </div>
            <Eye size={17} />
          </div>
          {selectedFeature ? <FeatureDetails feature={selectedFeature} /> : <DetailEmpty />}
          <div className="detail-section">
            <h3>探针</h3>
            <InfoRow label="型号" value={result?.probe?.name ?? 'Default Touch Probe'} />
            <InfoRow label="类型" value={result?.probe?.type ?? 'TouchTrigger'} />
            <InfoRow label="球径" value={`${(result?.probe?.tipDiameterMm ?? 2).toFixed(1)} mm`} />
            <InfoRow label="杆长" value={`${(result?.probe?.tipLengthMm ?? 20).toFixed(1)} mm`} />
          </div>
          <div className="detail-section warning-section">
            <h3>状态</h3>
            {(result?.warnings ?? []).map((warning, index) => (
              <p key={index} className={warning.level.toLowerCase()}>{warning.message}</p>
            ))}
          </div>
        </aside>
      </section>

      <footer className="timeline-panel">
        <div className="playback-controls">
          <button className="icon-button" disabled={!plan || currentStep === 0} onClick={() => setCurrentStep(value => Math.max(0, value - 1))}><ChevronLeft size={18} /></button>
          <button className="play-button" disabled={!plan?.segments.length} onClick={() => setPlaying(value => !value)}>
            {playing ? <Pause size={18} /> : <Play size={18} />}
          </button>
          <button className="icon-button" disabled={!plan || currentStep >= (plan?.segments.length ?? 1) - 1} onClick={() => setCurrentStep(value => Math.min((plan?.segments.length ?? 1) - 1, value + 1))}><ChevronRight size={18} /></button>
          <button className="speed-button" onClick={() => setSpeed(value => value === 1 ? 2 : value === 2 ? 4 : 1)}>{speed}x</button>
        </div>
        <div className="timeline-track-wrap">
          <div className="timeline-caption">
            <strong>{currentSegment?.name ?? '等待生成路径'}</strong>
            <span>{plan?.segments.length ? `${currentStep + 1} / ${plan.segments.length}` : '0 / 0'}</span>
          </div>
          <input
            className="timeline-range"
            type="range"
            min={0}
            max={Math.max((plan?.segments.length ?? 1) - 1, 0)}
            value={Math.min(currentStep, Math.max((plan?.segments.length ?? 1) - 1, 0))}
            onChange={event => setCurrentStep(Number(event.target.value))}
          />
        </div>
        <div className="current-step-data">
          <span>{currentSegment?.kind ?? '—'}</span>
          <strong>{currentSegment ? `${currentSegment.distanceMm.toFixed(2)} mm` : '0.00 mm'}</strong>
        </div>
      </footer>
    </main>
  )
}

function Metric({ label, value, accent }: { label: string; value: string | number; accent?: string }) {
  return <div className={`metric ${accent ? `accent-${accent}` : ''}`}><span>{label}</span><strong>{value}</strong></div>
}

function FeatureDetails({ feature }: { feature: VisualizationFeature }) {
  return (
    <div className="detail-section feature-details">
      <div className="type-title"><span className={`feature-swatch type-${feature.type.toLowerCase()}`} /><strong>{feature.type}</strong></div>
      <InfoRow label="位置" value={formatVector(feature.position)} />
      <InfoRow label="方向" value={formatVector(feature.direction)} />
      {feature.radius != null && <InfoRow label="半径" value={`${feature.radius.toFixed(3)} mm`} />}
      {feature.length != null && <InfoRow label="长度" value={`${feature.length.toFixed(3)} mm`} />}
      {feature.axisStart && <InfoRow label="轴向起点" value={formatVector(feature.axisStart)} />}
      {feature.axisEnd && <InfoRow label="轴向终点" value={formatVector(feature.axisEnd)} />}
      {feature.type === 'Cylinder' && <InfoRow label="表面" value={feature.isInnerSurface == null ? '未确定' : feature.isInnerSurface ? '内孔壁' : '外圆柱面'} />}
      {feature.sourceElementIds.length > 0 && <InfoRow label="来源面" value={feature.sourceElementIds.join(', ')} />}
      {feature.area != null && <InfoRow label="面积" value={`${feature.area.toFixed(2)} mm²`} />}
      <InfoRow label="测点" value={`${feature.measurementPoints.length}`} />
      {feature.requiresProbeReorientation && <InfoRow label="可达性" value="需转角测头或侧向探针" />}
      <InfoRow label="拟合" value={feature.fittingMethod ?? '—'} />
      <InfoRow label="公差" value={feature.tolerances.length ? feature.tolerances.join(', ') : '无'} />
    </div>
  )
}

function FeatureRow({ feature, selected, checked, onSelect, onToggle }: { feature: VisualizationFeature; selected: boolean; checked: boolean; onSelect: (id: string) => void; onToggle: (id: string) => void }) {
  return (
    <div className={`feature-row ${selected ? 'selected' : ''}`} onClick={() => onSelect(feature.id)}>
      <input className="feature-select-checkbox" type="checkbox" checked={checked} onChange={() => onToggle(feature.id)} onClick={event => event.stopPropagation()} aria-label={`选择 ${feature.name}`} />
      <span className={`feature-swatch type-${feature.type.toLowerCase()}`} />
      <span className="feature-copy">
        <strong>{feature.name}</strong>
        <small>{feature.type} · {feature.measurementPoints.length} 点</small>
      </span>
      <ChevronRight size={14} />
    </div>
  )
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return <div className="info-row"><span>{label}</span><strong>{value}</strong></div>
}

function Legend({ color, label }: { color: string; label: string }) {
  return <span><i style={{ backgroundColor: color }} />{label}</span>
}

function EmptyList() {
  return <div className="panel-empty"><FileBox size={24} /><span>暂无测量特征</span></div>
}

function DetailEmpty() {
  return <div className="panel-empty detail-empty"><Eye size={24} /><span>选择一个测量特征</span></div>
}

function WorkspaceEmpty() {
  return (
    <div className="workspace-empty">
      <Box size={42} />
      <strong>等待导入工件</strong>
      <span>支持 STP 和 STEP</span>
    </div>
  )
}

function formatVector(value: [number, number, number]) {
  return value.map(item => item.toFixed(2)).join(', ')
}

function formatFileSize(bytes: number) {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`
}

function isDrawingAssistActive(status: string) {
  return status === 'RenderingDrawing' || status === 'AnalyzingPages' || status === 'MatchingFeatures'
}

function describeDrawingAssistPhase(status: string, progress: number) {
  if (status === 'Completed') return '推荐完成'
  if (status === 'Failed') return 'AI 推荐失败'
  if (status === 'RenderingDrawing') return '渲染完整 PDF 页面'
  if (status === 'AnalyzingPages') return '豆包整页识别'
  if (status === 'MatchingFeatures') return '匹配 STEP 基元'
  return progress > 0 ? '正在处理' : '等待 AI 推荐'
}

function layerLabel(key: string) {
  const labels: Record<string, string> = {
    workpiece: '工件实体',
    features: '识别基元',
    points: '测量点',
    normals: '测点法向',
    path: '测量路径',
    goto: 'GOTO 点',
    safety: '安全高度',
  }
  return labels[key] ?? key
}
