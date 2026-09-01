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
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(150);
    public const int MaxConcurrency = 32;

    public async Task<ConsoleDiscoveryResult?> DiscoverAsync(
        int port = DefaultPort,
        IProgress<(string Ip, int Checked, int Total)>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var candidates = BuildCandidates();
        if (candidates.Count == 0)
            return null;

        var sw = Stopwatch.StartNew();
        using var http = CreateProbeClient();
        using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var checkedCount = 0;

        var tasks = candidates
            .Select(ip => ProbeOneAsync(
                http,
                gate,
                ip,
                port,
                progress,
                log,
                candidates.Count,
                () => Interlocked.Increment(ref checkedCount),
                searchCts.Token))
            .ToList();

        try
        {
            while (tasks.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);

                var result = await completed;
                if (!result.Found)
                    continue;

                searchCts.Cancel();
                try { await Task.WhenAll(tasks); }
                catch (OperationCanceledException) { }

                sw.Stop();
                return new ConsoleDiscoveryResult(result.Ip, port, sw.Elapsed);
            }
        }
        finally
        {
            searchCts.Cancel();
            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { }
        }

        sw.Stop();
        return null;
    }

    private static async Task<(string Ip, bool Found)> ProbeOneAsync(
        HttpClient http,
        SemaphoreSlim gate,
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
            await gate.WaitAsync(ct);
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(ProbeTimeout);
                using var response = await http.GetAsync(
                    $"http://{ip}:{port}/api/status",
                    HttpCompletionOption.ResponseHeadersRead,
                    linked.Token);
                found = response.IsSuccessStatusCode;
                return (ip, found);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (ip, false);
        }
        catch (HttpRequestException)
        {
            return (ip, false);
        }
        catch (SocketException)
        {
            return (ip, false);
        }
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
            ConnectTimeout = TimeSpan.FromMilliseconds(100),
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
        var network = GetActiveIpv4Network();
        if (network is null)
            return [];

        var result = new List<string>(254);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(byte[] address)
        {
            var ip = $"{address[0]}.{address[1]}.{address[2]}.{address[3]}";
            if (address[3] is 0 or 255)
                return;
            if (seen.Add(ip))
                result.Add(ip);
        }

        // Prioritize the local host and default gateway so common network
        // configurations are checked immediately, without sacrificing coverage.
        Add(network.Value.Host);
        foreach (var gateway in network.Value.Gateways)
            Add(gateway);

        for (var host = 1; host <= 254; host++)
        {
            var address = new[]
            {
                network.Value.Network[0],
                network.Value.Network[1],
                network.Value.Host[2],
                (byte)host
            };
            Add(address);
        }

        return result;
    }

    private static (byte[] Network, byte[] Host, List<byte[]> Gateways)? GetActiveIpv4Network()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            try
            {
                var properties = ni.GetIPProperties();
                foreach (var ua in properties.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork ||
                        ua.IPv4Mask is null)
                        continue;

                    var address = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    var network = new byte[4];
                    for (var i = 0; i < 4; i++)
                        network[i] = (byte)(address[i] & mask[i]);

                    var gateways = properties.GatewayAddresses
                        .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(g => g.Address.GetAddressBytes())
                        .ToList();

                    return (network, address, gateways);
                }
            }
            catch
            {
                // Ignore interfaces that cannot be queried and try the next active one.
            }
        }

        return null;
    }
}
