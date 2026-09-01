using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Services;

/// <summary>
/// Gestiona la detección de la última versión del payload Garlic en GitHub Releases,
/// su descarga (con caché local) y el envío al inyector elfldr en el puerto 9021
/// de la consola detectada.
/// </summary>
public sealed class PayloadLauncherService
{
    public const int ElfldrPort = 9021;
    public const int GarlicApiPort = 8082;

    private const string PayloadSourcesConfigFile = "payload_sources.json";
    private const string DefaultGitHubApiUrl = "https://api.github.com/repos/earthonion/garlic-savemgr/releases/latest";
    private static readonly string[] DefaultPldmgrCatalogUrls =
    [
        "https://shark-ps.github.io/PS5-PLDMGR-AutoUpdater/json/ps5_saves.json",
        "https://nexgen999.github.io/PS5-Super-PLDMGR-Auto-Updater/json/ps5_saves.json"
    ];
    private const string ElfCacheFile = "garlic_payload_latest.elf";
    private const string MetaCacheFile = "garlic_payload_latest.json";
    private const string ElfCacheTempFile = "garlic_payload_latest.elf.download";

    private static readonly TimeSpan GarlicWaitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan GarlicPollInterval = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(12);

    private readonly string _cacheDir;
    private readonly PayloadSourceConfig _payloadSources;

    public PayloadLauncherService()
    {
        _cacheDir = AppPaths.PayloadDirectory;
        Directory.CreateDirectory(_cacheDir);
        _payloadSources = LoadPayloadSourceConfig();
    }

    public async Task<string?> GetRunningVersionAsync(string consoleIp, CancellationToken ct = default)
    {
        try
        {
            using var api = new GarlicApi(consoleIp, GarlicApiPort);
            var apiVersion = await api.QueryVersionAsync(ct);
            if (!string.IsNullOrEmpty(apiVersion))
                return apiVersion;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Write($"GetRunningVersion: no se pudo consultar Garlic en {consoleIp}: {ex.Message}", "WARN");
        }

        return null;
    }

    public Task<(string? Version, bool Cached, string? Sha256)> PrepareLatestPayloadCacheAsync(
        Action<string, string>? log = null,
        CancellationToken ct = default) => PrepareLatestPayloadCacheCoreAsync(log, ct);

    public async Task<(string? Version, string? Sha256)> GetCachedPayloadVersionAsync()
    {
        var meta = LoadCachedMeta(Path.Combine(_cacheDir, MetaCacheFile));
        var path = Path.Combine(_cacheDir, ElfCacheFile);
        await Task.CompletedTask;
        return meta is not null && File.Exists(path)
            ? (meta.TagName, meta.Sha256)
            : (null, null);
    }

