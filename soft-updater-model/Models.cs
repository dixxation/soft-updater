// ReSharper disable NotAccessedPositionalProperty.Global
namespace soft_updater_model;

public record UpdateInfo(
    string Version,
    string ChangelogMarkdown,
    DateTime PublishedAt,
    string DownloadUrl
);
 
public record AppStatus(
    int     ProjectId,
    string? LatestVersion,
    DateTime? PublishedAt,
    bool Available
);
 
public record HealthStatus(
    string Status,
    DateTime Timestamp,
    GitLabHealth GitLab
);
 
public record GitLabHealth(
    bool Reachable,
    string? AuthenticatedAs,
    string? Error
);
 
public record GitLabRelease(
    string TagName,
    string Name,
    string? Description,
    DateTime CreatedAt,
    GitLabAssets? Assets
);
 
public record GitLabAssets(
    List<GitLabSource>? Sources,
    List<GitLabLink>? Links
);
 
public record GitLabSource(
    string Format,
    string Url
);
 
// Прикреплённые файлы релиза — assets.links
public record GitLabLink(
    int Id,
    string Name,
    string Url,
    string DirectAssetUrl,
    string LinkType
);
