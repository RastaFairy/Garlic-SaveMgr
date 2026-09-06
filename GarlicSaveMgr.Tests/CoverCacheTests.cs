using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Services;
using Xunit;

namespace GarlicSaveMgr.Tests;

public sealed class CoverCacheTests
{
    [Fact]
    public async Task WarmAsync_DoesNotWaitForWholeBatch()
    {
        using var handler = new PlayStationStoreHandler(TimeSpan.FromSeconds(2));
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        var started = DateTime.UtcNow;
        await covers.WarmAsync(new[] { ("PPSA-99991", "Prueba") });
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(500), $"WarmAsync tardó {elapsed.TotalMilliseconds:0} ms.");
        await handler.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task EnsureCoverAsync_DeduplicatesConcurrentRequestsByTitleId()
    {
        using var handler = new PlayStationStoreHandler(TimeSpan.FromMilliseconds(50));
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        var calls = Enumerable.Range(0, 8).Select(_ => covers.EnsureCoverAsync("PPSA-99992", "Prueba")).ToArray();
        var results = await Task.WhenAll(calls);

        Assert.All(results, path => Assert.Null(path));
        // Sin carátula en ninguna región: una única tarea compartida recorre las
        // regiones candidatas una sola vez pese a las 8 solicitudes concurrentes.
        Assert.Equal(CoverRegionPathCount, handler.StoreImageCalls);
    }

    [Fact]
    public async Task Dispose_CancelsInflightCoverWorkBeforeDisposingResources()
    {
        using var handler = new PlayStationStoreHandler(Timeout.InfiniteTimeSpan);
        using var http = new HttpClient(handler);
        var covers = new CoverCacheService(http);

        var request = covers.EnsureCoverAsync("PPSA-99993", "Prueba");
        await handler.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        covers.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
    }

    [Fact]
    public async Task CoverResolution_UsesOnlyOfficialPlayStationStoreChihiroByTitleId()
    {
        using var handler = new PlayStationStoreHandler(TimeSpan.FromMilliseconds(20));
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        await Task.WhenAll(
            covers.EnsureCoverAsync("PPSA-99994", "Prueba 1"),
            covers.EnsureCoverAsync("PPSA-99995", "Prueba 2"),
            covers.EnsureCoverAsync("PPSA-99996", "Prueba 3"));

        // Fuente única Chihiro para todos los títulos; al no haber carátula en
        // ninguna región, cada título recorre exactamente las regiones candidatas.
        Assert.Equal(3 * CoverRegionPathCount, handler.StoreImageCalls);
        Assert.True(handler.AllStoreImagePathsUseSuffix00);
        Assert.Equal(0, handler.LegacyCalls);
    }

    [Fact]
    public async Task CoverResolution_FallsBackToNextRegionWhenFirstRegionLacksTitle()
    {
        DeleteCachedCovers("PPSA99981");

        using var handler = new PlayStationStoreHandler(TimeSpan.FromMilliseconds(10)) { DirectCover = true };
        handler.MissingRegions.Add("gb/en");
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        var path = await covers.EnsureCoverAsync("PPSA-99981", "Prueba región");

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(2, handler.StoreImageCalls);
        Assert.Equal(new[] { "gb/en", "us/en" }, handler.RequestedRegions);
    }

    [Fact]
    public async Task CoverResolution_MemoizesWinningRegionOnRetry()
    {
        DeleteCachedCovers("PPSA99982");

        using var handler = new PlayStationStoreHandler(TimeSpan.FromMilliseconds(10)) { DirectCover = true };
        handler.MissingRegions.Add("gb/en");
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        var first = await covers.EnsureCoverAsync("PPSA-99982", "Prueba memo");
        Assert.NotNull(first);

        // Eliminar la caché local obliga a reintentar en remoto; la región ganadora
        // memorizada evita repetir las regiones que ya se saben sin carátula.
        File.Delete(first!);
        var second = await covers.EnsureCoverAsync("PPSA-99982", "Prueba memo");

        Assert.NotNull(second);
        Assert.Equal(3, handler.StoreImageCalls);
        Assert.DoesNotContain("es/es", handler.RequestedRegions);
    }

    [Fact]
    public async Task CoverResolution_ReportsNotFoundOnlyAfterExhaustingRegions()
    {
        DeleteCachedCovers("PPSA99983");

        using var handler = new PlayStationStoreHandler(TimeSpan.FromMilliseconds(10));
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        var result = await covers.EnsureCoverAsync("PPSA-99983", "Prueba agotada");
        var telemetry = covers.GetTelemetrySnapshot();

        Assert.Null(result);
        Assert.Equal(CoverRegionPathCount, handler.StoreImageCalls);
        Assert.Equal(0, telemetry.Completed);
        Assert.Equal(1, telemetry.NotFound);
        Assert.Equal(0, telemetry.Failed);
    }

