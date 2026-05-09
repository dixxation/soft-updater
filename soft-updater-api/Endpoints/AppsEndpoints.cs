using soft_updater_model;

namespace soft_updater_api.Endpoints;

public static class AppsEndpoints
{
    public static IEndpointRouteBuilder MapApps(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/apps").WithTags("Apps");
 
        // GET /api/apps — только для master key: статус всех проектов
        group.MapGet("/", async (
                HttpContext ctx,
                GitLabService svc,
                ApiKeyService keys) =>
            {
                if (!keys.IsMaster(ctx.Request.Headers[ApiKeyService.Header]))
                    return Results.Unauthorized();
 
                var tasks = keys.AllProjectIds().Select(id => svc.GetAppStatusAsync(id));
                var results = await Task.WhenAll(tasks);
                return Results.Ok(results);
            })
            .WithName("ListApps")
            .WithSummary("Статус всех приложений (только master key)")
            .Produces<AppStatus[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .CacheOutput("versions");
 
        return routes;
    }
}

