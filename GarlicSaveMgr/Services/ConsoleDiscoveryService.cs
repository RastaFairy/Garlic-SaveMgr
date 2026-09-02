using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace GarlicSaveMgr.Services;

public sealed record ConsoleDiscoveryResult(string Ip, int Port, TimeSpan Elapsed);

/// <summary>
/// Simple deterministic discovery for the 192.168.0.0/16 space.
/// Each batch contains up to 255 addresses. Every address is always pinged first
/// by the native Windows ping.exe process. Ping output is persisted under the
/// portable application directory so the successful replies can be analysed after
/// the batch finishes. Only ping-positive hosts are then probed on Garlic ports.
/// </summary>
public sealed class ConsoleDiscoveryService
{
    public const int DefaultPort = 8082;
    public const int ElfLdrPort = 9021;
    public static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan HttpProbeTimeout = TimeSpan.FromMilliseconds(500);
    public const int BatchSize = 255;
    public const int MaxAddresses = 1 << 16; // 192.168.0.0 .. 192.168.255.255

    public async Task<ConsoleDiscoveryResult?> DiscoverAsync(
        int port = DefaultPort,
        IProgress<(string Ip, int Checked, int Total)>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var tempRoot = Path.Combine(
            GarlicSaveMgr.Infrastructure.AppPaths.RootDirectory,
            "discovery_temp");
        Directory.CreateDirectory(tempRoot);

        try
        {
            for (var batchStart = 0; batchStart < MaxAddresses; batchStart += BatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var count = Math.Min(BatchSize, MaxAddresses - batchStart);
                var batchDirectory = Path.Combine(tempRoot, $"batch_{batchStart:D5}");
                Directory.CreateDirectory(batchDirectory);

                log?.Invoke($"Ping batch: {Format192168Address(batchStart)} + {count - 1} direcciones.");

                var tasks = new List<Task<PingProbeResult>>(count);
                for (var i = 0; i < count; i++)
                {
                    var offset = batchStart + i;
                    tasks.Add(RunNativePingAsync(offset, batchDirectory, ct));
                }

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                var pingOk = results.Where(r => r.Success).OrderBy(r => r.Offset).ToList();

                foreach (var result in results)
                {
                    var checkedCount = result.Offset + 1;
                    progress?.Report((result.Ip, checkedCount, MaxAddresses));
                }

                log?.Invoke($"Ping batch completado: {pingOk.Count}/{count} respondieron.");

                foreach (var result in pingOk)
                {
                    ct.ThrowIfCancellationRequested();
                    log?.Invoke($"Ping OK: {result.Ip}");

                    var garlicPort = await FindGarlicPortAsync(result.Ip, port, ct).ConfigureAwait(false);
                    if (garlicPort is not null)
                    {
                        sw.Stop();
                        log?.Invoke($"Consola encontrada en {result.Ip}:{garlicPort.Value}");
                        return new ConsoleDiscoveryResult(result.Ip, garlicPort.Value, sw.Elapsed);
                    }
                }

                // Keep the portable temp tree bounded while retaining the latest batch
                // for diagnostics. Older batch directories are safe to remove after use.
                CleanupOldBatchDirectories(tempRoot, keepLatest: 4);
            }

            sw.Stop();
            return null;
        }
        finally
        {
            CleanupOldBatchDirectories(tempRoot, keepLatest: 2);
        }
    }

    private static async Task<PingProbeResult> RunNativePingAsync(int offset, string batchDirectory, CancellationToken ct)
    {
        var ip = Format192168Address(offset);
        var safeIp = ip.Replace('.', '_');
        var outputPath = Path.Combine(batchDirectory, $"{safeIp}.txt");

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "ping.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = GarlicSaveMgr.Infrastructure.AppPaths.RootDirectory,
            Arguments = $"-n 1 -w {(int)PingTimeout.TotalMilliseconds} {ip}"
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                return new PingProbeResult(offset, ip, false);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var output = string.Concat(stdout, stderr);
            await File.WriteAllTextAsync(outputPath, output, ct).ConfigureAwait(false);

            // ping.exe exit code 0 means at least one echo reply was received.
            // TTL is kept as a second guard so a redirected/local diagnostic line is not
            // accidentally treated as a successful remote host.
            var success = process.ExitCode == 0 &&
                          output.Contains("TTL=", StringComparison.OrdinalIgnoreCase);
            return new PingProbeResult(offset, ip, success);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch
        {
            return new PingProbeResult(offset, ip, false);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static async Task<int?> FindGarlicPortAsync(string ip, int preferredPort, CancellationToken ct)
    {
        if (await ProbeGarlicAsync(ip, preferredPort, ct).ConfigureAwait(false))
            return preferredPort;

        // 9021 belongs to elfldr, not to the Garlic HTTP API. It is therefore only
        // evidence that this host is the console; the caller must keep using 8082
        // after the payload is sent and Garlic starts.
        if (preferredPort != ElfLdrPort && await ProbeTcpPortAsync(ip, ElfLdrPort, ct).ConfigureAwait(false))
            return preferredPort;

        return null;
    }

    private static async Task<bool> ProbeGarlicAsync(string ip, int port, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = HttpProbeTimeout,
            PooledConnectionLifetime = TimeSpan.FromSeconds(10)
        };
        using var http = new HttpClient(handler) { Timeout = HttpProbeTimeout };

        try
        {
            using var response = await http.GetAsync(
                $"http://{ip}:{port}/api/status",
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return false;

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mediaType, "text/json", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeTcpPortAsync(string ip, int port, CancellationToken ct)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(HttpProbeTimeout);
            await tcp.ConnectAsync(ip, port, timeoutCts.Token).ConfigureAwait(false);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string Format192168Address(int offset)
    {
        var third = (offset >> 8) & 0xFF;
        var fourth = offset & 0xFF;
        return $"192.168.{third}.{fourth}";
    }

    private static void CleanupOldBatchDirectories(string root, int keepLatest)
    {
        try
        {
            var directories = Directory.GetDirectories(root, "batch_*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName)
                .Skip(keepLatest)
                .ToList();

            foreach (var directory in directories)
            {
                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }
        catch
        {
            // Diagnostics directory cleanup must never break discovery.
        }
    }

    private sealed record PingProbeResult(int Offset, string Ip, bool Success);
}
