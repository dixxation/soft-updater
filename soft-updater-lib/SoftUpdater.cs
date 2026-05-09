using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using soft_updater_lib.Installer;
using soft_updater_lib.Services;
using soft_updater_model;

namespace soft_updater_lib;

public sealed class SoftUpdater : IAsyncDisposable
{
    private readonly UpdaterConfig  _config;
    private readonly UpdaterClient  _client;
    private readonly ILogger        _logger;
    private readonly string         _currentVersion;
    private readonly string         _installDirectory;

    private CancellationTokenSource? _cts;
    private Task?                    _backgroundTask;

    // ── События ──────────────────────────────────────────────────────────────

    /// <summary>Вызывается когда найдена новая версия. Из любого потока.</summary>
    public event Func<UpdateInfo, Task>? UpdateAvailable;

    /// <summary>Прогресс скачивания архива 0..100</summary>
    public event Action<double>? DownloadProgress;

    // ── Конструктор ───────────────────────────────────────────────────────────

    public SoftUpdater(UpdaterConfig config, ILogger<SoftUpdater>? logger = null)
    {
        _config           = config;
        _client           = new UpdaterClient(config);
        _logger           = logger ?? NullLogger<SoftUpdater>.Instance;
        _currentVersion   = ResolveVersion(config.CurrentVersion);
        _installDirectory = config.InstallDirectory ?? AppContext.BaseDirectory;
    }

    // ── Публичный API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Запускает фоновую проверку по расписанию.
    /// При нахождении новой версии вызывает UpdateAvailable.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _backgroundTask = RunBackgroundLoopAsync(_cts.Token);
        _logger.LogInformation("SoftUpdater started. Current version: {Version}", _currentVersion);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Разовая проверка обновления без запуска фонового цикла.
    /// </summary>
    public async Task<UpdateInfo?> CheckOnceAsync(CancellationToken ct = default)
    {
        try
        {
            return await _client.GetLatestAsync(_currentVersion, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    /// <summary>
    /// Скачивает архив и changelog параллельно, затем запускает инсталлер.
    ///
    /// После вызова этого метода приложение завершится — инсталлер
    /// распакует архив и перезапустит exe.
    /// </summary>
    public async Task ApplyUpdateAsync(UpdateInfo update, CancellationToken ct = default)
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"soft-updater-{update.Version}.zip");

        _logger.LogInformation("Downloading update {Version}...", update.Version);

        // Скачивание и сбор changelog — параллельно
        var downloadTask  = DownloadArchiveAsync(update, archivePath, ct);
        var changelogTask = WriteChangelogAsync(ct);

        await Task.WhenAll(downloadTask, changelogTask);

        _logger.LogInformation("Download complete. Launching installer...");

        InstallerRunner.LaunchAndExit(archivePath, _installDirectory);
        // После этой строки процесс завершится — код ниже недостижим
    }

    // ── Приватные методы ──────────────────────────────────────────────────────

    private async Task RunBackgroundLoopAsync(CancellationToken ct)
    {
        // Первая проверка сразу при запуске
        await CheckAndNotifyAsync(ct);

        using var timer = new PeriodicTimer(_config.CheckInterval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await CheckAndNotifyAsync(ct);
        }
    }

    private async Task CheckAndNotifyAsync(CancellationToken ct)
    {
        try
        {
            var update = await _client.GetLatestAsync(_currentVersion, ct);
            if (update is null) return;

            _logger.LogInformation("New version available: {Version}", update.Version);

            if (UpdateAvailable is not null)
                await UpdateAvailable.Invoke(update);
        }
        catch (OperationCanceledException) { /* штатная остановка */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background update check failed");
        }
    }

    private async Task DownloadArchiveAsync(UpdateInfo update, string archivePath, CancellationToken ct)
    {
        var progress = new Progress<double>(p => DownloadProgress?.Invoke(p));
        await _client.DownloadReleaseAsync(update.Version, archivePath, progress, ct);
    }

    private async Task WriteChangelogAsync(CancellationToken ct)
    {
        try
        {
            var releases = await _client.GetChangelogAsync(_currentVersion, ct);
            await ChangelogWriter.WriteAsync(releases, _installDirectory);
            _logger.LogInformation("releaseNotes.md written ({Count} releases)", releases.Count);
        }
        catch (Exception ex)
        {
            // Не критично — основной процесс продолжается
            _logger.LogWarning(ex, "Failed to write releaseNotes.md");
        }
    }

    /// <summary>
    /// Берёт версию из Assembly если не задана явно в конфиге.
    /// Соответствует &lt;Version&gt; в .csproj.
    /// </summary>
    private static string ResolveVersion(string? explicitVersion)
    {
        if (!string.IsNullOrEmpty(explicitVersion))
            return explicitVersion;

        return Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            // InformationalVersion может содержать +commit суффикс — обрезаем
            ?.Split('+')[0]
            ?? "0.0.0";
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_backgroundTask is not null)
                await _backgroundTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            _cts.Dispose();
        }
        _client.Dispose();
    }
}