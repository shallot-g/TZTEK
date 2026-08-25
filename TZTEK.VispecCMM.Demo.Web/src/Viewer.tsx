import { useEffect, useRef } from 'react'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { STLLoader } from 'three/examples/jsm/loaders/STLLoader.js'
import type { LayerState, PathPlan, VisualizationFeature, VisualizationResult } from './types'

interface ViewerProps {
  result: VisualizationResult
  plan: PathPlan
  currentStep: number
  selectedFeatureId?: string
  selectedFeatureIds: string[]
  selectedFeaturesIsolationMode: boolean
  layers: LayerState
  cameraView: string
  onSelectFeature: (id?: string) => void
}

const colors = {
  movement: 0x36c5b4,
  measurement: 0xf3c64e,
  goto: 0x4d8dff,
  risk: 0xef5b5b,
  feature: 0x50a8d8,
  innerFeature: 0x42c9b8,
  selected: 0xffffff,
  point: 0xff9638,
}

export default function Viewer({ result, plan, currentStep, selectedFeatureId, selectedFeatureIds, selectedFeaturesIsolationMode, layers, cameraView, onSelectFeature }: ViewerProps) {
  const hostRef = useRef<HTMLDivElement>(null)
  const cameraSnapshotRef = useRef<{
    sessionId: string
    position: THREE.Vector3
    target: THREE.Vector3
  } | null>(null)
  const stateRef = useRef<{
    renderer: THREE.WebGLRenderer
    scene: THREE.Scene
    camera: THREE.PerspectiveCamera
    controls: OrbitControls
    completed: THREE.LineSegments
    future: THREE.LineSegments
    risk: THREE.LineSegments
    active: THREE.Line
    probe: THREE.Group
    featureObjects: THREE.Object3D[]
    frame: number
  } | null>(null)

  useEffect(() => {
    const host = hostRef.current
    if (!host) return

    const scene = new THREE.Scene()
    scene.background = new THREE.Color(0x101419)
    const camera = new THREE.PerspectiveCamera(42, 1, 0.1, 100000)
    const renderer = new THREE.WebGLRenderer({ antialias: true })
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
    renderer.outputColorSpace = THREE.SRGBColorSpace
    host.replaceChildren(renderer.domElement)

    const controls = new OrbitControls(camera, renderer.domElement)
    controls.enableDamping = true
    controls.dampingFactor = 0.08
    scene.add(new THREE.HemisphereLight(0xffffff, 0x28303a, 1.8))
    const key = new THREE.DirectionalLight(0xffffff, 2.2)
    key.position.set(1, 1, 2)
    scene.add(key)

    const grid = new THREE.GridHelper(300, 30, 0x3a4652, 0x252d35)
    grid.rotation.x = Math.PI / 2
    grid.position.z = result.bounds.min[2] - 0.5
    grid.visible = !selectedFeaturesIsolationMode
    scene.add(grid)

    let disposed = false
    const selectedFeatureIdSet = new Set(selectedFeatureIds)
    const featureObjects: THREE.Object3D[] = []
    if (layers.features || selectedFeaturesIsolationMode) {
      result.features
        .filter(feature => !selectedFeaturesIsolationMode || selectedFeatureIdSet.has(feature.id))
        .forEach(feature => {
          const object = createFeatureObject(feature, selectedFeaturesIsolationMode || feature.id === selectedFeatureId)
          if (object) {
            object.userData.featureId = feature.id
            featureObjects.push(object)
            scene.add(object)
          }
      })
    }

    if ((layers.workpiece || selectedFeaturesIsolationMode) && result.modelUrl) {
      new STLLoader().load(result.modelUrl, geometry => {
        if (disposed) {
          geometry.dispose()
          return
        }
        geometry.computeVertexNormals()
        const material = new THREE.MeshStandardMaterial({
          color: 0x747d86,
          roughness: 0.78,
          metalness: 0.05,
          transparent: true,
          opacity: selectedFeaturesIsolationMode ? 0.18 : 0.72,
          depthWrite: !selectedFeaturesIsolationMode,
        })
        const mesh = new THREE.Mesh(geometry, material)
        mesh.name = 'workpiece'
        scene.add(mesh)
      })
    }

    if (layers.points && !selectedFeaturesIsolationMode) {
      const positions = result.features.flatMap(feature => feature.measurementPoints.flatMap(point => point.position))
      const geometry = new THREE.BufferGeometry()
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3))
      scene.add(new THREE.Points(geometry, new THREE.PointsMaterial({ color: colors.point, size: 2.4, sizeAttenuation: true })))

      const selected = result.features.find(feature => feature.id === selectedFeatureId)
      selected?.measurementPoints.slice(0, 40).forEach(point => {
        const label = createPointLabel(String(point.index))
        label.position.set(point.position[0], point.position[1], point.position[2] + 1.8)
        scene.add(label)
      })
    }

    if (layers.normals && !selectedFeaturesIsolationMode) {
      const vertices: number[] = []
      result.features.forEach(feature => feature.measurementPoints.forEach(point => {
        vertices.push(...point.position)
        vertices.push(
          point.position[0] + point.normal[0] * 4,
          point.position[1] + point.normal[1] * 4,
          point.position[2] + point.normal[2] * 4,
        )
      }))
      const geometry = new THREE.BufferGeometry()
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3))
      scene.add(new THREE.LineSegments(geometry, new THREE.LineBasicMaterial({ color: 0xb0bac4, transparent: true, opacity: 0.45 })))
    }

    const completed = lineSegments(0x7ad8ca, 0.95)
    const future = lineSegments(0x47525e, 0.35)
    const risk = dashedLineSegments(colors.risk, 0.9)
    const active = new THREE.Line(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color: 0xffffff }))
    scene.add(completed, future, risk, active)

    if (layers.safety && !selectedFeaturesIsolationMode) {
      const safetyZ = Math.max(
        result.bounds.max[2] + 10,
        ...plan.segments.filter(segment => segment.isAutoGoto).flatMap(segment => [segment.start[2], segment.end[2]]),
      )
      const width = Math.max(result.bounds.max[0] - result.bounds.min[0], 20) * 1.25
      const height = Math.max(result.bounds.max[1] - result.bounds.min[1], 20) * 1.25
      const geometry = new THREE.PlaneGeometry(width, height)
      const material = new THREE.MeshBasicMaterial({ color: 0x4d8dff, transparent: true, opacity: 0.055, side: THREE.DoubleSide, depthWrite: false })
      const plane = new THREE.Mesh(geometry, material)
      plane.position.set(
        (result.bounds.min[0] + result.bounds.max[0]) / 2,
        (result.bounds.min[1] + result.bounds.max[1]) / 2,
        safetyZ,
      )
      scene.add(plane)
    }

    const probe = createProbe(result.probe?.tipDiameterMm ?? 2, result.probe?.tipLengthMm ?? 20)
    probe.visible = !selectedFeaturesIsolationMode
    scene.add(probe)

    const box = new THREE.Box3(
      new THREE.Vector3(...result.bounds.min),
      new THREE.Vector3(...result.bounds.max),
    )
    const center = box.getCenter(new THREE.Vector3())
    const size = Math.max(box.getSize(new THREE.Vector3()).length(), 30)
    const cameraSnapshot = cameraSnapshotRef.current
    if (cameraSnapshot?.sessionId === result.sessionId) {
      controls.target.copy(cameraSnapshot.target)
      camera.position.copy(cameraSnapshot.position)
    } else {
      controls.target.copy(center)
      camera.position.set(center.x + size * 0.9, center.y - size * 1.1, center.z + size * 0.75)
    }
    camera.near = Math.max(0.01, size / 10000)
    camera.far = size * 100
    camera.updateProjectionMatrix()
    controls.update()

    const resize = () => {
      const width = host.clientWidth
      const height = host.clientHeight
      renderer.setSize(width, height, false)
      camera.aspect = width / Math.max(height, 1)
      camera.updateProjectionMatrix()
    }
    const observer = new ResizeObserver(resize)
    observer.observe(host)
    resize()

    const raycaster = new THREE.Raycaster()
    const pointer = new THREE.Vector2()
    const click = (event: PointerEvent) => {
      const rect = renderer.domElement.getBoundingClientRect()
      pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1
      pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1
      raycaster.setFromCamera(pointer, camera)
      const hit = raycaster.intersectObjects(featureObjects, true)[0]
      let object: THREE.Object3D | null = hit?.object ?? null
      while (object && !object.userData.featureId) object = object.parent
      onSelectFeature(object?.userData.featureId)
    }
    renderer.domElement.addEventListener('pointerdown', click)

    const animate = () => {
      controls.update()
      renderer.render(scene, camera)
      stateRef.current!.frame = requestAnimationFrame(animate)
    }
    stateRef.current = { renderer, scene, camera, controls, completed, future, risk, active, probe, featureObjects, frame: 0 }
    animate()

    return () => {
      disposed = true
      cameraSnapshotRef.current = {
        sessionId: result.sessionId,
        position: camera.position.clone(),
        target: controls.target.clone(),
      }
      observer.disconnect()
      renderer.domElement.removeEventListener('pointerdown', click)
      cancelAnimationFrame(stateRef.current?.frame ?? 0)
      scene.traverse(object => {
        if (object instanceof THREE.Mesh || object instanceof THREE.Line || object instanceof THREE.LineSegments || object instanceof THREE.Points) {
          object.geometry.dispose()
          const material = object.material
          if (Array.isArray(material)) material.forEach(value => value.dispose())
          else material.dispose()
        }
        if (object instanceof THREE.Sprite) {
          object.material.map?.dispose()
          object.material.dispose()
        }
      })
      renderer.dispose()
      stateRef.current = null
    }
  }, [result, plan, layers, selectedFeatureId, selectedFeatureIds, selectedFeaturesIsolationMode, onSelectFeature])

  useEffect(() => {
    const state = stateRef.current
    if (!state) return
    const index = Math.min(Math.max(currentStep, 0), Math.max(plan.segments.length - 1, 0))
    const visibleSegments = (segments: PathPlan['segments']) => layers.goto ? segments : segments.filter(segment => !segment.isGoto)
    const executable = (segments: PathPlan['segments']) => visibleSegments(segments).filter(segment => segment.isExecutable)
    updateLineSegments(state.completed, executable(plan.segments.slice(0, index)), true)
    updateLineSegments(state.future, executable(plan.segments.slice(index + 1)), false)
    updateRiskSegments(state.risk, visibleSegments(plan.segments.filter(segment => !segment.isExecutable)))
    const segment = plan.segments[index]
    if (segment) {
      const geometry = new THREE.BufferGeometry().setFromPoints([
        new THREE.Vector3(...segment.start),
        new THREE.Vector3(...segment.end),
      ])
      state.active.geometry.dispose()
      state.active.geometry = geometry
      ;(state.active.material as THREE.LineBasicMaterial).color.setHex(segment.hasRisk ? colors.risk : segment.isGoto ? colors.goto : segment.kind === 'Measurement' ? colors.measurement : colors.selected)
      state.probe.position.set(...(segment.isExecutable ? segment.end : segment.start))
      state.probe.quaternion.setFromUnitVectors(
        new THREE.Vector3(0, 0, 1),
        resolveSegmentProbeDirection(plan.segments, index),
      )
      state.probe.visible = true
    } else {
      state.probe.visible = false
    }
    state.completed.visible = layers.path && !selectedFeaturesIsolationMode
    state.future.visible = layers.path && !selectedFeaturesIsolationMode
    state.risk.visible = layers.path && !selectedFeaturesIsolationMode
    state.active.visible = layers.path && !selectedFeaturesIsolationMode && (layers.goto || !segment?.isGoto)
    state.probe.visible = !selectedFeaturesIsolationMode && Boolean(segment)
  }, [currentStep, plan, layers.path, layers.goto, selectedFeaturesIsolationMode])

  useEffect(() => {
    const state = stateRef.current
    if (!state) return
    const box = new THREE.Box3(new THREE.Vector3(...result.bounds.min), new THREE.Vector3(...result.bounds.max))
    const center = box.getCenter(new THREE.Vector3())
    const size = Math.max(box.getSize(new THREE.Vector3()).length(), 30)
    const views: Record<string, THREE.Vector3> = {
      iso: new THREE.Vector3(center.x + size, center.y - size, center.z + size * 0.8),
      top: new THREE.Vector3(center.x, center.y, center.z + size * 1.5),
      front: new THREE.Vector3(center.x, center.y - size * 1.5, center.z),
      side: new THREE.Vector3(center.x + size * 1.5, center.y, center.z),
    }
    state.camera.position.copy(views[cameraView] ?? views.iso)
    state.controls.target.copy(center)
    state.controls.update()
  }, [cameraView, result.bounds])

  return <div className="viewer-host" ref={hostRef} />
}

