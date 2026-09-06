using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Services;

/// <summary>
/// Resuelve y cachea carátulas por Title ID. El servicio es compartido por host y módulos,
/// deduplica solicitudes concurrentes y mantiene el trabajo de red/decodificación fuera del hilo de UI.
/// </summary>
public sealed class CoverCacheService : IDisposable
{
    // Fuente remota única de carátulas: PlayStation Store Chihiro.
    // Resuelve directamente Title ID -> imagen, sin catálogo intermedio ni autenticación.
    private const string ChihiroImageBase = "https://store.playstation.com/store/api/chihiro/00_09_000/titlecontainer/";

    // Un Title ID no siempre está publicado en el titlecontainer de todas las regiones
    // (títulos region-locked). Se recorren estas regiones en orden hasta encontrar imagen:
    // gb/en resuelve la mayoría del catálogo y us/en cubre los títulos ausentes de la
    // store británica/española. La región ganadora se memoriza por sesión para que los
    // reintentos sigan costando una única petición.
    private static readonly string[] CoverRegionPaths = { "gb/en", "us/en", "es/es" };
    private static readonly TimeSpan CoverRemoteTimeout = TimeSpan.FromSeconds(5);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _downloadGate = new(4, 4);
    private static readonly TimeSpan CoverStalledThreshold = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CoverQueuedStalledThreshold = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly ConcurrentDictionary<string, CoverTelemetryState> _coverTelemetry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapImage?>>> _decodedImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _negativeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _regionMemo = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(20);
    private int _disposed;

    public CoverCacheService() : this(null) { }

