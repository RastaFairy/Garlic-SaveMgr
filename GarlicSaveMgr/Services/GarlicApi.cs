using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

public sealed class GarlicApi : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _base;

    public string Ip { get; }
    public int Port { get; }

    public GarlicApi(string ip, int port = 8082)
    {
        Ip = ip;
        Port = port;
        _base = $"http://{ip}:{port}/api";
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<bool> PingAsync(CancellationToken ct = default, TimeSpan? timeout = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout ?? TimeSpan.FromMilliseconds(450));
        try
        {
            using var r = await _http.GetAsync($"{_base}/status", HttpCompletionOption.ResponseHeadersRead, linked.Token);
            return r.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> QueryVersionAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // Garlic v1.13 exposes its build version in the served HTML, inside
            // the <nav>...</nav> block. This is the authoritative live-version
            // source when /api/status does not expose a version field.
            using var htmlResponse = await _http.GetAsync($"http://{Ip}:{Port}/", HttpCompletionOption.ResponseContentRead, linked.Token);
            if (htmlResponse.IsSuccessStatusCode)
            {
                var html = await htmlResponse.Content.ReadAsStringAsync(linked.Token);
                var navMatch = Regex.Match(html, @"<nav\b[^>]*>(.*?)</nav>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (navMatch.Success)
                {
                    var versionMatch = Regex.Match(
                        navMatch.Groups[1].Value,
                        @"<span\b[^>]*>\s*(v\d+(?:\.\d+){1,3})\s*</span>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (versionMatch.Success)
                        return versionMatch.Groups[1].Value.Trim();
                }
            }

            // Backwards-compatible fallbacks for Garlic builds that expose the
            // version through /api/status or the HTTP Server header.
            using var r = await _http.GetAsync($"{_base}/status", HttpCompletionOption.ResponseContentRead, linked.Token);
            if (!r.IsSuccessStatusCode) return null;
            try
            {
                await using var stream = await r.Content.ReadAsStreamAsync(linked.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: linked.Token);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var candidate in new[] { "version", "garlic_version", "ver", "v" })
                    {
                        if (root.TryGetProperty(candidate, out var vProp) && vProp.ValueKind == JsonValueKind.String)
                        {
                            var v = vProp.GetString()?.Trim();
                            if (!string.IsNullOrEmpty(v)) return v;
                        }
                    }
                }
            }
            catch (JsonException) { }

            if (r.Headers.TryGetValues("Server", out var serverValues))
            {
                var server = string.Join(" ", serverValues).Trim();
                var slash = server.IndexOf('/');
                if (slash >= 0 && slash < server.Length - 1)
                {
                    var ver = server[(slash + 1)..].Trim();
                    if (!string.IsNullOrEmpty(ver)) return ver;
                }
                if (server.StartsWith("garlic", StringComparison.OrdinalIgnoreCase) ||
                    server.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    return server;
            }
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public async Task<JsonElement> StatusAsync(CancellationToken ct = default)
        => await GetJsonAsync("/status", 10, ct);

    public async Task<JsonElement> MountAsync(int idx, CancellationToken ct = default)
        => await GetJsonAsync($"/mount?idx={idx}", 60, ct);

    public async Task<JsonElement> RegenerateSfoAsync(string titleId, string dirName, CancellationToken ct = default)
    {
        var q = $"/regen_sfo?title_id={Uri.EscapeDataString(titleId)}&dir_name={Uri.EscapeDataString(dirName)}";
        return await GetJsonAsync(q, 30, ct);
    }

    public async Task<bool> UnmountAsync(CancellationToken ct = default)
    {
        var root = await GetJsonAsync("/unmount", 30, ct);
        return root.ValueKind == JsonValueKind.Object && (!root.TryGetProperty("ok", out var ok) || ok.ValueKind == JsonValueKind.True);
    }

    public async Task<JsonElement> GetJsonAsync(string path, int timeoutSeconds = 15, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var r = await _http.GetAsync(_base + path, linked.Token);
            if (!r.IsSuccessStatusCode) throw new GarlicException($"HTTP {(int)r.StatusCode} {r.ReasonPhrase}");
            await using var s = await r.Content.ReadAsStreamAsync(linked.Token);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: linked.Token);
            return doc.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new GarlicException("Tiempo de espera agotado"); }
        catch (JsonException ex) { throw new GarlicException("La consola devolvió JSON no válido", ex); }
        catch (HttpRequestException ex) { throw new GarlicException(ex.Message, ex); }
    }

    public async Task<long> DownloadRawAsync(int idx, string destinationPath, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        try
        {
            using var r = await _http.GetAsync($"{_base}/download_raw?idx={Uri.EscapeDataString(idx.ToString())}", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!r.IsSuccessStatusCode) throw new GarlicException($"HTTP {(int)r.StatusCode} {r.ReasonPhrase}");

            var total = r.Content.Headers.ContentLength ?? 0;
            var destination = Path.GetFullPath(destinationPath);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            // La versión Python escribe la respuesta directamente a disco mediante
            // requests.iter_content(). Hacemos lo mismo aquí: no acumulamos todo el
            // save en un byte[]/MemoryStream, evitando el límite práctico de ~2 GB.
            await using var input = await r.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true);

            var buffer = new byte[1024 * 1024];
            long done = 0;
            int n;
            while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                progress?.Report((done, total));
            }

            await output.FlushAsync(ct);
            return done;
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex) { throw new GarlicException(ex.Message, ex); }
        catch (IOException ex) { throw new GarlicException(ex.Message, ex); }
    }

    public async Task<JsonElement> PostFileAsync(string filePath, string uid, string? extraQuery, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        var full = Path.GetFullPath(filePath);
        await using var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        using var content = new StreamContent(new ProgressReadStream(file, progress));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = file.Length;
        var query = $"uid={Uri.EscapeDataString(uid)}" + (string.IsNullOrWhiteSpace(extraQuery) ? "" : "&" + extraQuery);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            using var r = await _http.PostAsync($"{_base}/import_encrypted?{query}", content, linked.Token);
            if (!r.IsSuccessStatusCode) throw new GarlicException($"HTTP {(int)r.StatusCode} {r.ReasonPhrase}");
            await using var stream = await r.Content.ReadAsStreamAsync(linked.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: linked.Token);
            return doc.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new GarlicException("Tiempo de espera agotado durante la subida"); }
        catch (JsonException ex) { throw new GarlicException("La consola devolvió JSON no válido", ex); }
        catch (HttpRequestException ex) { throw new GarlicException(ex.Message, ex); }
    }

    public async Task<JsonElement> ImportFinishAsync(string uid, CancellationToken ct)
        => await GetJsonAsync($"/import_finish?uid={Uri.EscapeDataString(uid)}", 30, ct);

    public async Task<JsonElement> DeleteAsync(int idx, CancellationToken ct)
    {
        try { return await GetJsonAsync($"/delete_save?idx={idx}", 30, ct); }
        catch (GarlicException ex)
        {
            LogService.Write($"delete idx={idx}: {ex.Message}", "ERROR");
            throw;
        }
    }

    public async Task<List<JsonElement>> AccountIdsAsync(CancellationToken ct = default)
    {
        try
        {
            var root = await GetJsonAsync("/account_ids", 15, ct);
            return ExtractArray(root, "account_ids", "accounts", "ids", "perfiles", "users");
        }
        catch (GarlicException) { return []; }
    }

    public async Task<List<JsonElement>> UsersAsync(CancellationToken ct = default)
    {
        var root = await GetJsonAsync("/users", 15, ct);
        return ExtractArray(root, "users");
    }

    public async Task<List<JsonElement>> SavesAsync(CancellationToken ct = default)
    {
        var root = await GetJsonAsync("/saves", 30, ct);
        return ExtractArray(root, "saves");
    }

    public async Task<List<JsonElement>> ScanTitlesAsync(string uid, CancellationToken ct = default)
    {
        try
        {
            var q = string.IsNullOrWhiteSpace(uid) ? "" : $"?uid={Uri.EscapeDataString(uid)}";
            var root = await GetJsonAsync($"/scan_titles{q}", 30, ct);
            return ExtractArray(root, "titles");
        }
        catch (GarlicException) { return GroupSaves(await SavesAsync(ct), uid); }
    }

    private static List<JsonElement> ExtractArray(JsonElement root, params string[] names)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().Select(x => x.Clone()).ToList();
        if (root.ValueKind != JsonValueKind.Object) return [];
        foreach (var name in names)
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
                return v.EnumerateArray().Select(x => x.Clone()).ToList();
        return [];
    }

    private static List<JsonElement> GroupSaves(List<JsonElement> saves, string uid)
    {
        var groups = new Dictionary<string, JsonObjectCompat>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in saves)
        {
            if (!IsPs5(s)) continue;
            var suid = GetString(s, "uid");
            if (!string.IsNullOrEmpty(uid) && !Norm(suid).Equals(Norm(uid), StringComparison.Ordinal)) continue;
            var titleId = GetString(s, "title_id");
            var key = $"{titleId}|{suid}";
            if (!groups.TryGetValue(key, out var g))
            {
                g = new JsonObjectCompat(titleId, suid, GetString(s, "title_name"));
                groups[key] = g;
            }
            g.SlotCount++;
            if (GetBool(s, "backup")) g.BackupCount++;
            g.Slots.Add(new SlotInfo { Name = GetString(s, "save_name"), Backup = GetBool(s, "backup") });
        }
        return groups.Values.Select(g => g.ToJsonElement()).ToList();
    }

    public static string GetString(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";

    public static bool GetBool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static bool IsPs5(JsonElement e) => !string.Equals(GetString(e, "type"), "ps4", StringComparison.OrdinalIgnoreCase);
    public static string Norm(object? value)
    {
        var s = Convert.ToString(value)?.Trim().ToLowerInvariant() ?? "";
        if (s.StartsWith("0x", StringComparison.Ordinal)) s = s[2..];
        // Deliberately preserve leading zeroes. They may be significant in PS5 IDs.
        return s;
    }

    public static Dictionary<string, object?> Owner(JsonElement entry)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "uid", "id", "account_id", "aid" })
            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(key, out var v) && v.ValueKind is not JsonValueKind.Null && v.ToString() != "")
                d[key] = v.ToString();
        return d;
    }

    public static bool ProfileMatches(Dictionary<string, object?> owner, JsonElement profile)
    {
        var p = Canonical(owner); var f = Canonical(Owner(profile));
        var common = new[] { "uid", "account_id" }.Where(k => p.TryGetValue(k, out var a) && a is not null && a.ToString() != "" && f.TryGetValue(k, out var b) && b is not null && b.ToString() != "").ToList();
        if (common.Count == 0) return false;
        return common.All(k => Norm(p[k]) == Norm(f[k]));
    }

    public static string ProfileImportValue(JsonElement profile)
    {
        return new[] { "uid", "id", "account_id", "aid" }
            .Select(k => GetString(profile, k))
            .FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
    }

    private static Dictionary<string, object?> Canonical(Dictionary<string, object?> d)
    {
        var o = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (d.TryGetValue("uid", out var uid)) o["uid"] = uid;
        if (d.TryGetValue("id", out uid)) o["uid"] = uid;
        if (d.TryGetValue("account_id", out var aid)) o["account_id"] = aid;
        if (d.TryGetValue("aid", out aid)) o["account_id"] = aid;
        return o;
    }

    public void Dispose() => _http.Dispose();

    private sealed class JsonObjectCompat
    {
        public string TitleId { get; }
        public string Uid { get; }
        public string TitleName { get; }
        public int SlotCount { get; set; }
        public int BackupCount { get; set; }
        public List<SlotInfo> Slots { get; } = [];
        public JsonObjectCompat(string titleId, string uid, string titleName) { TitleId = titleId; Uid = uid; TitleName = titleName; }
        public JsonElement ToJsonElement()
        {
            var json = JsonSerializer.Serialize(new { title_id = TitleId, uid = Uid, title_name = TitleName, slot_count = SlotCount, backup_count = BackupCount, slots = Slots.Select(s => new { name = s.Name, backup = s.Backup }) });
            using var d = JsonDocument.Parse(json); return d.RootElement.Clone();
        }
    }

    private sealed class ProgressReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly IProgress<(long Done, long Total)>? _progress;
        private long _done;
        public ProgressReadStream(Stream inner, IProgress<(long Done, long Total)>? progress)
        { _inner = inner; _progress = progress; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        { var n = _inner.Read(buffer, offset, count); Report(n); return n; }
        public override int Read(Span<byte> buffer)
        { var n = _inner.Read(buffer); Report(n); return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var n = await _inner.ReadAsync(buffer, cancellationToken); Report(n); return n; }
        private void Report(int n) { if (n > 0) { _done += n; _progress?.Report((_done, _inner.Length)); } }
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