function lineSegments(color: number, opacity: number) {
  return new THREE.LineSegments(
    new THREE.BufferGeometry(),
    new THREE.LineBasicMaterial({ color, transparent: true, opacity }),
  )
}

function dashedLineSegments(color: number, opacity: number) {
  return new THREE.LineSegments(
    new THREE.BufferGeometry(),
    new THREE.LineDashedMaterial({ color, transparent: true, opacity, dashSize: 3, gapSize: 2 }),
  )
}

function updateRiskSegments(object: THREE.LineSegments, segments: PathPlan['segments']) {
  const vertices = segments.flatMap(segment => [...segment.start, ...segment.end])
  const geometry = new THREE.BufferGeometry()
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3))
  object.geometry.dispose()
  object.geometry = geometry
  object.computeLineDistances()
}

function updateLineSegments(object: THREE.LineSegments, segments: PathPlan['segments'], completed: boolean) {
  const vertices: number[] = []
  const colorsArray: number[] = []
  segments.forEach(segment => {
    vertices.push(...segment.start, ...segment.end)
    const color = new THREE.Color(segment.hasRisk ? colors.risk : segment.isGoto ? colors.goto : segment.kind === 'Measurement' ? colors.measurement : completed ? colors.movement : 0x47525e)
    colorsArray.push(color.r, color.g, color.b, color.r, color.g, color.b)
  })
  const geometry = new THREE.BufferGeometry()
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3))
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colorsArray, 3))
  object.geometry.dispose()
  object.geometry = geometry
  ;(object.material as THREE.LineBasicMaterial).vertexColors = true
}

