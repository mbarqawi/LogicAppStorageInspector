using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LogicAppStorageInspector;
using LogicAppStorageInspector.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<StorageContext>();
builder.Services.AddSingleton<SiteScope>();
builder.Services.AddSingleton<HistorySearchService>();
builder.Services.AddSingleton<VersionService>();
builder.Services.AddSingleton<DashboardService>();

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Audit");

app.UseDefaultFiles();
app.UseStaticFiles();

// Audit log for every user-initiated action (spec 7.5).
void Audit(HttpContext ctx, string action, string detail)
{
    var site = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "";
    log.LogInformation("AUDIT site={Site} user={User} action={Action} detail={Detail}",
        site, ctx.User?.Identity?.Name ?? "anonymous", action, detail);
}

IResult Problem(Exception ex) => Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);

app.MapGet("/api/site", async (SiteScope scope, CancellationToken ct) =>
{
    try { await scope.EnsureAsync(ct); }
    catch (Exception ex) { return Results.Ok(new SiteInfo(scope.SiteName, scope.Prefix ?? "", false, ex.Message, scope.SiteCount)); }
    return Results.Ok(new SiteInfo(scope.SiteName, scope.Prefix, scope.Resolved, scope.Message, scope.SiteCount));
});

app.MapGet("/api/flows", async (HttpContext ctx, HistorySearchService svc, CancellationToken ct) =>
{
    try { Audit(ctx, "ListFlows", ""); return Results.Ok(await svc.ListFlowsAsync(ct)); }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch (Exception ex) { return Problem(ex); }
});

app.MapPost("/api/history/search", async (HttpContext ctx, HistorySearchRequest req, HistorySearchService svc, CancellationToken ct) =>
{
    try
    {
        Audit(ctx, "HistorySearch", $"q='{req?.Query}' flows={(req != null && req.AllFlows ? "ALL" : (req?.Flows?.Length ?? 0).ToString())}");
        return Results.Ok(await svc.SearchAsync(req, ct));
    }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch (Exception ex) { return Problem(ex); }
});

app.MapGet("/api/versions", async (HttpContext ctx, VersionService svc, CancellationToken ct) =>
{
    try { Audit(ctx, "VersionTree", ""); return Results.Ok(await svc.GetTreeAsync(ct)); }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch (Exception ex) { return Problem(ex); }
});

app.MapGet("/api/versions/content", async (HttpContext ctx, string flow, string version, VersionService svc, CancellationToken ct, string flowId = "") =>
{
    try { Audit(ctx, "VersionContent", $"{flow}/{version}"); return Results.Ok(await svc.GetContentAsync(flow, version, flowId, ct)); }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch (Exception ex) { return Problem(ex); }
});

app.MapGet("/api/versions/diff", async (HttpContext ctx, string flow, string left, string right, VersionService svc, CancellationToken ct, string flowId = "") =>
{
    try { Audit(ctx, "VersionDiff", $"{flow} {left} vs {right}"); return Results.Ok(await svc.DiffAsync(flow, left, right, flowId, ct)); }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch (Exception ex) { return Problem(ex); }
});

app.MapGet("/api/dashboard/tables", async (HttpContext ctx, DashboardService svc, CancellationToken ct) =>
{
    try { Audit(ctx, "DashboardTables", ""); return Results.Ok(await svc.GetTablesAsync(ct)); }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch (Exception ex) { return Problem(ex); }
});

app.MapGet("/api/dashboard/queues", async (HttpContext ctx, DashboardService svc, CancellationToken ct) =>
{
    try { Audit(ctx, "DashboardQueues", ""); return Results.Ok(await svc.GetQueuesAsync(ct)); }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch (Exception ex) { return Problem(ex); }
});

app.MapFallbackToFile("index.html");

app.Run();