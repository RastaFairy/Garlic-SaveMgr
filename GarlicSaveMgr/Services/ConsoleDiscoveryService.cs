using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;

namespace GarlicSaveMgr.Services;

public sealed record ConsoleDiscoveryResult(string Ip, int Port, TimeSpan Elapsed);

public sealed class ConsoleDiscoveryService
{
    public const int DefaultPort = 8082;
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(10);
    public const int MaxConcurrency = 32;
    private const int HostsPerBatch = 32;

    public async Task<ConsoleDiscoveryResult?> DiscoverAsync(
        int port = DefaultPort,
        IProgress<(string Ip, int Checked, int Total)>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var candidates = BuildCandidates();
        var sw = Stopwatch.StartNew();
        using var http = CreateProbeClient();
        using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var checkedCount = 0;

        for (var offset = 0; offset < candidates.Count; offset += HostsPerBatch)
        {
            searchCts.Token.ThrowIfCancellationRequested();
            var batch = candidates.Skip(offset).Take(HostsPerBatch).ToArray();
            var tasks = batch.Select(ip => ProbeOneAsync(http, ip, port, progress, log, candidates.Count, () => Interlocked.Increment(ref checkedCount), searchCts.Token)).ToList();

            var winner = await WaitForFirstMatchAsync(tasks, searchCts.Token);
            if (winner is not null)
            {
                searchCts.Cancel();
                try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }
                sw.Stop();
                return new ConsoleDiscoveryResult(winner, port, sw.Elapsed);
            }

            // All candidates in this batch have completed; continue with the next batch.
            await Task.WhenAll(tasks);
        }

        sw.Stop();
        return null;
    }

    private static async Task<string?> WaitForFirstMatchAsync(List<Task<(string Ip, bool Found)>> tasks, CancellationToken ct)
    {
        while (tasks.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            var result = await completed;
            if (result.Found) return result.Ip;
        }
        return null;
    }

    private static async Task<(string Ip, bool Found)> ProbeOneAsync(
        HttpClient http,
        string ip,
        int port,
        IProgress<(string Ip, int Checked, int Total)>? progress,
        Action<string>? log,
        int total,
        Func<int> incrementChecked,
        CancellationToken ct)
    {
        var found = false;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(ProbeTimeout);
            using var response = await http.GetAsync($"http://{ip}:{port}/api/status", HttpCompletionOption.ResponseHeadersRead, linked.Token);
            found = response.IsSuccessStatusCode;
            return (ip, found);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (ip, false); }
        catch (HttpRequestException) { return (ip, false); }
        catch (SocketException) { return (ip, false); }
        finally
        {
            var done = incrementChecked();
            progress?.Report((ip, done, total));
            // Deliberately do not log every address: per-IP file I/O dominated the old scan timing.
            if (found) log?.Invoke($"Consola encontrada en {ip}:{port}");
        }
    }

    private static HttpClient CreateProbeClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = ProbeTimeout,
            MaxConnectionsPerServer = MaxConcurrency,
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.None
        };
        return new HttpClient(handler)
        {
            Timeout = ProbeTimeout
        };
    }

    private static List<string> BuildCandidates()
    {
        var result = new List<string>(254);
        var networkPrefix = GetActiveIPv4NetworkPrefix();
        if (networkPrefix is null)
            return result;

        for (var host = 1; host <= 254; host++)
            result.Add($"{networkPrefix}.{host}");

        return result;
    }

    private static string? GetActiveIPv4NetworkPrefix()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            try
            {
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork ||
                        ua.IPv4Mask is null)
                        continue;

                    var address = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    var network = new byte[4];
                    for (var i = 0; i < 4; i++)
                        network[i] = (byte)(address[i] & mask[i]);

                    // The discovery scope is one /24. For a real /24 (the normal
                    // LAN case), the interface mask determines the exact network.
                    // For larger/smaller masks, keep the /24 containing the active IP.
                    var prefixBits = CountPrefixBits(mask);
                    var subnet = prefixBits == 24 ? network : address;

                    return $"{subnet[0]}.{subnet[1]}.{subnet[2]}";
                }
            }
            catch
            {
                // Ignore interfaces that cannot be queried and try the next active one.
            }
        }

        return null;
    }

    private static int CountPrefixBits(byte[] mask)
    {
        var bits = 0;
        foreach (var value in mask)
        {
            var current = value;
            while (current != 0)
            {
                bits += current >> 7;
                current <<= 1;
            }

            if (value != byte.MaxValue)
                break;
        }
        return bits;
    }
}