    [Fact]
    public async Task EnsureCoverAsync_BoundsRemoteResolutionWhenStoreImageHangs()
    {
        using var handler = new PlayStationStoreHandler(Timeout.InfiniteTimeSpan) { FailImageWithTimeout = true };
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        var started = DateTime.UtcNow;
        var result = await covers.EnsureCoverAsync("PPSA-99997", "Prueba");
        var elapsed = DateTime.UtcNow - started;

        Assert.Null(result);
        Assert.True(elapsed < TimeSpan.FromSeconds(6), $"La resolución remota tardó {elapsed.TotalSeconds:0.00} s.");
        // Un timeout es transitorio: no se itera el resto de regiones.
        Assert.Equal(1, handler.StoreImageCalls);
    }

    [Fact]
    public async Task Telemetry_KeepsNotFoundSeparateFromCompleted()
    {
        using var handler = new PlayStationStoreHandler(TimeSpan.FromMilliseconds(10));
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        var result = await covers.EnsureCoverAsync("PPSA-99998", "Prueba");
        var telemetry = covers.GetTelemetrySnapshot();

        Assert.Null(result);
        Assert.Equal(0, telemetry.Completed);
        Assert.Equal(1, telemetry.NotFound);
        Assert.Equal(0, telemetry.Failed);
    }

    [Fact]
    public async Task OfficialStoreChihiro_ResolvesTitleIdToOfficialCover()
    {
        DeleteCachedCovers("PPSA99999");

        using var handler = new PlayStationStoreHandler(TimeSpan.FromMilliseconds(10)) { DirectCover = true };
        using var http = new HttpClient(handler);
        using var covers = new CoverCacheService(http);

        var path = await covers.EnsureCoverAsync("PPSA-99999", "Prueba Store");

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(1, handler.StoreImageCalls);
    }

    // Número de regiones candidatas del pipeline Chihiro (gb/en, us/en, es/es).
    // Es un contrato de regresión: si cambia la lista de regiones, estos tests deben revisarse.
    private const int CoverRegionPathCount = 3;

    private static void DeleteCachedCovers(string stem)
    {
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var cached = Path.Combine(AppPaths.CoversDirectory, stem + ext);
            try { if (File.Exists(cached)) File.Delete(cached); } catch { }
        }
    }

    private sealed class PlayStationStoreHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        public int StoreImageCalls;
        public int LegacyCalls;
        public bool DirectCover { get; set; }
        public bool FailImageWithTimeout { get; set; }
        public bool AllStoreImagePathsUseSuffix00 { get; private set; } = true;
        public HashSet<string> MissingRegions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RequestedRegions { get; } = new();
        public TaskCompletionSource FirstRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlayStationStoreHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;

            if (uri.Contains("store.playstation.com/store/api/chihiro/00_09_000/titlecontainer/", StringComparison.OrdinalIgnoreCase) &&
                uri.Contains("/image", StringComparison.OrdinalIgnoreCase))
            {
                var region = ExtractRegion(uri);
                if (region is not null) RequestedRegions.Add(region);
                Interlocked.Increment(ref StoreImageCalls);
                FirstRequestStarted.TrySetResult();
                AllStoreImagePathsUseSuffix00 &= uri.Contains("_00/image", StringComparison.OrdinalIgnoreCase);
                if (FailImageWithTimeout)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                await Task.Delay(_delay, cancellationToken);

                if (!DirectCover || (region is not null && MissingRegions.Contains(region)))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="))
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                return response;
            }

            if (uri.Contains("api.serialstation.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("kytyps5.github.io", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("m.np.playstation.net/api/catalog/v2", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("PS5_Titles.tsv", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("prosperopatches.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("web.np.playstation.com/api/graphql/v1/op", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref LegacyCalls);
                throw new InvalidOperationException("Se ha consultado una fuente remota no permitida: " + uri);
            }

            throw new InvalidOperationException($"URL de prueba inesperada: {uri}");
        }

        private static string? ExtractRegion(string uri)
        {
            const string marker = "/titlecontainer/";
            var start = uri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            var rest = uri[(start + marker.Length)..];
            var parts = rest.Split('/');
            return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : null;
        }
    }
}
