using System.Windows.Media;

namespace GarlicSaveMgr.Models;

public sealed class ConsoleConfig
{
    public string Name { get; set; } = "PS5";
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 8082;
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
}

public sealed record BackupResult(string Level, string Message);