function createProbe(diameter: number, length: number) {
  const group = new THREE.Group()
  const ball = new THREE.Mesh(
    new THREE.SphereGeometry(Math.max(diameter / 2, 0.8), 16, 12),
    new THREE.MeshStandardMaterial({ color: 0xf4f6f8, roughness: 0.3, metalness: 0.2 }),
  )
  const stem = new THREE.Mesh(
    new THREE.CylinderGeometry(0.6, 0.6, Math.max(length, 8), 10),
    new THREE.MeshStandardMaterial({ color: 0x9ca6af, roughness: 0.5 }),
  )
  stem.rotation.x = Math.PI / 2
  stem.position.z = Math.max(length, 8) / 2
  group.add(ball, stem)
  return group
}

function resolveSegmentProbeDirection(segments: PathPlan['segments'], index: number) {
  for (let current = index; current >= 0; current--) {
    const direction = segments[current]?.probeDirection
    if (!direction || !direction.every(Number.isFinite)) continue

    const vector = new THREE.Vector3(...direction)
    if (vector.lengthSq() <= 1e-12) continue

    vector.normalize()
    if (Math.abs(vector.x) <= 1e-6 && Math.abs(vector.y) <= 1e-6 && vector.z < 0) {
      return new THREE.Vector3(0, 0, 1)
    }

    return vector
  }

  return new THREE.Vector3(0, 0, 1)
}

