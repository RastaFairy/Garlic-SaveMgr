using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

public sealed class GameMetadataService
{
    private static readonly Uri SerialStationBase = new("https://api.serialstation.com/v1/title-ids/");
    private static readonly Uri ProsperoBase = new("https://prosperopatches.com/");
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, MetadataRecord> _cache = new(StringComparer.OrdinalIgnoreCase);

    public GameMetadataService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.UserAgent} (+metadata lookup)");
        LoadCache();
    }

    public async Task ResolveMissingAsync(IList<TitleInfo> titles, Action<TitleInfo>? updated = null, CancellationToken ct = default)
    {
        var missing = titles.Where(t => string.IsNullOrWhiteSpace(t.TitleName) || t.TitleName == t.TitleId).ToList();
        foreach (var title in missing)
        {
            ct.ThrowIfCancellationRequested();
            var name = await ResolveAsync(title.TitleId, ct);
            if (string.IsNullOrWhiteSpace(name)) continue;
            title.TitleName = name;
            updated?.Invoke(title);
        }
    }

    public void SetCoverPath(string? rawTitleId, string? path)
    {
        var id = NormalizeTitleId(rawTitleId);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path)) return;
        var now = DateTimeOffset.UtcNow;
        if (_cache.TryGetValue(id, out var existing)) _cache[id] = existing with { CoverPath = path, RetrievedUtc = now };
        else _cache[id] = new MetadataRecord(string.Empty, path, now);
        SaveCache();
    }

    public string? GetCachedCoverPath(string? rawTitleId)
    {
        var id = NormalizeTitleId(rawTitleId);
        return _cache.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value.CoverPath) && File.Exists(value.CoverPath) ? value.CoverPath : null;
    }

    public async Task<string?> ResolveAsync(string? rawTitleId, CancellationToken ct = default)
    {
        var id = NormalizeTitleId(rawTitleId);
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (_cache.TryGetValue(id, out var cached) && !string.IsNullOrWhiteSpace(cached.Name)) return cached.Name;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(id, out cached) && !string.IsNullOrWhiteSpace(cached.Name)) return cached.Name;
            string? name = await TrySerialStationAsync(id, ct);
            if (string.IsNullOrWhiteSpace(name)) name = await TryProsperoPatchesAsync(id, ct);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _cache[id] = new MetadataRecord(name.Trim(), _cache.TryGetValue(id, out var old) ? old.CoverPath : null, DateTimeOffset.UtcNow);
                SaveCache();
            }
            return name;
        }
        finally { _gate.Release(); }
    }

    private async Task<string?> TrySerialStationAsync(string id, CancellationToken ct)
    {
        try
        {
            foreach (var variant in new[] { id, id.Replace("-", "", StringComparison.Ordinal) }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var response = await _http.GetAsync(new Uri(SerialStationBase, Uri.EscapeDataString(variant)), ct);
                if (!response.IsSuccessStatusCode) continue;
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var name = ExtractName(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> TryProsperoPatchesAsync(string id, CancellationToken ct)
    {
        try
        {
            var rawId = id.Replace("-", "", StringComparison.Ordinal);
            using var response = await _http.GetAsync(new Uri(ProsperoBase, Uri.EscapeDataString(rawId)), ct);
            if (!response.IsSuccessStatusCode) return null;
            var html = await response.Content.ReadAsStringAsync(ct);

            var title = Regex.Match(html, @"<h1[^>]*>\s*([^<]+?)\s*</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(title)) return WebUtility.HtmlDecode(title).Trim();

            title = Regex.Match(html, @"<title[^>]*>\s*([^<]+?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(title))
            {
                title = WebUtility.HtmlDecode(title).Trim();
                title = Regex.Replace(title, @"\s*[-|]\s*Prospero(?:Patches)?\s*$", "", RegexOptions.IgnoreCase).Trim();
                if (!string.Equals(title, id, StringComparison.OrdinalIgnoreCase)) return title;
            }
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static string? ExtractName(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in new[] { "titleName", "title_name", "name", "mainTitle", "main_title", "gameName", "game_name" })
        {
            if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s) && !s.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return s;
            }
        }
        foreach (var key in new[] { "game", "title", "data", "result" })
        {
            if (root.TryGetProperty(key, out var v))
            {
                var nested = ExtractName(v);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    public static string NormalizeTitleId(string? value)
    {
        var s = (value ?? "").Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
        s = s.Replace("-", "", StringComparison.Ordinal);
        return (s.Length == 9 && s.All(char.IsLetterOrDigit)) ? s[..4] + "-" + s[4..] : s;
    }

    private static string CachePath => AppPaths.MetadataFile;
    private void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            var text = File.ReadAllText(CachePath);
            _cache = JsonSerializer.Deserialize<Dictionary<string, MetadataRecord>>(text) ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { _cache = new(StringComparer.OrdinalIgnoreCase); }
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { LogService.Write($"No se pudo guardar caché de metadatos: {ex.Message}", "WARN"); }
    }

    private sealed record MetadataRecord(string Name, string? CoverPath, DateTimeOffset RetrievedUtc);
}
