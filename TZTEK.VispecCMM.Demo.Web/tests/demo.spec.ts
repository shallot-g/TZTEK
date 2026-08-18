import { expect, test } from '@playwright/test'
import { PNG } from 'pngjs'

let sessionId = ''
let sessionResult: any

test.beforeAll(async ({ request }) => {
  const created = await request.post('/api/demo/sessions/examples/cylinder')
  expect(created.ok()).toBeTruthy()
  sessionId = (await created.json()).id

  for (let attempt = 0; attempt < 120; attempt++) {
    const response = await request.get(`/api/demo/sessions/${sessionId}`)
    const session = await response.json()
    if (session.status === 'Completed') {
      sessionResult = session.result
      return
    }
    if (session.status === 'Failed') throw new Error(session.error)
    await new Promise(resolve => setTimeout(resolve, 500))
  }
  throw new Error('演示会话处理超时')
})

test('圆柱使用有限曲面范围生成基元和测点', async () => {
  const cylinders = sessionResult.features.filter((feature: any) => feature.isMeasurementFeature && feature.type === 'Cylinder')
  expect(cylinders).toHaveLength(2)

  for (const cylinder of cylinders) {
    expect(cylinder.position[2]).toBeCloseTo(-128, 6)
    expect(cylinder.length).toBeCloseTo(256, 6)
    expect(cylinder.angularSpanRad).toBeCloseTo(Math.PI * 2, 6)
    expect(cylinder.sourceElementIds).toHaveLength(2)
    expect(cylinder.measurementPoints).toHaveLength(24)
    const levels = [...new Set(cylinder.measurementPoints.map((point: any) => Number(point.position[2].toFixed(3))))]
    expect(levels).toEqual([-204.8, -128, -51.2])
  }

  const inner = cylinders.find((feature: any) => feature.isInnerSurface)
  const outer = cylinders.find((feature: any) => feature.isInnerSurface === false)
  expect(inner.measurementPoints[0].normal[0]).toBeCloseTo(-1, 6)
  expect(outer.measurementPoints[0].normal[0]).toBeCloseTo(1, 6)

  const entry = sessionResult.optimizedPlan.segments.find((segment: any) =>
    segment.featureId === inner.id && segment.name === 'Enter cylinder measurement path')
  const firstPoint = inner.measurementPoints[0]
  const approachOffset = entry.end.map((value: number, index: number) => value - firstPoint.position[index])
  const outwardDistance = approachOffset.reduce((sum: number, value: number, index: number) => sum + value * firstPoint.normal[index], 0)
  expect(outwardDistance).toBeGreaterThan(0)
})

test('批量选择只作用于当前搜索结果', async ({ page }) => {
  await page.goto(`/?session=${sessionId}`)
  const search = page.getByPlaceholder('搜索名称或类型')
  const selectAll = page.getByRole('button', { name: '全选', exact: true })
  const clear = page.getByRole('button', { name: '清空', exact: true })
  const rows = page.locator('.feature-row')
  await expect(rows.first()).toBeVisible()

  const hiddenFeature = sessionResult.features.find((feature: any) =>
    !`${feature.name} ${feature.type}`.toLowerCase().includes('cy'))
  expect(hiddenFeature).toBeTruthy()
  await page.getByRole('checkbox', { name: `选择 ${hiddenFeature.name}` }).check()

  await search.fill('CY')
  const visibleRows = page.locator('.feature-row')
  const visibleCount = await visibleRows.count()
  expect(visibleCount).toBeGreaterThan(0)
  await expect(page.getByText(new RegExp(`显示：${visibleCount} / 已识别：`))).toBeVisible()

  await selectAll.click()
  await expect(visibleRows.locator('input[type="checkbox"]:checked')).toHaveCount(visibleCount)
  await expect(selectAll).toBeDisabled()

  await clear.click()
  await expect(visibleRows.locator('input[type="checkbox"]:checked')).toHaveCount(0)
  await expect(clear).toBeDisabled()

  await search.fill('')
  await expect(page.getByRole('checkbox', { name: `选择 ${hiddenFeature.name}` })).toBeChecked()

  await search.fill('no-such-feature')
  await expect(page.getByText('没有匹配的基元')).toBeVisible()
  await expect(selectAll).toBeDisabled()
  await expect(clear).toBeDisabled()
  await expect(page.getByLabel('示例文件')).toHaveCount(0)
  await expect(page.getByTitle('加载示例')).toHaveCount(0)
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
    await expect(page.locator('.feature-group-label').filter({ hasText: /^测量特征$/ })).toBeVisible()
    await expect(page.locator('.feature-group-label').filter({ hasText: /^原始 CAD 曲面$/ })).toBeVisible()
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
