namespace soft_updater_lib;

public class UpdaterConfig
{
    /// <summary>URL микросервиса обновлений</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>X-Api-Key — зашивается в сборку приложения</summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Текущая версия приложения.
    /// Если не указана явно — берётся из Assembly автоматически.
    /// </summary>
    public string? CurrentVersion { get; init; }

    /// <summary>Интервал фоновой проверки. Default: 5 минут</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Директория куда распаковывать обновление.
    /// Default: директория исполняемого файла.
    /// </summary>
    public string? InstallDirectory { get; init; }

    /// <summary>Таймаут HTTP запросов. Default: 30 секунд</summary>
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Игнорировать SSL ошибки (только для dev/self-signed)</summary>
    public bool IgnoreSslErrors { get; init; }
}