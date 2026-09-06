using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GarlicSaveMgr.UpdaterModule.Services;

public sealed class UpdateInstaller
{
    public async Task ScheduleExecutableReplacementAsync(
        string currentExecutable,
        string downloadedExecutable,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(currentExecutable)) throw new FileNotFoundException("No se encontró el ejecutable actual.", currentExecutable);
        if (!File.Exists(downloadedExecutable)) throw new FileNotFoundException("No se encontró el ejecutable descargado.", downloadedExecutable);

        if (!await IsValidPeAsync(downloadedExecutable, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("El archivo descargado no parece ser un ejecutable Windows válido.");

        var directory = Path.GetDirectoryName(currentExecutable) ?? throw new InvalidDataException("No se pudo determinar la carpeta de instalación.");
        var script = Path.Combine(Path.GetTempPath(), $"GarlicSaveMgr-update-{Guid.NewGuid():N}.ps1");
        var backup = currentExecutable + $".previous-{Guid.NewGuid():N}";
        var pid = Environment.ProcessId;

        var scriptText = BuildScript(pid, currentExecutable, downloadedExecutable, backup, targetVersion, script);
        await File.WriteAllTextAsync(script, scriptText, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("No se encontró Windows PowerShell para completar la actualización.", powershell);

        Process.Start(new ProcessStartInfo
        {
            FileName = powershell,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
            UseShellExecute = false,
            WorkingDirectory = directory,
            CreateNoWindow = true
        });

        await Task.Delay(350, cancellationToken).ConfigureAwait(false);
        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
    }

    public static async Task<bool> IsValidPeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length < 2) return false;
        var header = new byte[2];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        return header[0] == (byte)'M' && header[1] == (byte)'Z';
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, useAsync: true);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static string BuildScript(int pid, string current, string downloaded, string backup, string version, string script)
    {
        var ps = new StringBuilder();
        ps.AppendLine("$ErrorActionPreference = 'Stop'");
        ps.AppendLine($"$pid = {pid}");
        ps.AppendLine($"$current = '{Quote(current)}'");
        ps.AppendLine($"$downloaded = '{Quote(downloaded)}'");
        ps.AppendLine($"$backup = '{Quote(backup)}'");
        ps.AppendLine($"$script = '{Quote(script)}'");
        ps.AppendLine("while (Get-Process -Id $pid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 250 }");
        ps.AppendLine("try {");
        ps.AppendLine("  Move-Item -LiteralPath $current -Destination $backup -Force");
        ps.AppendLine("  Move-Item -LiteralPath $downloaded -Destination $current -Force");
        ps.AppendLine("  $new = Start-Process -FilePath $current -WorkingDirectory (Split-Path $current) -PassThru");
        ps.AppendLine("  Start-Sleep -Seconds 5");
        ps.AppendLine("  if ($new.HasExited) { throw 'La nueva versión terminó durante el arranque.' }");
        ps.AppendLine("  Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue");
        ps.AppendLine("  Remove-Item -LiteralPath $script -Force -ErrorAction SilentlyContinue");
        ps.AppendLine("} catch {");
        ps.AppendLine("  if (Test-Path -LiteralPath $current) { Remove-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue }");
        ps.AppendLine("  if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $current -Force }");
        ps.AppendLine("  if (Test-Path -LiteralPath $downloaded) { Remove-Item -LiteralPath $downloaded -Force -ErrorAction SilentlyContinue }");
        ps.AppendLine("  Start-Process -FilePath $current -WorkingDirectory (Split-Path $current) -ErrorAction SilentlyContinue");
        ps.AppendLine("  Remove-Item -LiteralPath $script -Force -ErrorAction SilentlyContinue");
        ps.AppendLine("}");
        return ps.ToString();
    }

    private static string Quote(string value) => value.Replace("'", "''");
}
