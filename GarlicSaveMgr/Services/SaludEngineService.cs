using System.IO;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

public static class SaludEngineService
{
    public static EngineState Build(
        ConsoleConfig console,
        IReadOnlyList<TitleInfo> titles,
        IReadOnlyList<BackupEntry> backups,
        IReadOnlyList<SnapshotRecord> snapshots)
    {
        var state = new EngineState
        {
            ConsoleName = string.IsNullOrWhiteSpace(console.Name) ? "PS5" : console.Name,
            ConsoleAddress = string.IsNullOrWhiteSpace(console.Ip) ? "" : $"{console.Ip}:{console.Port}",
            Ps5Titles = titles.Count,
            Ps5Slots = titles.Sum(t => Math.Max(0, t.SlotCount)),
            PcBackups = backups.Count,
            PcBackupGames = backups.Select(b => b.TitleId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            PcSnapshots = snapshots.Count,
            PcBackupBytes = backups.Sum(b => SizeResolutionService.GetRealFileSize(b.ImgPath)),
            PcSnapshotBytes = 0,
            SmartBackupState = "Activo · coincidencias seguras"
        };

        try
        {
            var root = Path.GetPathRoot(AppPaths.RootDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                state.PcTotalBytes = drive.TotalSize;
                state.PcFreeBytes = drive.AvailableFreeSpace;
            }
        }
        catch
        {
            state.PcTotalBytes = 0;
            state.PcFreeBytes = 0;
        }

        state.IntegrityOk = backups.Count(b => !string.IsNullOrWhiteSpace(b.Sha256) && File.Exists(b.ImgPath));
        state.IntegrityErrors = backups.Count(b => !File.Exists(b.ImgPath));
        state.IntegrityUnchecked = backups.Count(b => string.IsNullOrWhiteSpace(b.Sha256));
        state.UpdatedLocal = DateTime.Now;
        return state;
    }
}