    public static int CompareVersions(string left, string right)
    {
        var a = ParseVersion(left);
        var b = ParseVersion(right);
        for (var i = 0; i < 3; i++)
        {
            var c = a[i].CompareTo(b[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    private static int[] ParseVersion(string value)
    {
        var numbers = System.Text.RegularExpressions.Regex.Matches(value ?? string.Empty, @"\d+")
            .Select(m => int.TryParse(m.Value, out var n) ? n : 0)
            .Take(3)
            .ToList();
        while (numbers.Count < 3) numbers.Add(0);
        return numbers.ToArray();
    }

    private async Task<(string? Version, bool Cached, string? Sha256)> PrepareLatestPayloadCacheCoreAsync(
        Action<string, string>? log = null,
        CancellationToken ct = default)
    {
        try
        {
            var release = await FetchLatestReleaseAsync(ct);
            if (release is null)
            {
                var cached = LoadCachedMeta(Path.Combine(_cacheDir, MetaCacheFile));
                var elfPath = Path.Combine(_cacheDir, ElfCacheFile);
                var ok = cached is not null && File.Exists(elfPath);
                log?.Invoke(ok
                    ? $"Payload cacheado disponible ({cached!.TagName})."
                    : "No se pudo consultar el payload remoto y no hay caché válida.", ok ? "info" : "warn");
                return (cached?.TagName, ok, cached?.Sha256);
            }

            var path = await EnsureElfAsync(release, log, null, ct);
            var meta = LoadCachedMeta(Path.Combine(_cacheDir, MetaCacheFile));
            return (release.TagName, path is not null && meta is not null, meta?.Sha256);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"No se pudo preparar la caché del payload: {ex.Message}", "warn");
            return (null, false, null);
        }
    }

    public async Task<bool> EnsureGarlicRunningAsync(
        string consoleIp,
        Action<string, string>? log = null,
        IProgress<(long Done, long Total)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            log?.Invoke("Comprobando última versión del payload en GitHub…", "info");
            var release = await FetchLatestReleaseAsync(ct);
            if (release is null)
            {
                log?.Invoke("No se pudo consultar GitHub. Usando payload cacheado si existe.", "warn");
            }
            else
            {
                log?.Invoke($"Última versión del payload: {release.TagName} ({release.ElfName})", "info");
            }

            var elfPath = await EnsureElfAsync(release, log, progress, ct);
            if (elfPath is null)
            {
                log?.Invoke("No hay payload disponible para enviar.", "error");
                return false;
            }

            log?.Invoke($"Enviando payload a {consoleIp}:{ElfldrPort} (elfldr)…", "info");
            await SendElfAsync(consoleIp, ElfldrPort, elfPath, ct);
            log?.Invoke("Payload enviado. Esperando que Garlic responda…", "info");

            var up = await WaitForGarlicAsync(consoleIp, GarlicApiPort, log, ct);
            if (up) log?.Invoke("Garlic está ejecutándose.", "ok");
            else log?.Invoke("Tiempo de espera agotado. Garlic no respondió.", "error");
            return up;
        }
        catch (OperationCanceledException)
        {
            log?.Invoke("Lanzamiento del payload cancelado.", "warn");
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"ERR lanzando payload: {ex.Message}", "error");
            LogService.Write($"PayloadLauncher: {ex}", "ERROR");
            return false;
        }
    }

    private async Task<ReleaseInfo?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            var candidates = new List<ReleaseInfo>();
            foreach (var catalogUrl in _payloadSources.PldmgrCatalogUrls)
            {
                try
                {
                    using var http = CreateHttpClient();
                    var uri = catalogUrl + (catalogUrl.Contains('?') ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        LogService.Write($"PLDMGR HTTP {(int)response.StatusCode}: {catalogUrl}", "WARN");
                        continue;
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    var match = FindCatalogEntry(doc.RootElement);
                    if (match is null || string.IsNullOrWhiteSpace(match.Version) || string.IsNullOrWhiteSpace(match.Url))
                        continue;

                    candidates.Add(new ReleaseInfo(
                        NormalizeTag(match.Version),
                        string.IsNullOrWhiteSpace(match.FileName) ? $"garlic-savemgr_{NormalizeTag(match.Version)}.elf" : match.FileName,
                        match.Url,
                        match.Size,
                        match.Source ?? catalogUrl,
                        match.Checksum));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogService.Write($"PLDMGR catálogo no disponible ({catalogUrl}): {ex.Message}", "WARN");
                }
            }

            if (candidates.Count > 0)
            {
                var selected = candidates.OrderByDescending(x => ParseVersion(x.TagName), VersionArrayComparer.Instance).First();
                LogService.Write($"PLDMGR: garlic-savemgr {selected.TagName} (seleccionado entre {candidates.Count} catálogo(s)).", "INFO");
                return selected;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogService.Write($"PLDMGR catalog no disponible: {ex.Message}", "WARN");
        }

        if (string.IsNullOrWhiteSpace(_payloadSources.GitHubApiUrl))
        {
            LogService.Write("GitHub fallback deshabilitado en payload_sources.json.", "WARN");
            return null;
        }

        try
        {
            using var http = CreateHttpClient();
            using var response = await http.GetAsync(_payloadSources.GitHubApiUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                LogService.Write($"GitHub API HTTP {(int)response.StatusCode}", "WARN");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var tagName = GetString(root, "tag_name");
            var htmlUrl = GetString(root, "html_url");
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = GetString(asset, "name");
                var downloadUrl = GetString(asset, "browser_download_url");
                var size = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0L;
                if (name.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
                    return new ReleaseInfo(NormalizeTag(tagName), name, downloadUrl, size, htmlUrl, null);
            }

            LogService.Write("GitHub latest release no contiene ningún .elf.", "WARN");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogService.Write($"FetchLatestRelease: {ex.Message}", "WARN");
        }
        return null;
    }

    private static CatalogEntry? FindCatalogEntry(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = ParseCatalogEntry(item);
                if (found is not null) return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("payloads", out var payloads)) return FindCatalogEntry(payloads);
            if (root.TryGetProperty("items", out var items)) return FindCatalogEntry(items);
            var found = ParseCatalogEntry(root);
            if (found is not null) return found;
        }
        return null;
    }

    private static CatalogEntry? ParseCatalogEntry(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var name = GetString(item, "name");
        if (!name.Equals("garlic-savemgr", StringComparison.OrdinalIgnoreCase)) return null;
        return new CatalogEntry(
            GetString(item, "version"),
            GetString(item, "filename"),
            GetString(item, "url"),
            GetString(item, "checksum"),
            GetString(item, "source"),
            item.TryGetProperty("size", out var size) && size.TryGetInt64(out var n) ? n : 0L);
    }

