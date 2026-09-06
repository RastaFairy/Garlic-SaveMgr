using System.Windows.Media;

namespace GarlicSaveMgr.Models;

public sealed class ConsoleConfig
{
    public string Name { get; set; } = "PS5";
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 8082;
}

public sealed class ConsoleConnection
{
    public string Ip { get; init; } = "";
    public int Port { get; init; } = 8082;
    public List<string> UserIds { get; } = [];
    public bool? GarlicApiAvailable { get; set; }
    public bool? ElfLdrAvailable { get; set; }
    public DateTime? LastSeenLocal { get; set; }
    public int? TitleCount { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(Ip) ? "PS5" : Ip;

    // Representación humana por defecto: el combo de consolas y cualquier
    // interpolación deben mostrar la IP, nunca el nombre del tipo.
    public override string ToString() => DisplayName;

    public ConsoleConfig ToConfig(string? name = null) => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? "PS5" : name.Trim(),
        Ip = Ip,
        Port = Port
    };
}

public sealed class TitleInfo
{
    public string TitleId { get; set; } = "";
    public string Uid { get; set; } = "";
    public string TitleName { get; set; } = "";
    public int SlotCount { get; set; }
    public int BackupCount { get; set; }
    public List<SlotInfo> Slots { get; set; } = [];
}

public sealed class SlotInfo
{
    public string Name { get; set; } = "";
    public bool Backup { get; set; }
    public int ConsoleIndex { get; set; } = -1;
}

public sealed class BackupEntry
{
    public string ImgPath { get; set; } = "";
    public string TitleId { get; set; } = "";
    public string SaveName { get; set; } = "";
    public string TitleName { get; set; } = "";
    public Dictionary<string, object?> Owner { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Origin { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Date { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string RemoteFingerprint { get; set; } = "";
}

public enum BackupIntegrityStatus
{
    NotChecked,
    Valid,
    MissingHash,
    Mismatch,
    Error
}

public sealed record BackupIntegrityResult(
    BackupIntegrityStatus Status,
    string ExpectedSha256,
    string ActualSha256,
    string ErrorMessage = "");

public sealed record BackupResult(string Level, string Message);

public enum ConsoleDeleteMode
{
    Permanent,
    InternalBackup
}

public enum PcBackupDeleteMode
{
    Permanent,
    MoveToTrash
}

public enum LocalBackupDeleteStatus
{
    PresentOnConsole,
    MissingOnConsole,
    CannotVerify
}

public sealed record LocalBackupDeleteCheck(
    BackupEntry Backup,
    LocalBackupDeleteStatus Status,
    string ConsoleAddress,
    string Detail);

public sealed record ConsoleDeletePreview(
    string TitleId,
    string Uid,
    string TitleName,
    IReadOnlyList<string> SlotNames,
    IReadOnlyList<string> CoveredSlotNames)
{
    public int SlotCount => SlotNames.Count;
    public int CoveredSlotCount => CoveredSlotNames.Count;
    public int UncoveredSlotCount => Math.Max(0, SlotCount - CoveredSlotCount);
}


public sealed class PcTrashEntry
{
    public string TrashDirectory { get; init; } = "";
    public string ImgPath { get; init; } = "";
    public string JsonPath { get; init; } = "";
    public string TitleId { get; init; } = "";
    public string SaveName { get; init; } = "";
    public string TitleName { get; init; } = "";
    public string UserId { get; init; } = "";
    public string SourceConsole { get; init; } = "";
    public string Date { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
    public bool Selected { get; set; }
    public BackupIntegrityStatus IntegrityStatus { get; set; } = BackupIntegrityStatus.NotChecked;
    public string IntegrityDisplay => IntegrityStatus switch
    {
        BackupIntegrityStatus.Valid => "OK",
        BackupIntegrityStatus.MissingHash => "SIN HASH",
        BackupIntegrityStatus.Mismatch => "CORRUPTA",
        BackupIntegrityStatus.Error => "ERROR",
        _ => "SIN COMPROBAR"
    };
    public string SizeDisplay => Size > 0 ? FormatBytes(Size) : "—";
    private static string FormatBytes(long n)
    {
        double d = n;
        foreach (var u in new[] { "B", "KB", "MB", "GB" })
        {
            if (d < 1024) return $"{d:0.#} {u}";
            d /= 1024;
        }
        return $"{d:0.0} TB";
    }
}

public sealed class TrashEntry
{
    public string TitleId { get; init; } = "";
    public string SaveName { get; init; } = "";
    public string TitleName { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Date { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
    public string RemoteImgPath { get; init; } = "";
    public string RemoteJsonPath { get; init; } = "";
    public bool Selected { get; set; }
    public BackupIntegrityStatus IntegrityStatus { get; set; } = BackupIntegrityStatus.NotChecked;
    public string IntegrityDisplay => IntegrityStatus switch
    {
        BackupIntegrityStatus.Valid => "OK",
        BackupIntegrityStatus.MissingHash => "SIN HASH",
        BackupIntegrityStatus.Mismatch => "CORRUPTA",
        BackupIntegrityStatus.Error => "ERROR",
        _ => "SIN COMPROBAR"
    };
    public string SizeDisplay => Size > 0 ? FormatBytes(Size) : "—";
    private static string FormatBytes(long n)
    {
        double d = n;
        foreach (var u in new[] { "B", "KB", "MB", "GB" })
        {
            if (d < 1024) return $"{d:0.#} {u}";
            d /= 1024;
        }
        return $"{d:0.0} TB";
    }
}
