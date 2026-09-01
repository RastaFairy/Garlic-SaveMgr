using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Services;

/// <summary>
/// Resuelve y cachea carátulas por Title ID. No participa en la ruta crítica del arranque.
/// </summary>
public sealed class CoverCacheService : IDisposable
{
    private static readonly Uri SerialStationBase = new("https://api.serialstation.com/v1/title-ids/");
    private static readonly Uri ProsperoBase = new("https://prosperopatches.com/");
    private static readonly Uri PublicCoverIndex = new("https://raw.githubusercontent.com/KytyPS5/kytyps5.github.io/main/src/data/compat-index.json");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly SemaphoreSlim _downloadGate = new(4, 4);
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private Dictionary<string, string> _index = new(StringComparer.OrdinalIgnoreCase);
    private bool _indexLoaded;

    public CoverCacheService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.UserAgent} (+covers)");
        Directory.CreateDirectory(AppPaths.CoversDirectory);
    }

    public async Task WarmAsync(IEnumerable<(string TitleId, string TitleName)> titles, Action<string>? log = null, CancellationToken ct = default)
    {
        var distinct = titles
            .Select(x => (Id: GameMetadataService.NormalizeTitleId(x.TitleId), x.TitleName))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missing = distinct.Where(x => FindCached(x.Id) is null).ToList();
        if (missing.Count == 0) return;

        await EnsureIndexAsync(ct);
        var tasks = missing.Select(x => EnsureCoverAsync(x.Id, x.TitleName, log, ct));
        await Task.WhenAll(tasks);
    }

    public async Task<string?> EnsureCoverAsync(string rawTitleId, string? titleName = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var id = GameMetadataService.NormalizeTitleId(rawTitleId);
        if (string.IsNullOrWhiteSpace(id)) return null;

        var cached = FindCached(id);
        if (cached is not null) return cached;

        await _downloadGate.WaitAsync(ct);
        try
        {
            cached = FindCached(id);
            if (cached is not null) return cached;

            var url = await ResolveCoverUrlAsync(id, ct);
            if (string.IsNullOrWhiteSpace(url)) return null;

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0) return null;

            var ext = GuessExtension(url, response.Content.Headers.ContentType?.MediaType);
            var path = Path.Combine(AppPaths.CoversDirectory, id.Replace('-', '_') + ext);
            var temp = path + ".download";
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, path, true);
            log?.Invoke($"Carátula cacheada: {id}{ext}");
            return path;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        finally { _downloadGate.Release(); }
    }

    private async Task<string?> ResolveCoverUrlAsync(string id, CancellationToken ct)
    {
        var serial = await TrySerialStationAsync(id, ct);
        if (!string.IsNullOrWhiteSpace(serial)) return serial;

        var prospero = await TryProsperoAsync(id, ct);
        if (!string.IsNullOrWhiteSpace(prospero)) return prospero;

        if (_index.TryGetValue(id, out var indexed)) return indexed;
        return null;
    }

    private async Task<string?> TrySerialStationAsync(string id, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(SerialStationBase, Uri.EscapeDataString(id)), ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return FindImageUrl(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<string?> TryProsperoAsync(string id, CancellationToken ct)
    {
        try
        {
            var rawId = id.Replace("-", "", StringComparison.Ordinal);
            using var response = await _http.GetAsync(new Uri(ProsperoBase, Uri.EscapeDataString(rawId)), ct);
            if (!response.IsSuccessStatusCode) return null;
            var html = await response.Content.ReadAsStringAsync(ct);

            foreach (var pattern in new[]
            {
                @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']",
                @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']",
                @"<meta[^>]+name=[""']twitter:image[""'][^>]+content=[""']([^""']+)[""']",
                @"<img[^>]+src=[""']([^""']+)[""']"
            })
            {
                var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!m.Success) continue;
                var value = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                    return uri.ToString();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
        }
        return null;
    }

    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        if (_indexLoaded) return;
        await _indexGate.WaitAsync(ct);
        try
        {
            if (_indexLoaded) return;
            try
            {
                using var response = await _http.GetAsync(PublicCoverIndex, ct);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    foreach (var entry in doc.RootElement.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("titleId", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                        if (!entry.TryGetProperty("cover", out var coverEl) || coverEl.ValueKind != JsonValueKind.String) continue;
                        var id = GameMetadataService.NormalizeTitleId(idEl.GetString());
                        var url = coverEl.GetString();
                        if (!string.IsNullOrWhiteSpace(id) && Uri.TryCreate(url, UriKind.Absolute, out _)) _index[id] = url!;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                LogService.Write($"No se pudo cargar índice público de carátulas: {ex.Message}", "WARN");
            }
            _indexLoaded = true;
        }
        finally { _indexGate.Release(); }
    }

    private static string? FindImageUrl(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "cover", "coverUrl", "cover_url", "image", "imageUrl", "image_url", "icon0", "pic0" })
            {
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)) return u.ToString();
                }
            }
            foreach (var property in root.EnumerateObject())
            {
                var found = FindImageUrl(property.Value);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindImageUrl(item);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        return null;
    }

    private static string? FindCached(string id)
    {
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var path = Path.Combine(AppPaths.CoversDirectory, id.Replace('-', '_') + ext);
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        }
        return null;
    }

    public static BitmapImage? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelWidth = 240;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private static string GuessExtension(string url, string? mediaType)
    {
        var lower = url.ToLowerInvariant();
        if (lower.Contains(".png")) return ".png";
        if (lower.Contains(".webp")) return ".webp";
        if (mediaType?.Contains("png", StringComparison.OrdinalIgnoreCase) == true) return ".png";
        if (mediaType?.Contains("webp", StringComparison.OrdinalIgnoreCase) == true) return ".webp";
        return ".jpg";
    }

    public void Dispose()
    {
        _http.Dispose();
        _downloadGate.Dispose();
        _indexGate.Dispose();
    }
}