    private static string NormalizeTag(string value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : (value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? value : "v" + value);

    private async Task<string?> EnsureElfAsync(
        ReleaseInfo? release,
        Action<string, string>? log,
        IProgress<(long Done, long Total)>? progress,
        CancellationToken ct)
    {
        var elfPath = Path.Combine(_cacheDir, ElfCacheFile);
        var metaPath = Path.Combine(_cacheDir, MetaCacheFile);
        var cached = LoadCachedMeta(metaPath);

        if (release is null)
        {
            if (File.Exists(elfPath))
            {
                log?.Invoke($"Sin acceso a GitHub; usando payload cacheado ({cached?.TagName ?? "versión desconocida"}).", "warn");
                return elfPath;
            }
            log?.Invoke("Sin payload cacheado y sin acceso a GitHub. No se puede continuar.", "error");
            return null;
        }

        var needsDownload = !File.Exists(elfPath)
            || cached is null
            || !string.Equals(cached.TagName, release.TagName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(cached.ElfName, release.ElfName, StringComparison.OrdinalIgnoreCase);

        if (!needsDownload)
        {
            if ((!string.IsNullOrEmpty(cached!.Sha256) && !VerifyFile(elfPath, cached.Sha256)) ||
                (!string.IsNullOrEmpty(release.ExpectedSha256) && !VerifyFile(elfPath, release.ExpectedSha256)))
            {
                log?.Invoke("El payload cacheado no supera la verificación de integridad. Descargando de nuevo…", "warn");
                needsDownload = true;
            }
            else
            {
                log?.Invoke($"Payload {release.TagName} ya cacheado y actualizado.", "info");
                return elfPath;
            }
        }

        log?.Invoke($"Descargando {release.ElfName} desde la fuente catalogada…", "info");
        var tempPath = Path.Combine(_cacheDir, ElfCacheTempFile);
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            var sha256 = await DownloadElfAsync(release.DownloadUrl, tempPath, release.Size, progress, ct);
            if (!string.IsNullOrWhiteSpace(release.ExpectedSha256) &&
                !string.Equals(sha256, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteTempFile(tempPath);
                log?.Invoke("El payload descargado no coincide con el SHA-256 publicado. Se ha descartado.", "error");
                LogService.Write($"Payload SHA-256 mismatch: esperado {release.ExpectedSha256}, obtenido {sha256}", "ERROR");
                return null;
            }

            File.Move(tempPath, elfPath, true);
            SaveCachedMeta(metaPath, new CachedMeta(release.TagName, release.ElfName, release.HtmlUrl, sha256));
            log?.Invoke($"Payload {release.TagName} cacheado correctamente ({FormatBytes(new FileInfo(elfPath).Length)}).", "ok");
            LogService.Write($"Payload cache actualizado: {release.TagName} SHA-256={sha256}", "INFO");
            return elfPath;
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static async Task<string> DownloadElfAsync(
        string url,
        string destPath,
        long expectedSize,
        IProgress<(long Done, long Total)>? progress,
        CancellationToken ct)
    {
        using var http = CreateHttpClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? expectedSize;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[1 << 20];
        long done = 0;
        int n;
        while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), ct);
            sha.AppendData(buffer, 0, n);
            done += n;
            progress?.Report((done, total));
        }

        await output.FlushAsync(ct);
        return Convert.ToHexString(sha.GetCurrentHash()).ToLowerInvariant();
    }

    private static async Task SendElfAsync(string ip, int port, string elfPath, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(SendTimeout);
        using var client = new TcpClient();
        client.SendTimeout = (int)SendTimeout.TotalMilliseconds;
        client.ReceiveTimeout = (int)SendTimeout.TotalMilliseconds;

        await client.ConnectAsync(ip, port, cts.Token);
        await using var ns = client.GetStream();
        await using var fs = new FileStream(elfPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        await fs.CopyToAsync(ns, 1 << 20, cts.Token);
        await ns.FlushAsync(cts.Token);
    }

    private static async Task<bool> WaitForGarlicAsync(string ip, int port, Action<string, string>? log, CancellationToken ct)
    {
        using var api = new GarlicApi(ip, port);
        var deadline = DateTime.UtcNow + GarlicWaitTimeout;
        var lastDotSeconds = -1;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await api.PingAsync(ct, TimeSpan.FromMilliseconds(600)))
                return true;

            var remaining = (int)(deadline - DateTime.UtcNow).TotalSeconds;
            var elapsed = (int)(GarlicWaitTimeout.TotalSeconds - remaining);
            var dotAt = elapsed / 5;
            if (dotAt != lastDotSeconds)
            {
                log?.Invoke($"Esperando Garlic… ({remaining} s restantes)", "info");
                lastDotSeconds = dotAt;
            }

            await Task.Delay(GarlicPollInterval, ct);
        }

        return false;
    }

