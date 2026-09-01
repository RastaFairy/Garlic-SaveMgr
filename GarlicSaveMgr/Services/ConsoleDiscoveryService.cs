using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Services;

public sealed record ConsoleDiscoveryResult(string Ip, int Port, TimeSpan Elapsed);

public sealed class ConsoleDiscoveryService
{
    public const int DefaultPort = 8082;
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan WideProbeTimeout = TimeSpan.FromMilliseconds(250);
    public const int MaxConcurrency = 32;
    private const int WideMaxConcurrency = 128;

    public async Task<ConsoleDiscoveryResult?> DiscoverAsync(
        int port = DefaultPort,
        IProgress<(string Ip, int Checked, int Total)>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var networks = GetActiveIpv4Networks();
        if (networks.Count == 0)
        {
            log?.Invoke("No se encontraron interfaces IPv4 locales utilizables.");
            return null;
        }

        foreach (var network in networks)
            log?.Invoke($"Interfaz IPv4: {network.Address} / {network.Mask}");

        var quickCandidates = ConsoleDiscoveryPlanner.BuildQuickCandidates(networks);
        if (quickCandidates.Count == 0)
            return null;

        var sw = Stopwatch.StartNew();
        using var http = CreateProbeClient();

        log?.Invoke($"Buscando Garlic en {quickCandidates.Count} direcciones de las redes locales...");
        var quickResult = await ProbeCandidatesAsync(
            http,
            quickCandidates,
            port,
            progress,
            log,
            alreadyChecked: 0,
            total: quickCandidates.Count,
            ProbeTimeout,
            MaxConcurrency,
            ct);
        if (quickResult is not null)
        {
            sw.Stop();
            return new ConsoleDiscoveryResult(quickResult, port, sw.Elapsed);
        }

        var wideCandidates = ConsoleDiscoveryPlanner.BuildWideCandidates(networks, quickCandidates);
        if (wideCandidates.Count > 0)
        {
            log?.Invoke($"No se encontró Garlic en las redes locales. Ampliando a {wideCandidates.Count} direcciones de 192.168.0.0/16 sin omitir hosts...");
            var wideResult = await ProbeCandidatesAsync(
                http,
                wideCandidates,
                port,
                progress,
                log,
                alreadyChecked: quickCandidates.Count,
                total: quickCandidates.Count + wideCandidates.Count,
                WideProbeTimeout,
                WideMaxConcurrency,
                ct);
            if (wideResult is not null)
            {
                sw.Stop();
                return new ConsoleDiscoveryResult(wideResult, port, sw.Elapsed);
            }
        }

        var expandedCandidates = ConsoleDiscoveryPlanner.BuildExpandedCandidates(networks, quickCandidates);
        if (expandedCandidates.Count > 0)
        {
            log?.Invoke($"No se encontró Garlic en la búsqueda prioritaria. Ampliando a {expandedCandidates.Count} direcciones adicionales dentro de las subredes conectadas...");
            var expandedResult = await ProbeCandidatesAsync(
                http,
                expandedCandidates,
                port,
                progress,
                log,
                alreadyChecked: quickCandidates.Count + wideCandidates.Count,
                total: quickCandidates.Count + wideCandidates.Count + expandedCandidates.Count,
                WideProbeTimeout,
                WideMaxConcurrency,
                ct);
            if (expandedResult is not null)
            {
                sw.Stop();
                return new ConsoleDiscoveryResult(expandedResult, port, sw.Elapsed);
            }
        }

        sw.Stop();
        return null;
    }

    private static async Task<string?> ProbeCandidatesAsync(
        HttpClient http,
        IReadOnlyList<string> candidates,
        int port,
        IProgress<(string Ip, int Checked, int Total)>? progress,
        Action<string>? log,
        int alreadyChecked,
        int total,
        TimeSpan timeout,
        int maxConcurrency,
        CancellationToken ct)
    {
        using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var checkedCount = alreadyChecked;
        string? foundIp = null;

        try
        {
            await Parallel.ForEachAsync(
                candidates,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = searchCts.Token
                },
                async (ip, workerCt) =>
                {
                    if (Volatile.Read(ref foundIp) is not null)
                        return;

                    var found = await ProbeOneAsync(http, ip, port, timeout, workerCt);
                    var done = Interlocked.Increment(ref checkedCount);
                    progress?.Report((ip, done, total));

                    if (!found)
                        return;

                    if (Interlocked.CompareExchange(ref foundIp, ip, null) is null)
                    {
                        log?.Invoke($"Consola encontrada en {ip}:{port}");
                        searchCts.Cancel();
                    }
                });
        }
        catch (OperationCanceledException) when (searchCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Expected when a worker finds the console and cancels the remaining probes.
        }

        return foundIp;
    }

    private static async Task<bool> ProbeOneAsync(
        HttpClient http,
        string ip,
        int port,
        TimeSpan timeout,
        CancellationToken ct)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            using var response = await http.GetAsync(
                $"http://{ip}:{port}/api/status",
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static HttpClient CreateProbeClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(100),
            MaxConnectionsPerServer = WideMaxConcurrency,
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.None,
            UseProxy = false
        };
        return new HttpClient(handler)
        {
            Timeout = WideProbeTimeout
        };
    }

    private static List<ConsoleDiscoveryPlanner.NetworkSnapshot> GetActiveIpv4Networks()
    {
        var result = new List<ConsoleDiscoveryPlanner.NetworkSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            try
            {
                var properties = ni.GetIPProperties();
                var gateways = properties.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(g => g.Address)
                    .Where(IsLocalNetworkAddress)
                    .ToList();

                foreach (var ua in properties.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork ||
                        ua.IPv4Mask is null ||
                        !IsLocalNetworkAddress(ua.Address))
                        continue;

                    var key = $"{ua.Address}|{ua.IPv4Mask}";
                    if (seen.Add(key))
                        result.Add(new ConsoleDiscoveryPlanner.NetworkSnapshot(ua.Address, ua.IPv4Mask, gateways));
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"Descubrimiento: no se pudo consultar la interfaz {ni.Name}: {ex.Message}", "WARN");
            }
        }

        return result;
    }

    private static bool IsLocalNetworkAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 169 && bytes[1] == 254 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }
}
