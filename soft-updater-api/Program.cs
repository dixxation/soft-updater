using Scalar.AspNetCore;
using soft_updater_api;
using soft_updater_api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ── Settings ───────────────────────────────────────────────────────────────
builder.Configuration.AddEnvironmentVariables();

var gitLabSettings = new GitLabSettings
{
    ApiUrl          = builder.Configuration["GitLab:ApiUrl"]  ?? throw new InvalidOperationException("GitLab:ApiUrl is required"),
    Token           = builder.Configuration["GitLab:Token"]   ?? throw new InvalidOperationException("GitLab:Token is required"),
    IgnoreSslErrors = builder.Configuration.GetValue<bool>("GitLab:IgnoreSslErrors"),
};

var apiKeySettings = new ApiKeySettings
{
    MasterKey = builder.Configuration["Auth:MasterKey"] ?? throw new InvalidOperationException("Auth:MasterKey is required"),
    Keys      = builder.Configuration.GetSection("Auth:Keys").Get<Dictionary<string, int>>()
                ?? throw new InvalidOperationException("Auth:Keys is required"),
};

builder.Services.AddSingleton(gitLabSettings);
builder.Services.AddSingleton(apiKeySettings);
builder.Services.AddSingleton<ApiKeyService>();

// ── HTTP client → GitLab ───────────────────────────────────────────────────
builder.Services.AddHttpClient<GitLabService>(client =>
{
    client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", gitLabSettings.Token);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    if (gitLabSettings.IgnoreSslErrors)
    {
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }
    return handler;
});

// ── Infrastructure ─────────────────────────────────────────────────────────
builder.Services.AddOutputCache(o =>
{
    o.AddPolicy("versions", p => p.Expire(TimeSpan.FromMinutes(5)));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name        = "X-Api-Key",
        Type        = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In          = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "API ключ приложения. *master key для доступа ко всем эндпоинтам.",
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id   = "ApiKey",
                }
            },
            []
        }
    });
});

var app = builder.Build();

app.UseSwagger(c =>
{
    c.RouteTemplate = "api/swagger/{documentName}/swagger.json";
});
app.MapGroup("/api").MapScalarApiReference(options =>
{
    options
        .WithTitle("Software Updater API")
        .WithTheme(ScalarTheme.DeepSpace)
        .WithOpenApiRoutePattern("/api/swagger/v1/swagger.json");
});

// Редирект / → /api/scalar
app.MapGet("/", () => Results.Redirect("/api/scalar/v1"))
    .ExcludeFromDescription();

app.UseOutputCache();

app.MapUpdates();
app.MapApps();
app.MapHealth();

app.Run();