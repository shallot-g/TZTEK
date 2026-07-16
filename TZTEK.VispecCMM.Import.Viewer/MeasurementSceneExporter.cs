using System.Text.Json;
using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Viewer;

/// <summary>
/// 将测量场景导出为 JSON 或内嵌 Three.js 的 HTML。
/// </summary>
public static class MeasurementSceneExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static string ToJson(MeasurementScene scene)
    {
        return JsonSerializer.Serialize(scene, SerializerOptions);
    }

    public static async Task SaveJsonAsync(MeasurementScene scene, string outputPath, CancellationToken ct = default)
    {
        var json = ToJson(scene);
        await File.WriteAllTextAsync(outputPath, json, ct).ConfigureAwait(false);
    }

    public static async Task SaveHtmlAsync(MeasurementScene scene, string outputPath, CancellationToken ct = default)
    {
        var json = ToJson(scene);
        var html = BuildHtml(json, scene.Title);
        await File.WriteAllTextAsync(outputPath, html, ct).ConfigureAwait(false);
    }

    private static string BuildHtml(string sceneJson, string title)
    {
        var escapedTitle = System.Net.WebUtility.HtmlEncode(title);
        return $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>{{escapedTitle}} - Vispec Viewer</title>
  <style>
    html, body { margin: 0; width: 100%; height: 100%; overflow: hidden; background: #11151d; color: #d8e1f0; font-family: Segoe UI, sans-serif; }
    #info { position: absolute; top: 12px; left: 12px; background: rgba(10, 14, 22, 0.82); padding: 10px 14px; border-radius: 8px; font-size: 13px; line-height: 1.5; z-index: 2; }
    #legend span { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 6px; }
  </style>
</head>
<body>
  <div id="info">
    <div><strong>{{escapedTitle}}</strong></div>
    <div id="legend">
      <div><span style="background:#b4bec8"></span>工件网格</div>
      <div><span style="background:#ffc850"></span>基元轮廓</div>
      <div><span style="background:#50dc78"></span>测点</div>
      <div><span style="background:#468cff"></span>移动路径</div>
      <div><span style="background:#ffaa3c"></span>安全 GOTO</div>
      <div><span style="background:#ff5a5a"></span>人工 GOTO</div>
    </div>
  </div>
  <script type="module">
    import * as THREE from 'https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.module.js';
    import { OrbitControls } from 'https://cdn.jsdelivr.net/npm/three@0.160.0/examples/jsm/controls/OrbitControls.js';

    const sceneData = {{sceneJson}};
    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x11151d);

    const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 0.1, 100000);
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(window.innerWidth, window.innerHeight);
    document.body.appendChild(renderer.domElement);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;

    const ambient = new THREE.AmbientLight(0xffffff, 0.65);
    const directional = new THREE.DirectionalLight(0xffffff, 0.85);
    directional.position.set(120, 180, 90);
    scene.add(ambient, directional);

    const bounds = sceneData.bounds || { min: { x: -50, y: -50, z: -50 }, max: { x: 50, y: 50, z: 50 } };
    const center = new THREE.Vector3(
      (bounds.min.x + bounds.max.x) / 2,
      (bounds.min.y + bounds.max.y) / 2,
      (bounds.min.z + bounds.max.z) / 2
    );
    const span = Math.max(
      bounds.max.x - bounds.min.x,
      bounds.max.y - bounds.min.y,
      bounds.max.z - bounds.min.z,
      20
    );

    camera.position.copy(center).add(new THREE.Vector3(span * 1.4, span * 1.1, span * 1.3));
    controls.target.copy(center);

    const colorFrom = (c) => new THREE.Color(c.r / 255, c.g / 255, c.b / 255);

    for (const obj of sceneData.objects || []) {
      const color = colorFrom(obj.color || { r: 200, g: 200, b: 200 });
      const opacity = obj.opacity ?? 1.0;

      if (obj.kind === 'WorkpieceMesh' && obj.vertices?.length && obj.triangles?.length) {
        const geometry = new THREE.BufferGeometry();
        const positions = new Float32Array(obj.vertices);
        const indices = new Uint32Array(obj.triangles);
        geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
        geometry.setIndex(new THREE.BufferAttribute(indices, 1));
        geometry.computeVertexNormals();
        const material = new THREE.MeshStandardMaterial({ color, transparent: true, opacity, side: THREE.DoubleSide });
        scene.add(new THREE.Mesh(geometry, material));
        continue;
      }

      if (obj.points?.length >= 2) {
        const points = obj.points.map(p => new THREE.Vector3(p.x, p.y, p.z));
        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        if (obj.kind === 'MeasurementPoint') {
          const line = new THREE.Line(geometry, new THREE.LineBasicMaterial({ color }));
          scene.add(line);
          const marker = new THREE.Mesh(
            new THREE.SphereGeometry(Math.max(span * 0.006, 0.4), 12, 12),
            new THREE.MeshStandardMaterial({ color })
          );
          marker.position.copy(points[0]);
          scene.add(marker);
        } else if (obj.kind === 'GotoPoint') {
          const marker = new THREE.Mesh(
            new THREE.SphereGeometry(Math.max(span * 0.008, 0.6), 14, 14),
            new THREE.MeshStandardMaterial({ color })
          );
          marker.position.copy(points[0]);
          scene.add(marker);
        } else {
          const line = new THREE.Line(geometry, new THREE.LineBasicMaterial({ color, transparent: opacity < 1, opacity }));
          scene.add(line);
        }
      }
    }

    const grid = new THREE.GridHelper(span * 2, 20, 0x334155, 0x1f2937);
    grid.position.copy(center);
    scene.add(grid);

    const axes = new THREE.AxesHelper(span * 0.35);
    axes.position.copy(center);
    scene.add(axes);

    function animate() {
      requestAnimationFrame(animate);
      controls.update();
      renderer.render(scene, camera);
    }
    animate();

    window.addEventListener('resize', () => {
      camera.aspect = window.innerWidth / window.innerHeight;
      camera.updateProjectionMatrix();
      renderer.setSize(window.innerWidth, window.innerHeight);
    });
  </script>
</body>
</html>
""";
    }
}
