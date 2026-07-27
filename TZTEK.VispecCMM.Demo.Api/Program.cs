using Microsoft.AspNetCore.Http.Features;
using TZTEK.VispecCMM.Demo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 512 * 1024 * 1024;
});
builder.Services.AddSingleton<DemoSessionService>();
builder.Services.AddHttpClient<IDrawingAssistClient, VolcengineDrawingAssistClient>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/demo/examples", (DemoSessionService service) => Results.Ok(service.GetExamples()));

app.MapPost("/api/demo/sessions", async (HttpRequest request, DemoSessionService service, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "请使用 multipart/form-data 上传文件。" });

    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "请选择 STP 或 STEP 文件。" });

    try
    {
        var session = await service.CreateFromUploadAsync(file, ct);
        return Results.Accepted($"/api/demo/sessions/{session.Id}", session);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/demo/sessions/examples/{name}", async (
    string name,
    DemoSessionService service,
    CancellationToken ct) =>
{
    try
    {
        var session = await service.CreateFromExampleAsync(name, ct);
        return Results.Accepted($"/api/demo/sessions/{session.Id}", session);
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapGet("/api/demo/sessions/{id}", (string id, DemoSessionService service) =>
{
    return service.TryGet(id, out var session)
        ? Results.Ok(session)
        : Results.NotFound(new { error = "演示会话不存在或已过期。" });
});

app.MapPost("/api/demo/sessions/{id}/features/selection", (string id, FeatureSelectionRequest request, DemoSessionService service) =>
{
    try { return Results.Ok(service.SaveSelection(id, request)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/demo/sessions/{id}/ai-assist", (string id, AiAssistSettingsRequest request, DemoSessionService service) =>
{
    try { service.SetAiAssist(id, request.Enabled); return Results.NoContent(); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
});

app.MapPost("/api/demo/sessions/{id}/drawing", async (string id, HttpRequest request, DemoSessionService service, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "请使用 multipart/form-data 上传 PDF 图纸。" });
    try
    {
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("drawingFile");
        if (file is null)
            return Results.BadRequest(new { error = "请选择 PDF 图纸。" });
        return Results.Ok(await service.UploadDrawingAsync(id, file, ct));
    }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/demo/sessions/{id}/drawing", (string id, DemoSessionService service) =>
{
    try { service.DeleteDrawing(id); return Results.NoContent(); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
});

app.MapPost("/api/demo/sessions/{id}/drawing-assist", async (string id, DemoSessionService service, CancellationToken ct) =>
{
    try { return Results.Ok(await service.RunDrawingAssistAsync(id, ct)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/demo/sessions/{id}/measurement-plan", async (string id, MeasurementPlanRequest request, DemoSessionService service, CancellationToken ct) =>
{
    try { return Results.Ok(await service.GenerateMeasurementPlanAsync(id, request, ct)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/demo/sessions/{id}/measurement-plan", (string id, DemoSessionService service) =>
{
    try { service.ClearMeasurementPlan(id); return Results.NoContent(); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
});

app.MapGet("/api/demo/sessions/{id}/model", (string id, DemoSessionService service) =>
{
    if (!service.TryGetModel(id, out var path) || !File.Exists(path))
        return Results.NotFound();

    return Results.File(path, "model/stl", enableRangeProcessing: true);
});

app.MapDelete("/api/demo/sessions/{id}", (string id, DemoSessionService service) =>
{
    return service.Delete(id) ? Results.NoContent() : Results.NotFound();
});

app.MapFallbackToFile("index.html");
app.Run();
