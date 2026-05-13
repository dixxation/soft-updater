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
 
    public async Task<UpdateInfo?> GetLatestAsync(int projectId, string? currentVersion = null)
    {
        var latest = await FetchLatestReleaseAsync(projectId);
        if (latest is null) return null;
 
        if (currentVersion is not null && !IsNewer(latest.TagName, currentVersion))
            return null;
 
        return MapToUpdateInfo(projectId, latest);
    }
 
    public async Task<AppStatus> GetAppStatusAsync(int projectId)
    {
        var latest = await FetchLatestReleaseAsync(projectId);
        return new AppStatus(
            ProjectId:     projectId,
            LatestVersion: latest?.TagName,
            PublishedAt:   latest?.CreatedAt,
            Available:     latest is not null
        );
    }
 
    public async Task<List<UpdateInfo>> GetChangelogAsync(int projectId, string fromVersion, int maxReleases = 20)
    {
        var releases = await FetchReleasesAsync(projectId, maxReleases);
 
        return releases
            .TakeWhile(r => IsNewer(r.TagName, fromVersion))
            .Select(r => MapToUpdateInfo(projectId, r))
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
 
    public async Task<Stream?> GetArchiveStreamAsync(int projectId, string version)
    {
        var release = await FetchReleaseByTagAsync(projectId, version);
 
        // Приоритет: assets.links (прикреплённый build) → assets.sources (исходники GitLab)
        var downloadUrl =
            release?.Assets?.Links?.FirstOrDefault()?.DirectAssetUrl
            ?? release?.Assets?.Sources?.FirstOrDefault(s => s.Format.Equals("zip", StringComparison.OrdinalIgnoreCase))?.Url;
 
        if (downloadUrl is null) return null;
 
        var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
 
        return await response.Content.ReadAsStreamAsync();
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

    public async Task<PagedResult<UpdateInfo>> GetReleasesPagedAsync(int projectId, int page, int pageSize)
    {
        // Запрашиваем на 1 больше, чтобы определить hasMore без лишнего запроса
        var url = $"{settings.ApiUrl}/projects/{projectId}/releases?page={page}&per_page={pageSize + 1}";
        try
        {
            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize<List<GitLabRelease>>(json, Json) ?? [];
 
            var hasMore = releases.Count > pageSize;
            var items   = releases.Take(pageSize).Select(r => MapToUpdateInfo(projectId, r)).ToList();
 
            return new PagedResult<UpdateInfo>(items, page, pageSize, hasMore);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch paged releases for project {ProjectId}", projectId);
            return new PagedResult<UpdateInfo>([], page, pageSize, false);
        }
    }
}
 
static class StringExtensions
{
    public static string Truncate(this string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}