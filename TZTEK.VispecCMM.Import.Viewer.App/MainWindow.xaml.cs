using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Microsoft.Extensions.DependencyInjection;
using TZTEK.VispecCMM.Import.Core.DependencyInjection;
using TZTEK.VispecCMM.Import.Interfaces.Interfaces;
using TZTEK.VispecCMM.Import.Interfaces.Models;
using TZTEK.VispecCMM.Import.Viewer;

namespace TZTEK.VispecCMM.Import.Viewer.App;

public partial class MainWindow : Window
{
    private readonly HelixViewport3D _viewport;

    public MainWindow()
    {
        Title = "Vispec CMM Viewer";
        Width = 1280;
        Height = 800;

        _viewport = new HelixViewport3D
        {
            ZoomExtentsWhenLoaded = true,
            ShowCoordinateSystem = true,
            Background = new SolidColorBrush(Color.FromRgb(17, 21, 29))
        };

        Content = _viewport;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var inputPath = GetArgumentValue(args, "--input");
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                MessageBox.Show(
                    "请通过命令行传入 STEP/DXF 文件：\nTZTEK.VispecCMM.Import.Viewer.App.exe --input sample.stp",
                    "Vispec Viewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var htmlPath = GetArgumentValue(args, "--html");
            var scene = await BuildSceneAsync(inputPath).ConfigureAwait(true);
            RenderScene(scene);
            Title = $"Vispec CMM Viewer - {scene.Title}";

            if (!string.IsNullOrWhiteSpace(htmlPath))
            {
                await MeasurementSceneExporter.SaveHtmlAsync(scene, htmlPath).ConfigureAwait(true);
                TryOpenFile(htmlPath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vispec Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<MeasurementScene> BuildSceneAsync(string inputPath)
    {
        var services = new ServiceCollection();
        services.AddVispecCmmImportCore();
        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IPrimitiveToleranceService>();

        await service.ImportAsync(inputPath, new ImportOptions()).ConfigureAwait(false);
        service.AssignProbes(
        [
            new Probe
            {
                Id = "probe_default",
                Name = "Default Probe",
                ProbeType = ProbeType.TouchTrigger,
                TipDiameter = 2.0,
                TipLength = 50.0
            }
        ]);

        return service.BuildMeasurementScene();
    }

    private void RenderScene(MeasurementScene scene)
    {
        var visuals = new ModelVisual3D { Content = new Model3DGroup() };
        _viewport.Children.Add(visuals);
        var group = (Model3DGroup)visuals.Content!;

        foreach (var obj in scene.Objects)
        {
            var color = Color.FromArgb(obj.Color.A, obj.Color.R, obj.Color.G, obj.Color.B);
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            if (obj.Kind == SceneObjectKind.WorkpieceMesh && obj.Vertices.Count > 0 && obj.Triangles.Count > 0)
            {
                var mesh = BuildTriangleMesh(obj);
                var material = new DiffuseMaterial(brush)
                {
                    Brush = new SolidColorBrush(Color.FromArgb(
                        (byte)(obj.Opacity * 255),
                        obj.Color.R,
                        obj.Color.G,
                        obj.Color.B))
                };
                material.Brush.Freeze();
                group.Children.Add(new GeometryModel3D(mesh, material));
                continue;
            }

            if (obj.Points.Count < 2)
            {
                if (obj.Points.Count == 1)
                {
                    var point = obj.Points[0];
                    var sphere = new SphereVisual3D
                    {
                        Center = new Point3D(point.X, point.Y, point.Z),
                        Radius = 0.8,
                        Fill = brush
                    };
                    _viewport.Children.Add(sphere);
                }

                continue;
            }

            var polyline = new LinesVisual3D
            {
                Color = color,
                Thickness = obj.Kind == SceneObjectKind.PathSegment ? 2.0 : 1.5
            };

            foreach (var point in obj.Points)
                polyline.Points.Add(new Point3D(point.X, point.Y, point.Z));

            _viewport.Children.Add(polyline);
        }
    }

    private static MeshGeometry3D BuildTriangleMesh(MeasurementSceneObject obj)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(),
            TriangleIndices = new Int32Collection()
        };

        for (var i = 0; i + 2 < obj.Vertices.Count; i += 3)
            mesh.Positions.Add(new Point3D(obj.Vertices[i], obj.Vertices[i + 1], obj.Vertices[i + 2]));

        foreach (var index in obj.Triangles)
            mesh.TriangleIndices.Add(index);

        return mesh;
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void TryOpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Ignore browser launch failures.
        }
    }
}
