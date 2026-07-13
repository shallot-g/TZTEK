using Microsoft.AspNetCore.Http.Features;
using TZTEK.VispecCMM.Demo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 512 * 1024 * 1024;
});
builder.Services.AddSingleton<DemoSessionService>();
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
