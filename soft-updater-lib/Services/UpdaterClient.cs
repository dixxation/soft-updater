using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace soft_updater_lib.Services;

internal class UpdaterClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public UpdaterClient(UpdaterConfig config)
    {
        _baseUrl = config.ServiceUrl.TrimEnd('/');

        var handler = new HttpClientHandler();
        if (config.IgnoreSslErrors)
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        _http = new HttpClient(handler)
        {
            Timeout = config.HttpTimeout,
            DefaultRequestHeaders = { { "X-Api-Key", config.ApiKey } },
        };
    }

    /// <summary>
    /// Проверяет наличие обновления.
    /// Возвращает null если версия актуальна (204) или сервис недоступен.
    /// </summary>
    public async Task<UpdateInfo?> GetLatestAsync(string currentVersion, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/updates/latest?currentVersion={Uri.EscapeDataString(currentVersion)}";
        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UpdateInfo>(Json, ct);
    }

    /// <summary>
    /// Возвращает все релизы новее fromVersion — для записи в releaseNotes.md
    /// </summary>
    public async Task<List<UpdateInfo>> GetChangelogAsync(string fromVersion, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/updates/changelog?from={Uri.EscapeDataString(fromVersion)}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UpdateInfo>>(Json, ct) ?? [];
    }

    /// <summary>
    /// Скачивает архив релиза, пишет в указанный файл.
    /// Вызывает onProgress(0..100) по ходу загрузки.
    /// </summary>
    public async Task DownloadReleaseAsync(
        string version,
        string destPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/updates/download/{Uri.EscapeDataString(version)}";

        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;

        await using var src  = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = File.Create(destPath);

        var buffer   = new byte[81920];
        var read     = 0L;
        int chunk;

        while ((chunk = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, chunk), ct);
            read += chunk;
            if (total > 0)
                progress?.Report((double)read / total * 100);
        }
    }

    /// <summary>
    /// Возвращает пагинированный список версий приложения.
    /// </summary>
    public async Task<PagedResult<UpdateInfo>> GetVersionsAsync(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/updates/versions?page={page}&pageSize={pageSize}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PagedResult<UpdateInfo>>(Json, ct)
               ?? new PagedResult<UpdateInfo>([], page, pageSize, false);
    }

    public void Dispose() => _http.Dispose();
}