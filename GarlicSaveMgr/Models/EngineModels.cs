using System.Text.Json.Serialization;

namespace GarlicSaveMgr.Models;

public sealed class EngineState
{
    public DateTime CreatedLocal { get; set; } = DateTime.Now;
    public DateTime UpdatedLocal { get; set; } = DateTime.Now;
    public string ConsoleName { get; set; } = "PS5";
    public string ConsoleAddress { get; set; } = "";
    public bool Ps5Online { get; set; }
    public string Ps5State { get; set; } = "Sin conexión";
    public int Ps5Titles { get; set; }
    public int Ps5Slots { get; set; }
    public int PcBackups { get; set; }
    public int PcBackupGames { get; set; }
    public int PcSnapshots { get; set; }
    public long PcBackupBytes { get; set; }
    public long PcSnapshotBytes { get; set; }
    public long PcFreeBytes { get; set; }
    public long PcTotalBytes { get; set; }
    public int IntegrityOk { get; set; }
    public int IntegrityErrors { get; set; }
    public int IntegrityUnchecked { get; set; }
    public string SmartBackupState { get; set; } = "Listo";
}

public sealed class SnapshotRecord
{
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedLocal { get; set; } = DateTime.Now;
    public string ConsoleAddress { get; set; } = "";
    public string ConsoleName { get; set; } = "PS5";
    public string TitleId { get; set; } = "";
    public string TitleName { get; set; } = "";
    public string SaveName { get; set; } = "";
    public string Uid { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string RemoteFingerprint { get; set; } = "";

    [JsonIgnore]
    public string SizeDisplay => Size > 0 ? FormatSize(Size) : "—";

    [JsonIgnore]
    public string HashDisplay => string.IsNullOrWhiteSpace(Sha256) ? "—" : (Sha256.Length <= 12 ? Sha256 : Sha256[..12] + "…");

    [JsonIgnore]
    public string State => File.Exists(BackupPath) ? "Backup disponible" : "Solo historial";

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var value = (double)bytes;
        var units = new[] { "KB", "MB", "GB", "TB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}

public sealed record SmartBackupDecision(bool Skip, string Reason);
