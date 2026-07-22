import type { DemoExample, DemoSession } from './types'

async function readJson<T>(response: Response): Promise<T> {
  const payload = await response.json()
  if (!response.ok) throw new Error(payload.error ?? `请求失败：${response.status}`)
  return payload as T
}

export async function getExamples(): Promise<DemoExample[]> {
  return readJson(await fetch('/api/demo/examples'))
}

export async function createUploadSession(file: File): Promise<DemoSession> {
  const form = new FormData()
  form.append('file', file)
  return readJson(await fetch('/api/demo/sessions', { method: 'POST', body: form }))
}

export async function createExampleSession(id: string): Promise<DemoSession> {
  return readJson(await fetch(`/api/demo/sessions/examples/${encodeURIComponent(id)}`, { method: 'POST' }))
}

export async function getSession(id: string): Promise<DemoSession> {
  return readJson(await fetch(`/api/demo/sessions/${id}`))
}

export async function saveFeatureSelection(id: string, featureIds: string[]): Promise<void> {
  await readJson(await fetch(`/api/demo/sessions/${id}/features/selection`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ featureIds, selectionSource: 'Manual' }),
  }))
}

export async function generateMeasurementPlan(id: string, featureIds: string[]): Promise<DemoSession> {
  return readJson(await fetch(`/api/demo/sessions/${id}/measurement-plan`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ featureIds, pathStrategy: 'ImportOrder', enableCollisionCheck: true, enableContinuousFeaturePath: true }),
  }))
}

export async function clearMeasurementPlan(id: string): Promise<void> {
  await readJson(await fetch(`/api/demo/sessions/${id}/measurement-plan`, { method: 'DELETE' }))
}
