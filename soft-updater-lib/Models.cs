// ReSharper disable NotAccessedPositionalProperty.Global
namespace soft_updater_lib;

public record UpdateInfo(
    string Version,
    string ChangelogMarkdown,
    DateTime PublishedAt,
    string DownloadUrl
);


public record PagedResult<T>(
    List<T> Items,
    int Page,
    int PageSize,
    bool HasMore
);