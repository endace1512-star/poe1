using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Poe1.Api;
using Poe1.Infrastructure;
using Poe1.Leveling;
using Poe1.Poe;
using Poe1.Pricing;
using Poe1.Wealth;

var demo = args.Contains("--demo");
var openBrowser = !args.Contains("--no-browser");

var paths = new AppPaths(demo ? Path.Combine(Path.GetTempPath(), "poe1-demo") : null);
var settingsStore = new SettingsStore(paths);
var port = settingsStore.Current.Port;
var portArg = Array.IndexOf(args, "--port");
if (portArg >= 0 && portArg + 1 < args.Length && int.TryParse(args[portArg + 1], out var p)) port = p;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
// Only reachable from this computer.
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(settingsStore);
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<EventHub>();
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddSingleton<SnapshotRepository>();
builder.Services.AddSingleton<PriceService>();
builder.Services.AddSingleton<SnapshotService>();
builder.Services.AddSingleton<PriceAlerts>();
builder.Services.AddSingleton<FarmService>();
builder.Services.AddSingleton<ClientLogWatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClientLogWatcher>());
if (!demo) builder.Services.AddHostedService<SnapshotScheduler>();

const string UserAgent = "poe1-aio/0.1 (local tool)";
builder.Services.AddHttpClient<PoeApiClient>(c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    c.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All });
builder.Services.AddHttpClient<NinjaClient>(c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    c.Timeout = TimeSpan.FromSeconds(60);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

if (demo) DemoSeeder.Seed(app.Services.GetRequiredService<SnapshotRepository>(), settingsStore.Current.League);

app.UseMiddleware<LocalOnlyMiddleware>();

// Web UI: embedded into the exe at build time. POE1_WEB_DIR points at a folder instead (for development).
IFileProvider webFiles;
if (Environment.GetEnvironmentVariable("POE1_WEB_DIR") is { Length: > 0 } webDir)
    webFiles = new PhysicalFileProvider(Path.GetFullPath(webDir));
else
{
    // No manifest when the exe was built before `npm run build`: serve the "not built" message instead of crashing.
    try { webFiles = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot"); }
    catch (InvalidOperationException) { webFiles = new NullFileProvider(); }
}
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFiles });
app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles });

app.MapApi();
app.MapFallback(async ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 404; return; }
    var index = webFiles.GetFileInfo("index.html");
    if (!index.Exists)
    {
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync("Web UI chưa được build. Chạy: cd web && npm install && npm run build");
        return;
    }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await using var s = index.CreateReadStream();
    await s.CopyToAsync(ctx.Response.Body);
});

var url = $"http://127.0.0.1:{port}";
app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("poe1 is running at {Url} (data: {Dir})", url, paths.DataDir);
    if (openBrowser && OperatingSystem.IsWindows())
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* no browser */ }
    }
});

app.Run();

public partial class Program;