function createPointLabel(text: string) {
  const canvas = document.createElement('canvas')
  canvas.width = 64
  canvas.height = 32
  const context = canvas.getContext('2d')!
  context.fillStyle = 'rgba(15, 19, 24, 0.88)'
  context.fillRect(0, 0, 64, 32)
  context.strokeStyle = '#ff9638'
  context.strokeRect(1, 1, 62, 30)
  context.fillStyle = '#ffffff'
  context.font = '18px Segoe UI'
  context.textAlign = 'center'
  context.textBaseline = 'middle'
  context.fillText(text, 32, 17)
  const texture = new THREE.CanvasTexture(canvas)
  texture.colorSpace = THREE.SRGBColorSpace
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, depthTest: false }))
  sprite.scale.set(3.2, 1.6, 1)
  sprite.renderOrder = 20
  return sprite
}

function createFeatureObject(feature: VisualizationFeature, selected: boolean): THREE.Object3D | null {
  const color = selected ? colors.selected : feature.isInnerSurface ? colors.innerFeature : colors.feature
  const material = new THREE.MeshBasicMaterial({ color, wireframe: true, transparent: true, opacity: selected ? 0.95 : 0.5, depthTest: false })
  let object: THREE.Object3D | null = null
  const radius = Math.max(feature.radius ?? 5, 0.5)
  const size = Math.max(Math.sqrt(feature.area ?? 100), 5)
  switch (feature.type) {
    case 'Cylinder': {
      const height = Math.max(feature.length ?? 0, 0.01)
      const thetaStart = feature.startAngleRad ?? 0
      const thetaLength = Math.min(Math.max(feature.angularSpanRad ?? Math.PI * 2, 0.001), Math.PI * 2)
      object = new THREE.Mesh(new THREE.CylinderGeometry(radius, radius, height, 48, 1, true, thetaStart, thetaLength), material)
      orientCylinder(object, feature.direction, feature.radialReference)
      break
    }
    case 'Plane':
      object = new THREE.Mesh(new THREE.PlaneGeometry(size, size), material)
      orient(object, new THREE.Vector3(0, 0, 1), feature.direction)
      break
    case 'Sphere':
      object = new THREE.Mesh(new THREE.SphereGeometry(radius, 20, 14), material)
      break
    case 'Cone': {
      const coneHeight = Math.max(feature.coneLength ?? size, 0.5)
      const rStart = Math.max(feature.coneRadiusStart ?? Math.tan(feature.angleRad ?? 0.35) * coneHeight, 0.25)
      const rEnd = Math.max(feature.coneRadiusEnd ?? rStart, 0.25)
      // Use CylinderGeometry for frustum (圆台) — different top/bottom radii
      object = new THREE.Mesh(new THREE.CylinderGeometry(rStart, rEnd, coneHeight, 24, 1, true), material)
      orient(object, new THREE.Vector3(0, 1, 0), feature.direction)
      break
    }
    case 'Circle':
    case 'Arc': {
      const points = Array.from({ length: 48 }, (_, index) => {
        const angle = index / 47 * Math.PI * 2
        return new THREE.Vector3(Math.cos(angle) * radius, Math.sin(angle) * radius, 0)
      })
      object = new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color }))
      orient(object, new THREE.Vector3(0, 0, 1), feature.direction)
      break
    }
    default:
      object = new THREE.Mesh(new THREE.SphereGeometry(1.2, 10, 8), material)
  }
  object.position.set(...feature.position)
  object.renderOrder = selected ? 10 : 5
  return object
}

function orient(object: THREE.Object3D, from: THREE.Vector3, direction: [number, number, number]) {
  const target = new THREE.Vector3(...direction).normalize()
  if (target.lengthSq() > 0) object.quaternion.setFromUnitVectors(from, target)
}

function orientCylinder(object: THREE.Object3D, direction: [number, number, number], radialReference?: [number, number, number]) {
  const axis = new THREE.Vector3(...direction).normalize()
  if (!radialReference) {
    orient(object, new THREE.Vector3(0, 1, 0), direction)
    return
  }

  const radial = new THREE.Vector3(...radialReference).normalize()
  const tangent = new THREE.Vector3().crossVectors(axis, radial).normalize()
  object.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(tangent, axis, radial))
}
