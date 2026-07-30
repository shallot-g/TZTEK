export type Vec3 = [number, number, number]

export interface DemoExample {
  id: string
  name: string
  description: string
}

export interface DemoSession {
  id: string
  status: 'Queued' | 'Processing' | 'Completed' | 'Failed'
  progress: number
  stage: string
  error?: string
  workflowStage: string
  selectedFeatureIds: string[]
  aiAssistEnabled: boolean
  drawingFile?: DrawingFile
  drawingAssistStatus: string
  drawingAssistProgress: number
  aiRecommendations: AiFeatureRecommendation[]
  result?: VisualizationResult
}

export interface DrawingFile {
  fileName: string
  sizeBytes: number
  uploadedAt: string
  status: string
}

export interface DrawingAssistResult {
  status: string
  message: string
  progress: number
  recommendations: AiFeatureRecommendation[]
  targetCount: number
  recommendedCount: number
  lowConfidenceCount: number
  requestId?: string
  elapsedMilliseconds: number
  warnings: string[]
  pageDiagnostics: DrawingAssistPageDiagnostic[]
  model: string
  pageCount: number
  successPageCount: number
  failedPageCount: number
}

export interface DrawingAssistPageDiagnostic {
  runId: string
  pdfHash: string
  imageHash: string
  promptVersion: string
  pageNumber: number
  httpStatusCode?: number
  requestId?: string
  model: string
  responseFields: string
  outputCount: number
  choicesCount: number
  contentKinds: string
  responseTextLength: number
  elapsedMilliseconds: number
  success: boolean
  responseStatus: string
  incompleteDetails?: string
  finishReason?: string
  inputTokens?: number
  outputTokens?: number
  totalTokens?: number
  cachedTokens?: number
  cacheHit: boolean
  startedAt: string
  completedAt: string
}

export interface VisualizationResult {
  sessionId: string
  fileName: string
  sourceType: string
  modelUrl?: string
  features: VisualizationFeature[]
  baselinePlan: PathPlan
  optimizedPlan: PathPlan
  probe?: ProbeInfo
  warnings: WarningInfo[]
  bounds: { min: Vec3; max: Vec3 }
}

export interface VisualizationFeature {
  id: string
  name: string
  type: string
  position: Vec3
  direction: Vec3
  radius?: number
  length?: number
  axisStart?: Vec3
  axisEnd?: Vec3
  startAngleRad?: number
  angularSpanRad?: number
  radialReference?: Vec3
  isInnerSurface?: boolean
  sourceElementIds: string[]
  isMeasurementFeature: boolean
  requiresProbeReorientation: boolean
  area?: number
  angleRad?: number
  coneLength?: number
  coneAxisStart?: Vec3
  coneAxisEnd?: Vec3
  coneRefRadius?: number
  coneRadiusStart?: number
  coneRadiusEnd?: number
  surfaceType?: string
  fittingMethod?: string
  tolerances: string[]
  measurementPoints: MeasurementPoint[]
}

export interface AiFeatureRecommendation {
  featureId: string
  status: string
  confidence: number
  reason: string
  pageNumber?: number
  annotationId?: string
  alternativeFeatureIds: string[]
}

export interface MeasurementPoint {
  index: number
  position: Vec3
  normal: Vec3
  approachDistance: number
  retractDistance: number
  searchDistance: number
}

export interface PathPlan {
  name: string
  segments: PathSegment[]
  statistics: Statistics
}

export interface PathSegment {
  sequence: number
  kind: 'Movement' | 'Measurement'
  name: string
  featureId?: string
  start: Vec3
  end: Vec3
  isGoto: boolean
  isAutoGoto: boolean
  hasRisk: boolean
  isExecutable: boolean
  requiresManualGoto: boolean
  collisionValidated: boolean
  movementKind: string
  reason?: string
  distanceMm: number
}

export interface Statistics {
  primitiveCount: number
  featureCount: number
  measurementPointCount: number
  movementCount: number
  measurementCount: number
  gotoCount: number
  autoGotoCount: number
  manualGotoCount: number
  collisionRiskCount: number
  unexecutableCount: number
  totalPathLengthMm: number
  estimatedTimeSeconds: number
}

export interface ProbeInfo {
  name: string
  type: string
  tipDiameterMm: number
  tipLengthMm: number
  angleADeg: number
  angleBDeg: number
}

export interface WarningInfo {
  level: string
  message: string
}

export interface LayerState {
  workpiece: boolean
  features: boolean
  points: boolean
  normals: boolean
  path: boolean
  goto: boolean
  safety: boolean
}
