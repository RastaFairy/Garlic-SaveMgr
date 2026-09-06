using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Services;

/// <summary>
/// Gestiona ftpsrv en la PS5 y proporciona una ruta FTP segura y mínima para
/// guardar temporalmente copias internas antes de una eliminación destructiva.
///
/// ftpsrv expone FTP en :2121 y se carga mediante elfldr :9021 cuando no está
/// activo. El payload se descarga desde el catálogo público y se verifica por
/// SHA-256 antes de inyectarlo.
/// </summary>
public sealed class FtpsrvService
{
    public const int Port = 2121;
    private const int ElfldrPort = PayloadLauncherService.ElfldrPort;
    private const string Version = "v0.21.1";
    private const string FileName = "ftpsrv_v0.21.1.elf";
    private const string DownloadUrl = "https://github.com/ps5-payload-dev/ftpsrv/releases/download/v0.21.1/ftpsrv-ps5.elf";
    private const string ExpectedSha256 = "7d4b31c83eae4e056580482a3a074e1db25922efc74ac1e439473b45d71a5938";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

    private readonly string _cacheDir;

    public FtpsrvService()
    {
        _cacheDir = AppPaths.PayloadDirectory;
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<bool> EnsureRunningAsync(string consoleIp, Action<string, string>? log = null, CancellationToken ct = default)
    {
        if (await IsPortOpenAsync(consoleIp, Port, TimeSpan.FromMilliseconds(800), ct))
        {
            log?.Invoke($"ftpsrv activo en {consoleIp}:{Port}.", "ok");
            return true;
        }

        log?.Invoke($"ftpsrv no está activo en {consoleIp}:{Port}; preparando payload {Version}…", "info");
        var elf = await EnsureCachedPayloadAsync(log, ct);
        if (elf is null) return false;

        log?.Invoke($"Enviando ftpsrv {Version} a {consoleIp}:{ElfldrPort}…", "info");
        await SendElfAsync(consoleIp, ElfldrPort, elf, ct);
        log?.Invoke("ftpsrv enviado. Esperando el puerto FTP :2121…", "info");

        var deadline = DateTime.UtcNow + StartTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsPortOpenAsync(consoleIp, Port, TimeSpan.FromMilliseconds(900), ct))
            {
                log?.Invoke($"ftpsrv operativo en {consoleIp}:{Port}.", "ok");
                return true;
            }
            await Task.Delay(350, ct);
        }

