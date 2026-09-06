using System.IO;
using System.Net.Http.Headers;
using System.Text.Json;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.UpdaterModule.Services;

public sealed class GitHubReleaseService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/RastaFairy/Garlic-SaveMgr/releases/latest";
    private readonly HttpClient _http;
    private readonly string _cacheFile;
    public bool UsedCachedResult { get; private set; }

    public GitHubReleaseService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        _cacheFile = Path.Combine(AppPaths.AppDataDirectory, "updater_latest.json");
        Directory.CreateDirectory(AppPaths.AppDataDirectory);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GarlicSaveMgr-UpdaterModule", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        UsedCachedResult = false;
        if (!forceRefresh && TryLoadRecentCache(out var recent))
        {
            UsedCachedResult = true;
            return recent;
        }
        try
        {
            using var response = await _http.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(_cacheFile, json, cancellationToken).ConfigureAwait(false);
                return ParseRelease(json);
            }

            if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
                return LoadCachedOrThrow("GitHub ha limitado temporalmente las consultas del actualizador.");

            return LoadCachedOrThrow($"GitHub respondió HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LoadCachedOrThrow("La consulta a GitHub agotó el tiempo de espera.");
        }
        catch (HttpRequestException ex)
        {
            return LoadCachedOrThrow($"No se pudo consultar GitHub: {ex.Message}");
        }
    }

    private GitHubReleaseInfo ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = root.GetProperty("tag_name").GetString()?.Trim() ?? throw new InvalidDataException("GitHub no devolvió tag_name.");
        var name = root.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? tag : tag;
        var body = root.TryGetProperty("body", out var bodyNode) ? bodyNode.GetString() ?? string.Empty : string.Empty;
        var html = root.TryGetProperty("html_url", out var htmlNode) ? htmlNode.GetString() ?? string.Empty : string.Empty;

        GitHubReleaseInfo? selected = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var download = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(download)) continue;
                if (!assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                var candidate = new GitHubReleaseInfo(
                    tag,
                    name,
                    body,
                    html,
                    assetName,
                    download,
                    asset.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var size) ? size : 0,
                    asset.TryGetProperty("digest", out var digestNode) ? digestNode.GetString() : null);

                if (assetName.Equals("Garlic_SaveMgr.exe", StringComparison.OrdinalIgnoreCase))
                {
                    selected = candidate;
                    break;
                }

                if (selected is null && assetName.Contains("Garlic", StringComparison.OrdinalIgnoreCase))
                    selected = candidate;
            }
        }

        return selected ?? throw new InvalidDataException("La release no publica un ejecutable .exe de Garlic SaveMgr.");
    }

    private bool TryLoadRecentCache(out GitHubReleaseInfo release)
    {
        release = null!;
        if (!File.Exists(_cacheFile)) return false;
        try
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFile);
            if (age > TimeSpan.FromHours(12)) return false;
            release = ParseRelease(File.ReadAllText(_cacheFile));
            return true;
        }
        catch { return false; }
    }

    private GitHubReleaseInfo LoadCachedOrThrow(string reason)
    {
        if (File.Exists(_cacheFile))
        {
            try
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFile);
                if (age <= TimeSpan.FromDays(7))
                {
                    UsedCachedResult = true;
                    var release = ParseRelease(File.ReadAllText(_cacheFile));
                    return release;
                }
            }
            catch { }
        }
        throw new HttpRequestException(reason);
    }

    public async Task DownloadAsync(GitHubReleaseInfo release, string destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(release.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? release.AssetSize;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64, useAsync: true);
        var buffer = new byte[1024 * 64];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            progress?.Report(total > 0 ? done : done);
        }
    }
}
