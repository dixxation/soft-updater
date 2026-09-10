using System.Text.RegularExpressions;

namespace soft_updater_api;

/// <summary>
/// Приложение, за которым закреплён X-Api-Key: проект в GitLab и — опционально — маска файла
/// в ассетах релиза.
///
/// Маска нужна там, где из одного репозитория выходит несколько сборок одной версии и все они
/// лежат ассетами в одном релизе (варианты продукта, платформы). Без неё сервис отдаёт первую
/// ссылку релиза, а её порядок задаёт GitLab, а не имя файла — то есть клиент получил бы
/// произвольную из них.
/// </summary>
public record AppTarget(int ProjectId, string? Asset)
{
    /// <summary>Файл релиза выбирается маской, а не «первый попавшийся».</summary>
    public bool HasAssetFilter => !string.IsNullOrWhiteSpace(Asset);
}

public class ApiKeySettings
{
    public required string MasterKey { get; init; }

    // X-Api-Key → приложение (проект GitLab + маска ассета)
    public required Dictionary<string, AppTarget> Keys { get; init; }
}

public class ApiKeyService(ApiKeySettings settings)
{
    public const string Header = "X-Api-Key";

    // Возвращает приложение или null если ключ не найден
    public AppTarget? Resolve(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return null;

        // Master key видит все проекты — возвращаем специальное значение
        if (apiKey == settings.MasterKey)
            return new AppTarget(MasterProjectId, null);

        return settings.Keys.TryGetValue(apiKey, out var target) ? target : null;
    }

    public bool IsMaster(string? apiKey) =>
        !string.IsNullOrEmpty(apiKey) && apiKey == settings.MasterKey;

    /// <summary>Все зарегистрированные приложения. Два ключа на один проект с разными масками —
    /// это два разных приложения, поэтому различаем и по маске тоже.</summary>
    public IEnumerable<AppTarget> AllTargets() =>
        settings.Keys.Values.DistinctBy(t => (t.ProjectId, t.Asset?.ToLowerInvariant()));

    // Sentinel — означает "все проекты" для master key
    public const int MasterProjectId = -1;
}

/// <summary>
/// Подбор файла релиза по маске вида <c>*-factoryOnly.zip</c>.
/// Поддерживает <c>*</c> и <c>?</c>, регистр не важен.
/// </summary>
public static class AssetMatcher
{
    public static bool Matches(string? pattern, string? name)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        if (string.IsNullOrWhiteSpace(name)) return false;

        // Маску без подстановочных знаков трактуем как «имя содержит»: так в конфиге
        // достаточно написать factoryOnly вместо *factoryOnly*.
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return name.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase);
    }
}
