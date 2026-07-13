import { expect, test } from '@playwright/test'
import { PNG } from 'pngjs'

let sessionId = ''

test.beforeAll(async ({ request }) => {
  const created = await request.post('/api/demo/sessions/examples/cylinder')
  expect(created.ok()).toBeTruthy()
  sessionId = (await created.json()).id

  for (let attempt = 0; attempt < 120; attempt++) {
    const response = await request.get(`/api/demo/sessions/${sessionId}`)
    const session = await response.json()
    if (session.status === 'Completed') return
    if (session.status === 'Failed') throw new Error(session.error)
    await new Promise(resolve => setTimeout(resolve, 500))
  }
  throw new Error('演示会话处理超时')
})

for (const viewport of [
  { width: 1366, height: 768 },
  { width: 1440, height: 900 },
  { width: 1920, height: 1080 },
]) {
  test(`工作台 ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await page.goto(`/?session=${sessionId}`)
    await expect(page.getByText('Vispec CMM')).toBeVisible()
    await expect(page.getByText('98', { exact: true })).toBeVisible()
    const canvas = page.locator('.viewer-host canvas')
    await expect(canvas).toBeVisible()
    await page.waitForTimeout(1500)

    const boxes = await Promise.all([
      page.locator('.feature-panel').boundingBox(),
      page.locator('.viewport-panel').boundingBox(),
      page.locator('.detail-panel').boundingBox(),
      page.locator('.timeline-panel').boundingBox(),
    ])
    expect(boxes.every(Boolean)).toBeTruthy()
    expect(boxes[0]!.x + boxes[0]!.width).toBeLessThanOrEqual(boxes[1]!.x + 1)
    expect(boxes[1]!.x + boxes[1]!.width).toBeLessThanOrEqual(boxes[2]!.x + 1)
    expect(boxes[1]!.y + boxes[1]!.height).toBeLessThanOrEqual(boxes[3]!.y + 1)

    const canvasBox = await canvas.boundingBox()
    expect(canvasBox!.width).toBeGreaterThan(400)
    expect(canvasBox!.height).toBeGreaterThan(300)
    const image = PNG.sync.read(await canvas.screenshot())
    const unique = new Set<number>()
    for (let index = 0; index < image.data.length; index += 64) {
      unique.add((image.data[index] << 16) | (image.data[index + 1] << 8) | image.data[index + 2])
      if (unique.size > 40) break
    }
    expect(unique.size).toBeGreaterThan(40)

    await page.screenshot({
      path: `test-results/demo-${viewport.width}x${viewport.height}.png`,
      fullPage: true,
    })
  })
}
