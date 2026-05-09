using soft_updater_model;

namespace soft_updater_api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health", async (GitLabService svc) =>
            {
                var gitLab = await svc.CheckHealthAsync();

                var status = new HealthStatus(
                    Status: gitLab.Reachable ? "healthy" : "degraded",
                    Timestamp: DateTime.UtcNow,
                    GitLab: gitLab
                );

                return gitLab.Reachable
                    ? Results.Ok(status)
                    : Results.Json(status, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithTags("System")
            .WithName("Health")
            .WithSummary("Проверка доступности сервиса и GitLab")
            .Produces<HealthStatus>()
            .Produces<HealthStatus>(StatusCodes.Status503ServiceUnavailable);
 
        return routes;
    }
}