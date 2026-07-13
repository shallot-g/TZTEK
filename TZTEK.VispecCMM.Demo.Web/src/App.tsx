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
  Upload,
} from 'lucide-react'
import { createExampleSession, createUploadSession, getExamples, getSession } from './api'
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
  const [planMode, setPlanMode] = useState<'baseline' | 'optimized'>('optimized')
  const [currentStep, setCurrentStep] = useState(0)
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState(1)
  const [layers, setLayers] = useState(defaultLayers)
  const [layerMenu, setLayerMenu] = useState(false)
  const [cameraView, setCameraView] = useState('iso')
  const fileInput = useRef<HTMLInputElement>(null)

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
      if (value.status === 'Completed' && value.result) {
        setResult(value.result)
        setSelectedFeatureId(value.result.features[0]?.id)
      }
    }).catch(reason => setError(reason instanceof Error ? reason.message : '无法加载演示会话'))
  }, [])

  useEffect(() => {
    if (!session || session.status === 'Completed' || session.status === 'Failed') return
    const timer = window.setInterval(async () => {
      try {
        const next = await getSession(session.id)
        setSession(next)
        if (next.status === 'Completed' && next.result) {
          setResult(next.result)
          setSelectedFeatureId(next.result.features[0]?.id)
          setCurrentStep(0)
        }
        if (next.status === 'Failed') setError(next.error ?? '处理失败')
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : '无法读取会话状态')
      }
    }, 700)
    return () => window.clearInterval(timer)
  }, [session])

  const plan = result ? (planMode === 'optimized' ? result.optimizedPlan : result.baselinePlan) : undefined
  const selectedFeature = result?.features.find(value => value.id === selectedFeatureId)
  const filteredFeatures = useMemo(() => {
    if (!result) return []
    const value = query.trim().toLowerCase()
    return value.length === 0
      ? result.features
      : result.features.filter(feature => `${feature.name} ${feature.type}`.toLowerCase().includes(value))
  }, [query, result])

  useEffect(() => {
    setCurrentStep(0)
    setPlaying(false)
  }, [planMode, result])

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
    try {
      setSession(await factory())
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法创建演示会话')
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
  const baseline = result?.baselinePlan.statistics
  const optimized = result?.optimizedPlan.statistics
  const reduction = baseline && optimized && baseline.totalPathLengthMm > 0
    ? (1 - optimized.totalPathLengthMm / baseline.totalPathLengthMm) * 100
    : 0

  return (
    <main className="app-shell">
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

      <section className="summary-strip">
        <Metric label="基元" value={optimized?.primitiveCount ?? 0} />
        <Metric label="测量特征" value={optimized?.featureCount ?? 0} />
        <Metric label="测点" value={optimized?.measurementPointCount ?? 0} />
        <Metric label="自动 GOTO" value={optimized?.autoGotoCount ?? 0} accent="blue" />
        <Metric label="人工 GOTO" value={optimized?.manualGotoCount ?? 0} accent={optimized?.manualGotoCount ? 'red' : undefined} />
        <Metric label="路径长度" value={optimized ? `${optimized.totalPathLengthMm.toFixed(1)} mm` : '0 mm'} />
        <Metric label="缩短" value={`${Math.max(reduction, 0).toFixed(1)}%`} accent="amber" />
      </section>

      <section className="workspace">
        <aside className="feature-panel">
          <div className="panel-heading">
            <div>
              <h2>识别元素</h2>
              <span>{filteredFeatures.length} / {result?.features.length ?? 0}</span>
            </div>
          </div>
          <label className="search-box">
            <Search size={15} />
            <input value={query} onChange={event => setQuery(event.target.value)} placeholder="搜索名称或类型" />
          </label>
          <div className="feature-list">
            {filteredFeatures.map(feature => (
              <button
                key={feature.id}
                className={`feature-row ${feature.id === selectedFeatureId ? 'selected' : ''}`}
                onClick={() => setSelectedFeatureId(feature.id)}
              >
                <span className={`feature-swatch type-${feature.type.toLowerCase()}`} />
                <span className="feature-copy">
                  <strong>{feature.name}</strong>
                  <small>{feature.type} · {feature.measurementPoints.length} 点</small>
                </span>
                <ChevronRight size={14} />
              </button>
            ))}
            {!result && <EmptyList />}
          </div>
        </aside>

        <section className="viewport-panel">
          <div className="viewport-toolbar">
            <div className="segmented-control">
              <button className={planMode === 'baseline' ? 'active' : ''} onClick={() => setPlanMode('baseline')}>基础路径</button>
              <button className={planMode === 'optimized' ? 'active' : ''} onClick={() => setPlanMode('optimized')}>优化路径</button>
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
            {result && plan ? (
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
      {feature.area != null && <InfoRow label="面积" value={`${feature.area.toFixed(2)} mm²`} />}
      <InfoRow label="测点" value={`${feature.measurementPoints.length}`} />
      <InfoRow label="拟合" value={feature.fittingMethod ?? '—'} />
      <InfoRow label="公差" value={feature.tolerances.length ? feature.tolerances.join(', ') : '无'} />
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
