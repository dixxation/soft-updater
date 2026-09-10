using System.Text.Json;
using System.Text.Json.Serialization;

namespace soft_updater_api;

public class GitLabSettings
{
    public required string ApiUrl          { get; init; }
    public required string Token           { get; init; }
    public bool            IgnoreSslErrors { get; init; }
}
 
public record GitLabUser(string Username);
 
public class GitLabService(HttpClient http, GitLabSettings settings, ILogger<GitLabService> logger)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy         = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive  = true,
        DefaultIgnoreCondition       = JsonIgnoreCondition.WhenWritingNull,
    };
 
    public async Task<UpdateInfo?> GetLatestAsync(AppTarget target, string? currentVersion = null)
    {
        var latest = await FetchLatestForAppAsync(target);
        if (latest is null) return null;

        if (currentVersion is not null && !IsNewer(latest.TagName, currentVersion))
            return null;

        return MapToUpdateInfo(target.ProjectId, latest);
    }

    public async Task<AppStatus> GetAppStatusAsync(AppTarget target)
    {
        var latest = await FetchLatestForAppAsync(target);
        return new AppStatus(
            ProjectId:     target.ProjectId,
            Asset:         target.Asset,
            LatestVersion: latest?.TagName,
            PublishedAt:   latest?.CreatedAt,
            Available:     latest is not null
        );
    }

    public async Task<List<UpdateInfo>> GetChangelogAsync(AppTarget target, string fromVersion, int maxReleases = 20)
    {
        var releases = await FetchReleasesAsync(target.ProjectId, maxReleases);

        return releases
            .TakeWhile(r => IsNewer(r.TagName, fromVersion))
            // Релиз без файла этого приложения показывать незачем: поставить его всё равно нельзя.
            .Where(r => HasAsset(target, r))
            .Select(r => MapToUpdateInfo(target.ProjectId, r))
            .ToList();
    }
 
    public async Task<GitLabHealth> CheckHealthAsync()
    {
        var url = $"{settings.ApiUrl}/user";
        try
        {
            var response = await http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
 
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GitLab health: HTTP {Status}, Body: {Body}", (int)response.StatusCode, body);
                return new GitLabHealth(false, null, $"HTTP {(int)response.StatusCode}: {body.Truncate(200)}");
            }
 
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("application/json"))
            {
                logger.LogWarning("GitLab health: unexpected Content-Type '{CT}', Body: {Body}", contentType, body.Truncate(300));
                return new GitLabHealth(false, null, $"Expected JSON, got '{contentType}'. Body: {body.Truncate(200)}");
            }
 
            var user = JsonSerializer.Deserialize<GitLabUser>(body, Json);
            return new GitLabHealth(true, user?.Username, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GitLab health check failed");
            return new GitLabHealth(false, null, ex.Message);
        }
    }
 
    public async Task<string?> GetDownloadUrlAsync(AppTarget target, string version)
    {
        var release = await FetchReleaseByTagAsync(target.ProjectId, version);
        if (release is null) return null;

        var link = SelectLink(target, release);

        // Приоритет: внешняя ссылка на прикреплённый build → исходники GitLab.
        // Берём сырой Url (прямой адрес MinIO), а НЕ DirectAssetUrl: тот — редирект-пермалинк
        // GitLab, на который клиент тоже должен был бы иметь доступ. Клиент качает архив сам
        // (эндпоинт отдаёт 302 на эту ссылку), поэтому поток через апи больше не гоним.
        if (link is not null)
            return link.Url;

        // Маска задана, но подходящего файла в релизе нет — отдать «что-нибудь» нельзя:
        // на машину приедет чужая сборка. Пусть лучше клиент получит 404.
        if (target.HasAssetFilter)
        {
            logger.LogWarning(
                "Release {Tag} in project {ProjectId} has no asset matching '{Pattern}'",
                version, target.ProjectId, target.Asset);
            return null;
        }

        return release.Assets?.Sources?.FirstOrDefault(s => s.Format.Equals("zip", StringComparison.OrdinalIgnoreCase))?.Url;
    }

    /// <summary>
    /// Файл релиза для этого приложения. Без маски — прежнее поведение: первая ссылка.
    /// </summary>
    private static GitLabLink? SelectLink(AppTarget target, GitLabRelease release)
    {
        var links = release.Assets?.Links;
        if (links is null || links.Count == 0) return null;

        if (!target.HasAssetFilter)
            return links[0];

        // Сначала по имени ссылки, потом по имени файла в URL: в релиз их кладут
        // и так и так, а CI обычно называет ссылку именем архива.
        return links.FirstOrDefault(l => AssetMatcher.Matches(target.Asset, l.Name))
               ?? links.FirstOrDefault(l => AssetMatcher.Matches(target.Asset, FileNameOf(l.Url)));
    }

    private static bool HasAsset(AppTarget target, GitLabRelease release) =>
        !target.HasAssetFilter || SelectLink(target, release) is not null;

    private static string FileNameOf(string url)
    {
        var path = url.Split('?')[0].TrimEnd('/');
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    /// <summary>
    /// Последний релиз, в котором есть файл этого приложения. Без маски — просто последний:
    /// это один запрос вместо выборки по списку.
    /// </summary>
    private async Task<GitLabRelease?> FetchLatestForAppAsync(AppTarget target)
    {
        if (!target.HasAssetFilter)
            return await FetchLatestReleaseAsync(target.ProjectId);

        var releases = await FetchReleasesAsync(target.ProjectId, 20);
        return releases.FirstOrDefault(r => HasAsset(target, r));
    }

    // GET /projects/:id/releases/permalink/latest
    private async Task<GitLabRelease?> FetchLatestReleaseAsync(int projectId)
    {
        var url = $"{settings.ApiUrl}/projects/{projectId}/releases/permalink/latest";
        try
        {
            var response = await http.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GitLabRelease>(json, Json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch latest release for project {ProjectId}", projectId);
            return null;
        }
    }
 
    // GET /projects/:id/releases?per_page=N
    private async Task<List<GitLabRelease>> FetchReleasesAsync(int projectId, int perPage)
    {
        var url = $"{settings.ApiUrl}/projects/{projectId}/releases?per_page={perPage}";
        try
        {
            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<GitLabRelease>>(json, Json) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch releases for project {ProjectId}", projectId);
            return [];
        }
    }
 
    // GET /projects/:id/releases/:tag_name
    private async Task<GitLabRelease?> FetchReleaseByTagAsync(int projectId, string tag)
    {
        var url = $"{settings.ApiUrl}/projects/{projectId}/releases/{Uri.EscapeDataString(tag)}";
        try
        {
            var response = await http.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GitLabRelease>(json, Json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch release {Tag} for project {ProjectId}", tag, projectId);
            return null;
        }
    }
 
    private UpdateInfo MapToUpdateInfo(int projectId, GitLabRelease r) => new(
        Version:           r.TagName,
        ChangelogMarkdown: r.Description ?? string.Empty,
        PublishedAt:       r.CreatedAt,
        DownloadUrl:       $"/api/updates/{projectId}/download/{r.TagName}"
    );
 
    private static bool IsNewer(string incoming, string current)
    {
        if (!Version.TryParse(incoming.TrimStart('v'), out var a)) return false;
        if (!Version.TryParse(current.TrimStart('v'),  out var b)) return false;
        return a > b;
    }

    public async Task<PagedResult<UpdateInfo>> GetReleasesPagedAsync(AppTarget target, int page, int pageSize)
    {
        // Запрашиваем на 1 больше, чтобы определить hasMore без лишнего запроса
        var url = $"{settings.ApiUrl}/projects/{target.ProjectId}/releases?page={page}&per_page={pageSize + 1}";
        try
        {
            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize<List<GitLabRelease>>(json, Json) ?? [];

            var hasMore = releases.Count > pageSize;
            // Релизы без файла этого приложения отсеиваем уже после нарезки страницы:
            // hasMore остаётся оценкой по исходной выдаче GitLab, страница может выйти короче.
            var items   = releases.Take(pageSize)
                                  .Where(r => HasAsset(target, r))
                                  .Select(r => MapToUpdateInfo(target.ProjectId, r))
                                  .ToList();

            return new PagedResult<UpdateInfo>(items, page, pageSize, hasMore);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch paged releases for project {ProjectId}", target.ProjectId);
            return new PagedResult<UpdateInfo>([], page, pageSize, false);
        }
    }
}
 
static class StringExtensions
{
    public static string Truncate(this string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}