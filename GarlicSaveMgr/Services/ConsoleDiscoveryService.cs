using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Services;

public sealed record ConsoleDiscoveryResult(string Ip, int Port, bool GarlicApiAvailable, bool ElfLdrAvailable, TimeSpan Elapsed);

public sealed class ConsoleDiscoveryService
{
    public const int DefaultPort = 8082;
    public const int ElfLdrPort = 9021;
    public static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan HttpProbeTimeout = TimeSpan.FromMilliseconds(500);
    public const int BatchSize = 255; // Historical/compatibility constant; discovery is now one 1,275-target stage.
    public const int ScanFirstThirdOctet = 0;
    public const int ScanLastThirdOctet = 4;

    public async Task<IReadOnlyList<ConsoleDiscoveryResult>> DiscoverAllAsync(
        int port = DefaultPort,
        IProgress<(string Ip, int Checked, int Total)>? progress = null,
        Action<string>? log = null,
        Func<ConsoleDiscoveryResult, Task>? onConsoleFoundAsync = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var telemetryId = ApplicationTelemetryService.StartOperation("LAN_SCAN", "Autodetección LAN de 1.275 direcciones");
        var found = new List<ConsoleDiscoveryResult>();
        var totalAddresses = (ScanLastThirdOctet - ScanFirstThirdOctet + 1) * 255;

        var scanIps = (from third in Enumerable.Range(ScanFirstThirdOctet, ScanLastThirdOctet - ScanFirstThirdOctet + 1)
                       from fourth in Enumerable.Range(1, 255)
                       select $"192.168.{third}.{fourth}").ToArray();

        log?.Invoke($"Autodetección: una sola etapa de {scanIps.Length} direcciones (192.168.{ScanFirstThirdOctet}.1 → 192.168.{ScanLastThirdOctet}.255).");

        try
        {
            // Los 1.275 pings se programan en una única etapa y arrancan sin
            // esperar bloques de 255. El límite temporal del propio ping evita
            // que un host silencioso bloquee el conjunto.
            var pingTasks = scanIps.Select((ip, index) =>
                RunPingAsync(ip, index, totalAddresses, progress, ct)).ToArray();

            ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", 0.25, "ICMP");
            var pingResults = await Task.WhenAll(pingTasks).ConfigureAwait(false);
            var pingOk = pingResults.Where(x => x.Success).OrderBy(x => x.Offset).ToList();
            log?.Invoke($"Escaneo ICMP completado: {pingOk.Count}/{scanIps.Length} respondieron.");

            // La fase de Garlic/elfldr sigue limitada: aquí sí interesa proteger
            // la pila de red porque solo se probarán los hosts que respondieron.
            using var probeGate = new SemaphoreSlim(4, 4);
            var probeTasks = pingOk.Select(async result =>
            {
                await probeGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    log?.Invoke($"Ping OK: {result.Ip} → probando Garlic :{port} y, si procede, elfldr :{ElfLdrPort}.");
                    var probe = await FindGarlicPortAsync(result.Ip, port, ct).ConfigureAwait(false);
                    return probe is null
                        ? null
                        : new ConsoleDiscoveryResult(result.Ip, probe.Value.Port, probe.Value.GarlicApiAvailable, probe.Value.ElfLdrAvailable, sw.Elapsed);
                }
                finally { probeGate.Release(); }
            }).ToArray();

            ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", 0.75, "Garlic/elfldr probes");
            var candidates = await Task.WhenAll(probeTasks).ConfigureAwait(false);
            foreach (var candidate in candidates.Where(c => c is not null).Cast<ConsoleDiscoveryResult>())
            {
                if (found.Any(x => string.Equals(x.Ip, candidate.Ip, StringComparison.OrdinalIgnoreCase) && x.Port == candidate.Port))
                    continue;

                found.Add(candidate);
                log?.Invoke(candidate.GarlicApiAvailable
                    ? $"Consola encontrada con Garlic API en {candidate.Ip}:{candidate.Port}."
                    : $"Consola encontrada con elfldr en {candidate.Ip}; Garlic aún no está activo.");
                if (onConsoleFoundAsync is not null)
                    await onConsoleFoundAsync(candidate).ConfigureAwait(false);
            }

            sw.Stop();
            ApplicationTelemetryService.CompleteOperation(telemetryId, true);
            return found;
        }
        catch (OperationCanceledException)
        {
            ApplicationTelemetryService.CompleteOperation(telemetryId, false, true);
            throw;
        }
        catch (Exception ex)
        {
            ApplicationTelemetryService.RecordException("ConsoleDiscoveryService", ex);
            ApplicationTelemetryService.CompleteOperation(telemetryId, false, false, ex.Message);
            throw;
        }
        finally
        {
            sw.Stop();
        }
    }

    public async Task<ConsoleDiscoveryResult?> DiscoverAsync(
        int port = DefaultPort,
        IProgress<(string Ip, int Checked, int Total)>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
        => (await DiscoverAllAsync(port, progress, log, null, ct).ConfigureAwait(false)).FirstOrDefault();

    private static async Task<PingProbeResult> RunPingAsync(
        string ip,
        int offset,
        int totalAddresses,
        IProgress<(string Ip, int Checked, int Total)>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var ping = new System.Net.NetworkInformation.Ping();
        try
        {
            var reply = await ping.SendPingAsync(ip, (int)PingTimeout.TotalMilliseconds).ConfigureAwait(false);
            return new PingProbeResult(offset, ip, reply.Status == System.Net.NetworkInformation.IPStatus.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new PingProbeResult(offset, ip, false);
        }
        finally
        {
            progress?.Report((ip, offset + 1, totalAddresses));
        }
    }

    private static async Task<(int Port, bool GarlicApiAvailable, bool ElfLdrAvailable)?> FindGarlicPortAsync(string ip, int preferredPort, CancellationToken ct)
    {
        if (await ProbeGarlicAsync(ip, preferredPort, ct).ConfigureAwait(false))
            return (preferredPort, true, await ProbeTcpPortAsync(ip, ElfLdrPort, ct).ConfigureAwait(false));
        if (preferredPort != ElfLdrPort && await ProbeTcpPortAsync(ip, ElfLdrPort, ct).ConfigureAwait(false))
            return (preferredPort, false, true);
        return null;
    }

    private static async Task<bool> ProbeGarlicAsync(string ip, int port, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        { ConnectTimeout = HttpProbeTimeout, PooledConnectionLifetime = TimeSpan.FromSeconds(10) };
        using var http = new HttpClient(handler) { Timeout = HttpProbeTimeout };
        try
        {
            using var response = await http.GetAsync($"http://{ip}:{port}/api/status", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mediaType, "text/json", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        catch { return false; }
    }

    private static async Task<bool> ProbeTcpPortAsync(string ip, int port, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HttpProbeTimeout);
        try { await tcp.ConnectAsync(ip, port, timeoutCts.Token).ConfigureAwait(false); return tcp.Connected; }
        catch { return false; }
    }

    private sealed record PingProbeResult(int Offset, string Ip, bool Success);
}
