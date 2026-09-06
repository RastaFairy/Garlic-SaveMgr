using System.Globalization;
using System.Text;
using System.Text.Json;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

/// <summary>Decisiones conservadoras para evitar backups idénticos.</summary>
public static class SmartBackupService
{
    private static readonly string[] TimestampKeys = ["modified", "updated_at", "updated", "mtime", "timestamp", "save_time"];
    private static readonly string[] SizeKeys = ["size", "size_bytes", "filesize", "file_size", "tamano"];

    public static string RemoteFingerprint(JsonElement save)
    {
        var size = First(save, SizeKeys);
        var time = First(save, TimestampKeys);
        if (string.IsNullOrWhiteSpace(size) || string.IsNullOrWhiteSpace(time)) return "";
        return $"size={Normalize(size)}|time={Normalize(time)}";
    }

    public static SmartBackupDecision Decide(JsonElement save, BackupEntry? latest)
    {
        if (latest is null) return new(false, "No existe una copia previa para este slot.");
        var remote = RemoteFingerprint(save);
        if (string.IsNullOrWhiteSpace(remote)) return new(false, "Garlic no expone una huella remota suficientemente fiable; se creará la copia.");
        if (string.IsNullOrWhiteSpace(latest.RemoteFingerprint)) return new(false, "La copia anterior no tiene huella remota; se creará una nueva copia.");
        if (string.Equals(remote, latest.RemoteFingerprint, StringComparison.Ordinal))
            return new(true, "El savedata no presenta cambios según tamaño y marca temporal remotos.");
        return new(false, "La huella remota ha cambiado.");
    }

    private static BackupEntry? FindLatest(string titleId, string saveName, string uid)
        => BackupService.LoadLocalBackups()
            .Where(x => string.Equals(x.TitleId, titleId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(x.SaveName, saveName, StringComparison.OrdinalIgnoreCase)
                     && OwnerMatches(x.Owner, uid))
            .OrderByDescending(x => ParseDate(x.Date))
            .FirstOrDefault();

    public static BackupEntry? FindLatestFor(string titleId, string saveName, string uid) => FindLatest(titleId, saveName, uid);

    private static bool OwnerMatches(Dictionary<string, object?> owner, string uid)
    {
        var expected = GarlicApi.Norm(uid);
        return new[] { "uid", "account_id", "id", "aid" }
            .Select(k => owner.TryGetValue(k, out var v) ? Convert.ToString(v) ?? "" : "")
            .Any(v => !string.IsNullOrWhiteSpace(v) && GarlicApi.Norm(v) == expected);
    }

    private static string First(JsonElement root, IEnumerable<string> names)
    {
        foreach (var key in names)
        {
            var value = GarlicApi.GetString(root, key);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static DateTime ParseDate(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : DateTime.MinValue;
}
