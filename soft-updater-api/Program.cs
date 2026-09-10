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
    Keys      = ReadKeys(builder.Configuration.GetSection("Auth:Keys")),
};

// Значение ключа — либо число (id проекта), либо объект { projectId, asset }.
// Второй формат нужен, когда из одного проекта выходит несколько сборок одной версии:
// маска указывает, какой файл релиза принадлежит этому приложению.
static Dictionary<string, AppTarget> ReadKeys(IConfigurationSection section)
{
    if (!section.Exists())
        throw new InvalidOperationException("Auth:Keys is required");

    var result = new Dictionary<string, AppTarget>();

    foreach (var child in section.GetChildren())
    {
        // Скалярная запись: "ключ": 123
        if (child.Value is not null)
        {
            if (!int.TryParse(child.Value, out var id))
                throw new InvalidOperationException(
                    $"Auth:Keys:{child.Key} — ожидалось число или объект {{ projectId, asset }}");

            result[child.Key] = new AppTarget(id, null);
            continue;
        }

        // Объектная запись: "ключ": { "projectId": 123, "asset": "*-main.zip" }
        var projectId = child["projectId"]
                        ?? throw new InvalidOperationException($"Auth:Keys:{child.Key}:projectId is required");

        if (!int.TryParse(projectId, out var parsed))
            throw new InvalidOperationException($"Auth:Keys:{child.Key}:projectId — ожидалось число");

        result[child.Key] = new AppTarget(parsed, child["asset"]);
    }

    if (result.Count == 0)
        throw new InvalidOperationException("Auth:Keys is required");

    return result;
}

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
    // Ответ зависит от ключа: он определяет и проект, и вариант сборки. Без VaryByHeader
    // закэшированный ответ одного приложения уходил всем остальным — включая тех, кто
    // вообще не предъявил ключ (кэш срабатывает до эндпоинта, то есть до проверки доступа).
    o.AddPolicy("versions", p => p
        .Expire(TimeSpan.FromMinutes(5))
        .SetVaryByHeader(ApiKeyService.Header));
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