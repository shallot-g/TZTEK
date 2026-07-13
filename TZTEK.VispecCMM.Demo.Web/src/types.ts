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
  result?: VisualizationResult
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
  area?: number
  angleRad?: number
  surfaceType?: string
  fittingMethod?: string
  tolerances: string[]
  measurementPoints: MeasurementPoint[]
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