    private static PayloadSourceConfig LoadPayloadSourceConfig()
    {
        var path = Path.Combine(AppPaths.AppDataDirectory, PayloadSourcesConfigFile);
        var defaults = new PayloadSourceConfig(DefaultGitHubApiUrl, DefaultPldmgrCatalogUrls.ToList());

        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
                LogService.Write($"Configuración de payload creada en {path}.", "INFO");
                return defaults;
            }

            var json = File.ReadAllText(path);
            var configured = JsonSerializer.Deserialize<PayloadSourceConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (configured is null)
            {
                LogService.Write($"Configuración de payload vacía o inválida: {path}. Usando valores predeterminados.", "WARN");
                return defaults;
            }

            var catalogs = configured.PldmgrCatalogUrls?
                .Where(IsValidHttpUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            if (configured.PldmgrCatalogUrls is { Count: > 0 } && catalogs.Count == 0)
            {
                LogService.Write($"Los catálogos PLDMGR configurados no contienen URLs HTTP(S) válidas: {path}. Usando valores predeterminados.", "WARN");
                catalogs = defaults.PldmgrCatalogUrls.ToList();
            }

            if (configured.PldmgrCatalogUrls is null)
                catalogs = defaults.PldmgrCatalogUrls.ToList();

            var githubUrl = string.IsNullOrWhiteSpace(configured.GitHubApiUrl)
                ? defaults.GitHubApiUrl
                : configured.GitHubApiUrl.Trim();

            if (!string.IsNullOrWhiteSpace(githubUrl) && !IsValidHttpUrl(githubUrl))
            {
                LogService.Write($"URL de GitHub inválida en {path}: {githubUrl}. Usando valor predeterminado.", "WARN");
                githubUrl = defaults.GitHubApiUrl;
            }

            LogService.Write($"Configuración de payload cargada: {catalogs.Count} catálogo(s) PLDMGR.", "INFO");
            return new PayloadSourceConfig(githubUrl, catalogs);
        }
        catch (Exception ex)
        {
            LogService.Write($"No se pudo cargar payload_sources.json ({ex.Message}). Usando valores predeterminados.", "WARN");
            return defaults;
        }
    }

    private static bool IsValidHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static CachedMeta? LoadCachedMeta(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CachedMeta>(json);
        }
        catch (Exception ex)
        {
            LogService.Write($"LoadCachedMeta: no se pudo leer {path}: {ex.Message}", "WARN");
            return null;
        }
    }

    private static void SaveCachedMeta(string path, CachedMeta meta)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(meta)); }
        catch (Exception ex) { LogService.Write($"SaveCachedMeta: {ex.Message}", "WARN"); }
    }

    private static bool VerifyFile(string path, string expectedHex)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            return string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogService.Write($"VerifyFile: no se pudo verificar {path}: {ex.Message}", "WARN");
            return false;
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
        }
        catch (Exception ex)
        {
            LogService.Write($"Cleanup payload temporal: no se pudo eliminar {path}: {ex.Message}", "WARN");
        }
    }

    private sealed class VersionArrayComparer : IComparer<int[]>
    {
        public static readonly VersionArrayComparer Instance = new();
        public int Compare(int[]? x, int[]? y)
        {
            x ??= Array.Empty<int>();
            y ??= Array.Empty<int>();
            var len = Math.Max(x.Length, y.Length);
            for (var i = 0; i < len; i++)
            {
                var a = i < x.Length ? x[i] : 0;
                var b = i < y.Length ? y[i] : 0;
                var c = a.CompareTo(b);
                if (c != 0) return c;
            }
            return 0;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = HttpTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string GetString(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";

    private static string FormatBytes(long n)
    {
        double d = n;
        foreach (var u in new[] { "B", "KB", "MB", "GB" })
        {
            if (d < 1024) return $"{d:0.0} {u}";
            d /= 1024;
        }
        return $"{d:0.0} TB";
    }

    private sealed record ReleaseInfo(string TagName, string ElfName, string DownloadUrl, long Size, string HtmlUrl, string? ExpectedSha256);
    private sealed record CachedMeta(string TagName, string ElfName, string HtmlUrl, string Sha256);
    private sealed record CatalogEntry(string Version, string FileName, string Url, string Checksum, string Source, long Size);
    private sealed record PayloadSourceConfig(string? GitHubApiUrl, List<string> PldmgrCatalogUrls);
}
