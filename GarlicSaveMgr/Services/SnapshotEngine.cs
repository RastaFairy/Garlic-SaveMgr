using System.Text.Json;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

/// <summary>
/// Snapshot Engine 6.8.7.23: mantiene un historial ligero de estados de las copias.
/// Los snapshots son manifiestos; no duplican los IMG por defecto.
/// </summary>
public static class SnapshotEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<SnapshotRecord> Load()
    {
        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.SnapshotsIndexFile)) return [];
        try
        {
            var records = JsonSerializer.Deserialize<List<SnapshotRecord>>(File.ReadAllText(AppPaths.SnapshotsIndexFile), JsonOptions) ?? [];
            foreach (var record in records)
            {
                // Always derive displayed size from the physical IMG when it exists.
                // A snapshot is a manifest and does not consume the backup bytes again.
                record.Size = !string.IsNullOrWhiteSpace(record.BackupPath)
                    ? SizeResolutionService.GetRealFileSize(record.BackupPath)
                    : 0;
            }
            return records;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogService.Write($"Snapshot Engine: no se pudo leer el índice: {ex.Message}", "WARN");
            return [];
        }
    }

    public static SnapshotRecord CreateForBackup(BackupEntry backup, ConsoleConfig console)
    {
        AppPaths.EnsureDirectories();
        var record = new SnapshotRecord
        {
            CreatedLocal = DateTime.Now,
            ConsoleAddress = console.Ip,
            ConsoleName = console.Name,
            TitleId = backup.TitleId,
            TitleName = backup.TitleName,
            SaveName = backup.SaveName,
            Uid = FirstOwnerValue(backup.Owner),
            BackupPath = backup.ImgPath,
            Sha256 = backup.Sha256,
            Size = !string.IsNullOrWhiteSpace(backup.ImgPath) ? SizeResolutionService.GetRealFileSize(backup.ImgPath) : 0,
            RemoteFingerprint = backup.RemoteFingerprint
        };

        var all = Load();
        all.Add(record);
        AtomicWrite(all);
        return record;
    }

    public static int CountFor(string titleId, string saveName, string? uid = null)
        => Load().Count(x => string.Equals(x.TitleId, titleId, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(x.SaveName, saveName, StringComparison.OrdinalIgnoreCase)
                          && (string.IsNullOrWhiteSpace(uid) || GarlicApi.Norm(x.Uid) == GarlicApi.Norm(uid)));

    public static long TotalManifestBytes()
    {
        try { return AppPaths.SnapshotsIndexFile is { Length: > 0 } path && File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static void AtomicWrite(List<SnapshotRecord> records)
    {
        var tmp = AppPaths.SnapshotsIndexFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(records, JsonOptions));
        File.Move(tmp, AppPaths.SnapshotsIndexFile, true);
    }

    private static string FirstOwnerValue(Dictionary<string, object?> owner)
        => new[] { "uid", "account_id", "id", "aid" }
            .Select(k => owner.TryGetValue(k, out var v) ? Convert.ToString(v) ?? "" : "")
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}
