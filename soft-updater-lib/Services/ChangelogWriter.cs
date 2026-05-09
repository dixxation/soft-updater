using soft_updater_model;

namespace soft_updater_lib.Services;

internal static class ChangelogWriter
{
    private const string FileName = "releaseNotes.md";

    /// <summary>
    /// Перезаписывает releaseNotes.md рядом с exe.
    /// Каждый релиз — отдельная секция с версией и датой.
    /// </summary>
    public static async Task WriteAsync(IEnumerable<UpdateInfo> releases, string? directory = null)
    {
        var dir  = directory ?? AppContext.BaseDirectory;
        var path = Path.Combine(dir, FileName);

        await using var writer = new StreamWriter(path, append: false);

        foreach (var release in releases)
        {
            await writer.WriteLineAsync($"## {release.Version}  ");
            await writer.WriteLineAsync($"*{release.PublishedAt:yyyy-MM-dd}*");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(release.ChangelogMarkdown.Trim());
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("---");
            await writer.WriteLineAsync();
        }
    }
}