        log?.Invoke("ftpsrv no respondió en el puerto 2121 después de cargar el payload.", "error");
        return false;
    }

    public async Task<string> UploadBackupAsync(
        string consoleIp,
        string remotePath,
        string localPath,
        CancellationToken ct = default)
    {
        var client = new SimpleFtpClient(consoleIp, Port);
        await client.ConnectAsync(ct);
        try
        {
            await client.EnsureDirectoryAsync("/data/backup_save_enc", ct);
            var parent = remotePath[..remotePath.LastIndexOf('/')];
            await client.EnsureDirectoryAsync(parent, ct);
            await client.UploadAsync(localPath, remotePath, ct);
            var remoteSize = await client.GetSizeAsync(remotePath, ct);
            var localSize = new FileInfo(localPath).Length;
            if (remoteSize != localSize)
            {
                try { await client.DeleteAsync(remotePath, ct); } catch { }
                throw new GarlicException($"El respaldo interno no coincide en tamaño: local={localSize} bytes, consola={remoteSize} bytes.");
            }
            return remotePath;
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<string>> ListNamesAsync(string consoleIp, string remoteDirectory, CancellationToken ct = default)
    {
        var client = new SimpleFtpClient(consoleIp, Port);
        await client.ConnectAsync(ct);
        try { return await client.ListNamesAsync(remoteDirectory, ct); }
        finally { await client.DisposeAsync(); }
    }

    public async Task DownloadAsync(string consoleIp, string remotePath, string localPath, CancellationToken ct = default)
    {
        var client = new SimpleFtpClient(consoleIp, Port);
        await client.ConnectAsync(ct);
        try { await client.DownloadAsync(remotePath, localPath, ct); }
        finally { await client.DisposeAsync(); }
    }

    public async Task<long> GetFileSizeAsync(string consoleIp, string remotePath, CancellationToken ct = default)
    {
        var client = new SimpleFtpClient(consoleIp, Port);
        await client.ConnectAsync(ct);
        try { return await client.GetSizeAsync(remotePath, ct); }
        finally { await client.DisposeAsync(); }
    }

    public async Task DeleteRemoteAsync(string consoleIp, string remotePath, CancellationToken ct = default)
    {
        var client = new SimpleFtpClient(consoleIp, Port);
        await client.ConnectAsync(ct);
        try { await client.DeleteAsync(remotePath, ct); }
        finally { await client.DisposeAsync(); }
    }

    private async Task<string?> EnsureCachedPayloadAsync(Action<string, string>? log, CancellationToken ct)
    {
        var path = Path.Combine(_cacheDir, FileName);
        if (File.Exists(path) && VerifyFile(path, ExpectedSha256))
        {
            log?.Invoke($"ftpsrv {Version} ya está cacheado y verificado.", "info");
            return path;
        }

        var temp = path + ".download";
        try
        {
            if (File.Exists(temp)) File.Delete(temp);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
            await input.CopyToAsync(output, 1024 * 1024, ct);
            await output.FlushAsync(ct);

            var sha = ComputeSha256(temp);
            if (!sha.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new GarlicException($"SHA-256 inválido para ftpsrv: esperado {ExpectedSha256}, obtenido {sha}.");

            File.Move(temp, path, true);
            log?.Invoke($"ftpsrv {Version} cacheado y verificado (SHA-256 OK).", "ok");
            return path;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log?.Invoke($"No se pudo preparar ftpsrv {Version}: {ex.Message}", "error");
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return null;
        }
    }

    private static bool VerifyFile(string path, string expected)
    {
        try { return ComputeSha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task SendElfAsync(string ip, int port, string elfPath, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(SendTimeout);
        using var client = new TcpClient { SendTimeout = (int)SendTimeout.TotalMilliseconds, ReceiveTimeout = (int)SendTimeout.TotalMilliseconds };
        await client.ConnectAsync(ip, port, linked.Token);
        await using var stream = client.GetStream();
        await using var file = new FileStream(elfPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        await file.CopyToAsync(stream, 1024 * 1024, linked.Token);
        await stream.FlushAsync(linked.Token);
    }

    private static async Task<bool> IsPortOpenAsync(string ip, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(ip, port, linked.Token);
            return client.Connected;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        catch (SocketException) { return false; }
    }

    private sealed class SimpleFtpClient : IAsyncDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _control;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        public SimpleFtpClient(string host, int port) { _host = host; _port = port; }

        public async Task ConnectAsync(CancellationToken ct)
        {
            _control = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(ConnectTimeout);
            await _control.ConnectAsync(_host, _port, linked.Token);
            var stream = _control.GetStream();
            _reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            _writer = new StreamWriter(stream, Encoding.ASCII, 4096, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

            await ExpectAsync(220, ct);
            await CommandAsync("USER anonymous", 230, ct);
            await CommandAsync("TYPE I", 200, ct);
        }

        public async Task EnsureDirectoryAsync(string path, CancellationToken ct)
        {
            var clean = "/" + path.Trim('/');
            var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = "";
            foreach (var part in parts)
            {
                current += "/" + part;
                var code = await CommandRawAsync($"MKD {current}", ct);
                if (code is 200 or 226 or 250) continue;
                if (code is 450 or 550) continue; // ya existe / servidor devuelve error equivalente
                throw new GarlicException($"FTP MKD {current} devolvió {code}.");
            }
        }

        public async Task<IReadOnlyList<string>> ListNamesAsync(string remoteDirectory, CancellationToken ct)
        {
            // La Papelera solo puede trabajar dentro de esta raíz. Cualquier otra
            // ruta solicitada queda bloqueada antes de llegar al servidor FTP.
            const string trashRoot = "/data/backup_save_enc";
            if (!IsTrashPath(remoteDirectory, trashRoot))
                throw new GarlicException($"Ruta FTP fuera de la Papelera: {remoteDirectory}");

            // ftpsrv puede no implementar NLST; en ese caso usamos LIST. En ambos
            // casos filtramos las respuestas y aceptamos únicamente nombres simples
            // de un solo segmento. Nunca devolvemos rutas relativas, absolutas ni
            // recorridos con '/', '\\', '.', '..' o segmentos adicionales.
            IReadOnlyList<string> raw;
            try
            {
                // ftpsrv se comporta de forma más estable con LIST cuando una
                // carpeta está vacía; NLST puede cerrar la transferencia de datos
                // provocando un TaskCanceledException aunque la conexión siga viva.
                raw = await ListWithCommandAsync("LIST", remoteDirectory, parseListing: true, ct: ct);
            }
            catch (GarlicException ex) when (IsUnsupportedListCommand(ex.Message))
            {
                raw = await ListWithCommandAsync("NLST", remoteDirectory, parseListing: false, ct: ct);
            }
            catch (OperationCanceledException) when (!ct.CanBeCanceled)
            {
                // Algunos ftpsrv cancelan la lectura de la conexión de datos al
                // devolver una LIST vacía. Para la Papelera esto equivale a 0 items.
                return Array.Empty<string>();
            }

            return raw.Where(IsSafeSingleFtpSegment).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool IsTrashPath(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var normalizedRoot = root.TrimEnd('/');
            var normalized = path.Trim().Replace('\\', '/').TrimEnd('/');
            if (string.Equals(normalized, normalizedRoot, StringComparison.Ordinal)) return true;
            return normalized.StartsWith(normalizedRoot + "/", StringComparison.Ordinal)
                && !normalized.Contains("..", StringComparison.Ordinal);
        }

        private static bool IsSafeSingleFtpSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim().Replace('\\', '/');
            if (v.Length == 0 || v == "." || v == "..") return false;
            if (v.StartsWith("/", StringComparison.Ordinal) || v.Contains("/", StringComparison.Ordinal)) return false;
            if (v.Contains("..", StringComparison.Ordinal)) return false;
            return v.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && !v.Contains(':')
                && !v.Contains('\\');
        }

        private async Task<IReadOnlyList<string>> ListWithCommandAsync(string command, string remoteDirectory, bool parseListing, CancellationToken ct)
        {
            var (data, _) = await OpenPassiveAsync(ct);
            try
            {
                await CommandAsync(command + " " + remoteDirectory, 150, ct, allow: [125]);
                using (data)
                using (var reader = new StreamReader(data, Encoding.ASCII, false, 4096, leaveOpen: false))
                {
                    var names = new List<string>();
                    while (true)
                    {
                        var line = await reader.ReadLineAsync(ct);
                        if (line is null) break;
                        var name = parseListing ? ParseListLineName(line) : line.Trim();
                        if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                    }
                    await ExpectAsync(226, ct, allow: [250]);
                    return names;
                }
            }
            finally
            {
                await data.DisposeAsync();
            }
        }

        private static bool IsUnsupportedListCommand(string message)
            => message.Contains(" 502 ", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Command not recognized", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not implemented", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not supported", StringComparison.OrdinalIgnoreCase);

        private static string ParseListLineName(string line)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("total ", StringComparison.OrdinalIgnoreCase))
                return "";

            // Unix-style LIST: type + permissions/metadata + filename. Backup
            // names generated by this application never contain spaces, so the
            // final whitespace-delimited token is the stable filename.
            if (line.Length > 0 && "-dclpsb".IndexOf(char.ToLowerInvariant(line[0])) >= 0)
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 9)
                    return string.Join(" ", parts.Skip(8));
            }

            // Fallback for DOS/other compact listings: use the final field.
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return fields.Length == 0 ? "" : fields[^1];
        }

        public async Task DownloadAsync(string remotePath, string localPath, CancellationToken ct)
        {
            var (data, _) = await OpenPassiveAsync(ct);
            await CommandAsync("RETR " + remotePath, 150, ct, allow: [125]);
            await using (data)
            await using (var output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                await data.CopyToAsync(output, 1024 * 1024, ct);
                await output.FlushAsync(ct);
            }
            await ExpectAsync(226, ct, allow: [250]);
        }

        public async Task UploadAsync(string localPath, string remotePath, CancellationToken ct)
        {
            var (data, _) = await OpenPassiveAsync(ct);
            await CommandAsync("STOR " + remotePath, 150, ct, allow: [125]);
            await using (data)
            await using (var input = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true))
            {
                await input.CopyToAsync(data, 1024 * 1024, ct);
            }
            await ExpectAsync(226, ct, allow: [250]);
        }

        public async Task<long> GetSizeAsync(string remotePath, CancellationToken ct)
        {
            var line = await CommandLineAsync("SIZE " + remotePath, ct, expected: [213]);
            var value = line.Length > 4 ? line[4..].Trim() : "";
            if (!long.TryParse(value, out var size)) throw new GarlicException($"Respuesta SIZE inválida para {remotePath}: {line}");
            return size;
        }

        public async Task DeleteAsync(string remotePath, CancellationToken ct)
            => await CommandAsync("DELE " + remotePath, 226, ct, allow: [250]);

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_writer is not null) await CommandRawAsync("QUIT", CancellationToken.None);
            }
            catch { }
            _writer?.Dispose();
            _reader?.Dispose();
            _control?.Dispose();
        }

        private async Task<(NetworkStream Stream, string Host)> OpenPassiveAsync(CancellationToken ct)
        {
            var line = await CommandLineAsync("PASV", ct, expected: [227]);
            var start = line.IndexOf('(');
            var end = line.IndexOf(')', start + 1);
            if (start < 0 || end < 0) throw new GarlicException("Respuesta PASV inválida.");
            var parts = line[(start + 1)..end].Split(',');
            if (parts.Length != 6 || !int.TryParse(parts[4], out var p1) || !int.TryParse(parts[5], out var p2))
                throw new GarlicException("Respuesta PASV inválida.");
            var port = (p1 << 8) + p2;
            var host = string.Join('.', parts.Take(4));
            var dataClient = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(10));
            await dataClient.ConnectAsync(host, port, linked.Token);
            return (dataClient.GetStream(), host);
        }

        private async Task<string> CommandLineAsync(string command, CancellationToken ct, int[] expected)
        {
            EnsureConnected();
            await _writer!.WriteLineAsync(command.AsMemory(), ct);
            var line = await ReadResponseAsync(ct);
            var code = ParseCode(line);
            if (!expected.Contains(code)) throw new GarlicException($"FTP {command} devolvió {line}");
            return line;
        }

        private async Task<int> CommandRawAsync(string command, CancellationToken ct)
        {
            EnsureConnected();
            await _writer!.WriteLineAsync(command.AsMemory(), ct);
            return ParseCode(await ReadResponseAsync(ct));
        }

        private Task CommandAsync(string command, int expected, CancellationToken ct, int[]? allow = null)
            => ExpectCommandAsync(command, [expected, ..(allow ?? [])], ct);

        private async Task ExpectCommandAsync(string command, int[] expected, CancellationToken ct)
        {
            EnsureConnected();
            await _writer!.WriteLineAsync(command.AsMemory(), ct);
            var line = await ReadResponseAsync(ct);
            var code = ParseCode(line);
            if (!expected.Contains(code)) throw new GarlicException($"FTP {command} devolvió {line}");
        }

        private Task ExpectAsync(int expected, CancellationToken ct, int[]? allow = null)
            => ExpectResponseAsync([expected, ..(allow ?? [])], ct);

        private async Task ExpectResponseAsync(int[] expected, CancellationToken ct)
        {
            var line = await ReadResponseAsync(ct);
            var code = ParseCode(line);
            if (!expected.Contains(code)) throw new GarlicException($"FTP devolvió {line}");
        }

        private async Task<string> ReadResponseAsync(CancellationToken ct)
        {
            EnsureConnected();
            var first = await _reader!.ReadLineAsync(ct) ?? throw new GarlicException("FTP cerró la conexión.");
            if (first.Length < 4) return first;
            var code = first[..3];
            if (first[3] == '-')
            {
                while (true)
                {
                    var line = await _reader.ReadLineAsync(ct) ?? throw new GarlicException("FTP cerró la conexión durante la respuesta.");
                    if (line.StartsWith(code + " ", StringComparison.Ordinal)) return line;
                }
            }
            return first;
        }

        private static int ParseCode(string line) => int.TryParse(line.AsSpan(0, Math.Min(3, line.Length)), out var code) ? code : 0;
        private void EnsureConnected()
        {
            if (_control is null || _reader is null || _writer is null) throw new GarlicException("Cliente FTP no conectado.");
        }
    }
}
