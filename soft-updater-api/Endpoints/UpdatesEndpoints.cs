using soft_updater_model;

namespace soft_updater_api.Endpoints;

public static class UpdatesEndpoints
{
    public static IEndpointRouteBuilder MapUpdates(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/updates").WithTags("Updates");
 
        // GET /api/updates/latest?currentVersion=1.2.3
        group.MapGet("/latest", async (
                string? currentVersion,
                HttpContext ctx,
                GitLabService svc,
                ApiKeyService keys) =>
            {
                var projectId = keys.Resolve(ctx.Request.Headers[ApiKeyService.Header]);
                if (projectId is null)
                    return Results.Unauthorized();
 
                var update = await svc.GetLatestAsync(projectId.Value, currentVersion);
                return update is null ? Results.NoContent() : Results.Ok(update);
            })
            .WithName("GetLatest")
            .WithSummary("Последний релиз. 204 — версия актуальна.")
            .Produces<UpdateInfo>()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .CacheOutput("versions");
 
        // GET /api/updates/changelog?from=1.0.0
        group.MapGet("/changelog", async (
                string from,
                HttpContext ctx,
                GitLabService svc,
                ApiKeyService keys) =>
            {
                var projectId = keys.Resolve(ctx.Request.Headers[ApiKeyService.Header]);
                if (projectId is null)
                    return Results.Unauthorized();
 
                var changelog = await svc.GetChangelogAsync(projectId.Value, from);
                return Results.Ok(changelog);
            })
            .WithName("GetChangelog")
            .WithSummary("Все релизы новее указанной версии")
            .Produces<List<UpdateInfo>>()
            .Produces(StatusCodes.Status401Unauthorized);
 
        // GET /api/updates/download/{version}
        group.MapGet("/download/{version}", async (
                string version,
                HttpContext ctx,
                GitLabService svc,
                ApiKeyService keys) =>
            {
                var projectId = keys.Resolve(ctx.Request.Headers[ApiKeyService.Header]);
                if (projectId is null)
                    return Results.Unauthorized();
 
                var stream = await svc.GetArchiveStreamAsync(projectId.Value, version);
                if (stream is null)
                    return Results.NotFound(new { error = $"Release {version} not found" });
 
                return Results.Stream(stream, "application/zip", $"release-{version}.zip");
            })
            .WithName("DownloadRelease")
            .WithSummary("Скачать архив релиза")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
 
        // GET /api/updates/versions?page=1&pageSize=10
        group.MapGet("/versions", async (
                HttpContext ctx,
                GitLabService svc,
                ApiKeyService keys,
                int page     = 1,
                int pageSize = 10) =>
            {
                var projectId = keys.Resolve(ctx.Request.Headers[ApiKeyService.Header]);
                if (projectId is null)
                    return Results.Unauthorized();
 
                page     = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 50);
 
                var result = await svc.GetReleasesPagedAsync(projectId.Value, page, pageSize);
                return Results.Ok(result);
            })
            .WithName("GetVersions")
            .WithSummary("Список всех версий с пагинацией")
            .Produces<PagedResult<UpdateInfo>>()
            .Produces(StatusCodes.Status401Unauthorized);
 
        return routes;
    }
}