    public CoverCacheService(HttpClient? httpClient)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        _ownsHttp = httpClient is null;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.UserAgent} (+covers)");
        Directory.CreateDirectory(AppPaths.CoversDirectory);
    }

    /// <summary>
    /// Precarga solicitudes sin convertir el lote en una barrera de UI. Cada carátula publica su
    /// resultado al completar; este método únicamente pone en marcha las solicitudes deduplicadas.
    /// </summary>
    public Task WarmAsync(IEnumerable<(string TitleId, string TitleName)> titles, Action<string>? log = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        foreach (var (rawTitleId, titleName) in titles
                     .Select(x => (x.TitleId, x.TitleName))
                     .DistinctBy(x => GameMetadataService.NormalizeTitleId(x.TitleId), StringComparer.OrdinalIgnoreCase))
        {
            var id = GameMetadataService.NormalizeTitleId(rawTitleId);
            if (string.IsNullOrWhiteSpace(id)) continue;

            _ = PrefetchOneAsync(id, titleName, log, ct);
        }

        return Task.CompletedTask;
    }

    public async Task<string?> EnsureCoverAsync(string rawTitleId, string? titleName = null, Action<string>? log = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var id = GameMetadataService.NormalizeTitleId(rawTitleId);
        if (string.IsNullOrWhiteSpace(id)) return null;

        _coverTelemetry.AddOrUpdate(id,
            _ => new CoverTelemetryState(DateTime.Now, DateTime.Now, "QUEUED", null),
            (_, current) => current.State is "COMPLETED" or "FAILED" or "NOT_FOUND"
                ? new CoverTelemetryState(DateTime.Now, DateTime.Now, "QUEUED", null)
                : current);

        var existing = _inflight.GetOrAdd(
            id,
            static (key, state) => new Lazy<Task<string?>>(
                () => state.Service.EnsureCoverCoreAsync(key, state.TitleName, state.Log),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Service: this, TitleName: titleName, Log: log));

        var sharedTask = existing.Value;
        _ = sharedTask.ContinueWith(
            _ => _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(id, existing)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            return await sharedTask.WaitAsync(ct).ConfigureAwait(false);
        }
        finally { }
    }

    private async Task PrefetchOneAsync(string id, string? titleName, Action<string>? log, CancellationToken callerToken)
    {
        try
        {
            await EnsureCoverAsync(id, titleName, log, callerToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested || _lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            log?.Invoke($"Carátula {id}: {ex.Message}");
        }
    }

    private async Task<string?> EnsureCoverCoreAsync(string id, string? titleName, Action<string>? log)
    {
        var serviceToken = _lifetimeCts.Token;
        UpdateCoverTelemetry(id, "RUNNING", null);
        if (_negativeCache.TryGetValue(id, out var negativeUntil) && negativeUntil > DateTimeOffset.UtcNow)
        {
            UpdateCoverTelemetry(id, "NOT_FOUND", "negative-cache");
            return null;
        }
        _negativeCache.TryRemove(id, out _);

        var cached = await FindCachedAsync(id, serviceToken).ConfigureAwait(false);
        if (cached is not null) { UpdateCoverTelemetry(id, "COMPLETED", "cache"); return cached; }

        await _downloadGate.WaitAsync(serviceToken).ConfigureAwait(false);
        UpdateCoverTelemetry(id, "RUNNING", "download-slot");
        try
        {
            cached = await FindCachedAsync(id, serviceToken).ConfigureAwait(false);
            if (cached is not null) { UpdateCoverTelemetry(id, "COMPLETED", "cache"); return cached; }

            UpdateCoverTelemetry(id, "RUNNING", "resolving");
            var titleId = ToStoreTitleId(id);
            if (string.IsNullOrWhiteSpace(titleId))
            {
                _negativeCache[id] = DateTimeOffset.UtcNow + NegativeCacheDuration;
                UpdateCoverTelemetry(id, "NOT_FOUND", "title-id-not-usable");
                return null;
            }

            var regions = _regionMemo.TryGetValue(id, out var knownRegion)
                ? new[] { knownRegion }
                : CoverRegionPaths;

            foreach (var region in regions)
            {
                var url = BuildChihiroImageUrl(region, titleId);

                using var remoteTimeout = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
                remoteTimeout.CancelAfter(CoverRemoteTimeout);
                HttpResponseMessage response;
                try
                {
                    response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, remoteTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (remoteTimeout.IsCancellationRequested && !serviceToken.IsCancellationRequested)
                {
                    // Todas las regiones comparten host (store.playstation.com): un timeout
                    // es casi siempre de red/host y repetir contra la misma máquina solo
                    // añade latencia. Se contabiliza FAILED y no se itera (contrato v6.8.7.47).
                    UpdateCoverTelemetry(id, "FAILED", "remote-timeout");
                    return null;
                }

                using (response)
                {
                    UpdateCoverTelemetry(id, "RUNNING", "response");
                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode is 404 or 410)
                        {
                            if (region != regions[^1])
                            {
                                // Esa región no publica el título; otra región sí puede tenerlo,
                                // así que NOT_FOUND solo se declara tras agotar las regiones.
                                UpdateCoverTelemetry(id, "RUNNING", $"region-{region}-missing");
                                continue;
                            }

                            _negativeCache[id] = DateTimeOffset.UtcNow + NegativeCacheDuration;
                            UpdateCoverTelemetry(id, "NOT_FOUND", $"ps-store-http-{(int)response.StatusCode}");
                            return null;
                        }

                        UpdateCoverTelemetry(id, "FAILED", $"http-{(int)response.StatusCode}");
                        return null;
                    }

                    var mediaType = response.Content.Headers.ContentType?.MediaType;
                    if (string.IsNullOrWhiteSpace(mediaType) || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateCoverTelemetry(id, "FAILED", "non-image-response");
                        return null;
                    }

                    byte[] bytes;
                    try
                    {
                        bytes = await response.Content.ReadAsByteArrayAsync(remoteTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (remoteTimeout.IsCancellationRequested && !serviceToken.IsCancellationRequested)
                    {
                        UpdateCoverTelemetry(id, "FAILED", "remote-timeout");
                        return null;
                    }

                    UpdateCoverTelemetry(id, "RUNNING", "downloaded");
                    if (bytes.Length == 0)
                    {
                        UpdateCoverTelemetry(id, "FAILED", "empty-response");
                        return null;
                    }

                    var ext = GuessExtension(url, response.Content.Headers.ContentType?.MediaType);
                    var path = Path.Combine(AppPaths.CoversDirectory, id.Replace('-', '_') + ext);
                    var temp = path + ".download";

                    try
                    {
                        await File.WriteAllBytesAsync(temp, bytes, serviceToken).ConfigureAwait(false);
                        var validImage = await Task.Run(() => LoadImage(temp) is not null, serviceToken).ConfigureAwait(false);
                        if (!validImage)
                        {
                            TryDelete(temp);
                            UpdateCoverTelemetry(id, "FAILED", "invalid-image");
                            return null;
                        }

                        serviceToken.ThrowIfCancellationRequested();
                        File.Move(temp, path, true);
                        _decodedImages.TryRemove(path, out _);
                        _negativeCache.TryRemove(id, out _);
                        _regionMemo[id] = region;
                        UpdateCoverTelemetry(id, "COMPLETED", $"downloaded:{region}");
                        return path;
                    }
                    catch
                    {
                        TryDelete(temp);
                        throw;
                    }
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            // La cancelación del servicio o del trabajo del llamador sí se propaga;
            // los timeouts de red ya se convierten en FAILED/remote-timeout arriba.
            UpdateCoverTelemetry(id, "CANCELLED", "cancelled");
            throw;
        }
        catch (HttpRequestException ex)
        {
            UpdateCoverTelemetry(id, "FAILED", ex.Message);
            return null;
        }
        catch (IOException ex)
        {
            UpdateCoverTelemetry(id, "FAILED", ex.Message);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            UpdateCoverTelemetry(id, "FAILED", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            UpdateCoverTelemetry(id, "FAILED", ex.Message);
            throw;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private static string BuildChihiroImageUrl(string region, string titleId)
        => $"{ChihiroImageBase}{region}/999/{Uri.EscapeDataString(titleId)}/image?w=800&h=800";

    private static string ToStoreTitleId(string id)
    {
        var compact = id.Replace("-", "", StringComparison.Ordinal);
        if (compact.Length == 9 &&
            (compact.StartsWith("PPSA", StringComparison.OrdinalIgnoreCase) || compact.StartsWith("CUSA", StringComparison.OrdinalIgnoreCase)))
            return compact + "_00";

        return compact;
    }

    private static async Task<string?> FindCachedAsync(string id, CancellationToken ct)
    {
        return await Task.Run(() => FindCached(id), ct).ConfigureAwait(false);
    }

    private static string? FindCached(string id)
    {
        var stem = id.Replace('-', '_');
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var path = Path.Combine(AppPaths.CoversDirectory, stem + ext);
            if (!File.Exists(path)) continue;

            try
            {
                // No decodificar aquí: LoadImageAsync comparte y limita la decodificación.
                // Validar dos veces cada cache hit era una fuente importante de trabajo en UI.
                if (new FileInfo(path).Length <= 0)
                {
                    TryDelete(path);
                    continue;
                }
                return path;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Devuelve una imagen decodificada y congelada, compartiendo la misma tarea entre
    /// consumidores que apuntan al mismo fichero. Así un título con muchos slots no
    /// vuelve a decodificar la misma carátula decenas de veces.
    /// </summary>
    public async Task<BitmapImage?> LoadImageAsync(string? path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(path)) return null;

        var fullPath = Path.GetFullPath(path);
        var shared = _decodedImages.GetOrAdd(
            fullPath,
            key => new Lazy<Task<BitmapImage?>>(() => LoadImageCoreAsync(key), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await shared.Value.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (shared.IsValueCreated && shared.Value.IsCompleted)
                _decodedImages.TryRemove(new KeyValuePair<string, Lazy<Task<BitmapImage?>>>(fullPath, shared));
            throw;
        }
    }

    private async Task<BitmapImage?> LoadImageCoreAsync(string path)
    {
        await _decodeGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
        try
        {
            var image = await Task.Run(() => LoadImage(path), _lifetimeCts.Token).ConfigureAwait(false);
            if (image is null) TryDelete(path);
            return image;
        }
        finally
        {
            _decodeGate.Release();
        }
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
        catch
        {
            return null;
        }
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

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(CoverCacheService));
    }

    public CoverTelemetrySnapshot GetTelemetrySnapshot()
    {
        var now = DateTime.Now;
        var known = _coverTelemetry.Values.ToArray();
        var queued = known.Count(x => x.State == "QUEUED");
        var running = known.Count(x => x.State == "RUNNING");
        var completed = known.Count(x => x.State == "COMPLETED");
        var notFound = known.Count(x => x.State == "NOT_FOUND");
        var failed = known.Count(x => x.State == "FAILED");
        var stalledRunning = known.Count(x => x.State == "RUNNING" && now - x.LastActivityLocal > CoverStalledThreshold);
        var stalledQueued = known.Count(x => x.State == "QUEUED" && now - x.LastActivityLocal > CoverQueuedStalledThreshold);
        var stalled = stalledRunning + stalledQueued;
        return new CoverTelemetrySnapshot(known.Length, queued, running, completed, notFound, failed, stalled, stalledQueued);
    }

    private void UpdateCoverTelemetry(string id, string state, string? message)
    {
        _coverTelemetry.AddOrUpdate(id,
            _ => new CoverTelemetryState(DateTime.Now, DateTime.Now, state, message),
            (_, current) => current with { State = state, LastActivityLocal = DateTime.Now, Message = message });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _lifetimeCts.Cancel();

        var tasks = _inflight.Values
            .Where(x => x.IsValueCreated)
            .Select(x => (Task)x.Value)
            .Concat(_decodedImages.Values.Where(x => x.IsValueCreated).Select(x => (Task)x.Value))
            .Distinct()
            .ToArray();

        if (tasks.Length > 0)
        {
            try { Task.WhenAll(tasks).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch { }
        }

        if (_ownsHttp) _http.Dispose();
        _downloadGate.Dispose();
        _decodeGate.Dispose();
        _lifetimeCts.Dispose();
        _inflight.Clear();
        _decodedImages.Clear();
        _coverTelemetry.Clear();
        _negativeCache.Clear();
        _regionMemo.Clear();
    }
}

public sealed record CoverTelemetrySnapshot(int Known, int Queued, int Running, int Completed, int NotFound, int Failed, int Stalled, int StalledQueued);
internal sealed record CoverTelemetryState(DateTime StartedLocal, DateTime LastActivityLocal, string State, string? Message);
