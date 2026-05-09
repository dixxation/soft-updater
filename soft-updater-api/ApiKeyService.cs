namespace soft_updater_api;

public class ApiKeySettings
{
    public required string MasterKey { get; init; }
    // X-Api-Key → GitLab project id
    public required Dictionary<string, int> Keys { get; init; }
}

public class ApiKeyService(ApiKeySettings settings)
{
    public const string Header = "X-Api-Key";

    // Возвращает projectId или null если ключ не найден
    public int? Resolve(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return null;

        // Master key видит все проекты — возвращаем специальное значение
        if (apiKey == settings.MasterKey)
            return MasterProjectId;

        return settings.Keys.TryGetValue(apiKey, out var id) ? id : null;
    }

    public bool IsMaster(string? apiKey) =>
        !string.IsNullOrEmpty(apiKey) && apiKey == settings.MasterKey;

    public IEnumerable<int> AllProjectIds() => settings.Keys.Values.Distinct();

    // Sentinel — означает "все проекты" для master key
    public const int MasterProjectId = -1;
}