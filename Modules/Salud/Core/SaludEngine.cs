using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr.Modules.Salud.Core;

public sealed class SaludEngine
{
    private readonly SaludHistoryStore _history;
    private readonly object _historySync = new();
    private DateTime _lastHistoryLocal;
    private string? _lastHistoryFingerprint;
    private static readonly TimeSpan HistorySampleInterval = TimeSpan.FromMinutes(1);

    public SaludEngine(SaludHistoryStore history)
    {
        _history = history;
    }

    public SaludReport Build(
        ConsoleConfig config,
        IReadOnlyList<TitleInfo> titles,
        IReadOnlyList<BackupEntry> backups,
        IReadOnlyList<SnapshotRecord> snapshots,
        IReadOnlyList<ConsoleConnection> connections,
        bool consoleOnline,
        bool configured,
        ApplicationTelemetrySnapshot applicationTelemetry,
        CoverTelemetrySnapshot coverTelemetry)
    {
        var now = DateTime.Now;
        var existingBackups = backups.Count(b => File.Exists(b.ImgPath));
        var missingFiles = backups.Count - existingBackups;
        var withHash = backups.Count(b => !string.IsNullOrWhiteSpace(b.Sha256));
        var withoutHash = backups.Count - withHash;
        var backupBytes = backups.Sum(b => SizeResolutionService.GetRealFileSize(b.ImgPath));

        long totalBytes = 0;
        long freeBytes = 0;
        try
        {
            var root = Path.GetPathRoot(AppPaths.RootDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                totalBytes = drive.TotalSize;
                freeBytes = drive.AvailableFreeSpace;
            }
        }
        catch { }

        var freePercent = totalBytes > 0 ? 100d * freeBytes / totalBytes : (double?)null;
        var backupPercent = totalBytes > 0 ? 100d * backupBytes / totalBytes : (double?)null;
        var backupStatus = new SaludBackupStatus(
            backups.Count,
            backups.Select(b => b.TitleId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            missingFiles,
            withHash,
            withoutHash,
            missingFiles > 0 ? $"{existingBackups} disponibles · {missingFiles} faltantes" : $"{existingBackups} disponibles");

        var linkedSnapshots = snapshots.Count(s => ResolveBackup(s, backups) is not null);
        var historyOnly = snapshots.Count - linkedSnapshots;
        var snapshotStatus = new SaludSnapshotStatus(
            snapshots.Count,
            linkedSnapshots,
            historyOnly,
            snapshots.Count == 0 ? null : snapshots.Max(s => s.CreatedLocal),
            historyOnly > 0 ? $"{historyOnly} solo historial · {linkedSnapshots} vinculados" : $"{linkedSnapshots} vinculados");

        var integrity = new SaludIntegrityStatus(
            backups.Count(b => File.Exists(b.ImgPath) && !string.IsNullOrWhiteSpace(b.Sha256)),
            withoutHash,
            missingFiles,
            withoutHash > 0 || missingFiles > 0 ? "Revisión incompleta" : "Referencias disponibles");

        var storage = new SaludStorageStatus(
            backupBytes,
            freeBytes,
            totalBytes,
            freePercent,
            backupPercent,
            freePercent is null ? "Espacio no disponible" : $"{freePercent:0.#}% libre");

        var active = connections.FirstOrDefault(c =>
            string.Equals(c.Ip, config.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == config.Port);
        var activeUsers = active?.UserIds.Count ?? 0;

        var anomalies = new SaludAnomalyDetector().Detect(
            configured,
            consoleOnline,
            backupStatus,
            snapshotStatus,
            integrity,
            storage,
            connections,
            applicationTelemetry,
            coverTelemetry);

        var history = new SaludHistoryRecord(
            now,
            backupStatus.Count,
            backupBytes,
            snapshotStatus.Count,
            freeBytes,
            totalBytes,
            integrity.ValidReferences,
            integrity.MissingHash,
            integrity.MissingFiles,
            consoleOnline,
            connections.Count);
        AppendHistoryIfMeaningful(history);

        var recommendations = new SaludRecommendationEngine().Build(anomalies, history, _history.ReadRecent(30));
        var applicationStatus = new SaludApplicationStatus(
            applicationTelemetry.StartedLocal,
            applicationTelemetry.LastHeartbeatLocal,
            applicationTelemetry.HeartbeatAge,
            applicationTelemetry.RunningOperations,
            applicationTelemetry.FailedOperations,
            applicationTelemetry.RecentFailedOperations,
            applicationTelemetry.StalledOperations,
            applicationTelemetry.RecentExceptions,
            applicationTelemetry.RecentUnhandledExceptions,
            applicationTelemetry.LoadedModules,
            applicationTelemetry.FailedModules,
            applicationTelemetry.MissingModules,
            $"Host activo · {applicationTelemetry.LoadedModules} módulo(s) cargados · {applicationTelemetry.RunningOperations} operación(es) activas · {applicationTelemetry.RecentExceptions} excepción(es) recientes");
        var health = SaludHealthCalculator.Calculate(anomalies);

        var smartHealthy = !string.IsNullOrWhiteSpace("Activo") && backups.Count >= 0;
        return new SaludReport(
            now,
            health,
            new SaludConsoleStatus(configured, consoleOnline, string.IsNullOrWhiteSpace(config.Ip) ? "Sin configurar" : $"{config.Ip}:{config.Port}", titles.Count, titles.Sum(t => Math.Max(0, t.SlotCount))),
            storage,
            backupStatus,
            snapshotStatus,
            integrity,
            new SaludSmartBackupStatus("Activo · coincidencias seguras", smartHealthy, "Estado informado por el flujo actual"),
            applicationStatus,
            new SaludCoverStatus(coverTelemetry.Known, coverTelemetry.Queued, coverTelemetry.Running, coverTelemetry.Completed, coverTelemetry.NotFound, coverTelemetry.Failed, coverTelemetry.Stalled, coverTelemetry.StalledQueued, $"{coverTelemetry.Completed} encontradas · {coverTelemetry.NotFound} no encontradas · {coverTelemetry.Failed} fallidas · {coverTelemetry.Queued} en cola · {coverTelemetry.Running} activas"),
            anomalies,
            recommendations,
            connections.Count,
            activeUsers,
            $"{connections.Count} consola(s) detectada(s) · {activeUsers} user_id activos");
    }

    private void AppendHistoryIfMeaningful(SaludHistoryRecord current)
    {
        var fingerprint = string.Join("|",
            current.BackupCount,
            current.BackupBytes,
            current.SnapshotCount,
            current.FreeBytes,
            current.TotalBytes,
            current.IntegrityValid,
            current.IntegrityMissingHash,
            current.IntegrityMissingFiles,
            current.ConsoleOnline,
            current.ConnectionCount);

        lock (_historySync)
        {
            var now = DateTime.Now;
            var changed = !string.Equals(_lastHistoryFingerprint, fingerprint, StringComparison.Ordinal);
            if (!changed && now - _lastHistoryLocal < HistorySampleInterval) return;
            _history.Append(current);
            _lastHistoryFingerprint = fingerprint;
            _lastHistoryLocal = now;
        }
    }

    public static BackupEntry? ResolveBackup(SnapshotRecord snapshot, IReadOnlyList<BackupEntry> backups)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.BackupPath))
        {
            var snapshotPath = TryFull(snapshot.BackupPath);
            var exact = backups.FirstOrDefault(b =>
                string.Equals(snapshotPath, TryFull(b.ImgPath), StringComparison.OrdinalIgnoreCase) &&
                File.Exists(b.ImgPath));
            if (exact is not null) return exact;
        }

        var candidates = backups.Where(b =>
            string.Equals(b.TitleId, snapshot.TitleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(b.SaveName, snapshot.SaveName, StringComparison.OrdinalIgnoreCase) &&
            OwnerMatches(b.Owner, snapshot.Uid) &&
            File.Exists(b.ImgPath)).ToList();

        if (!string.IsNullOrWhiteSpace(snapshot.Sha256))
        {
            var bySha = candidates.FirstOrDefault(b => string.Equals(b.Sha256, snapshot.Sha256, StringComparison.OrdinalIgnoreCase));
            if (bySha is not null) return bySha;
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static string? TryFull(string? path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path); }
        catch { return null; }
    }

    private static bool OwnerMatches(Dictionary<string, object?> owner, string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return true;
        return owner.Values.Any(v => string.Equals(Convert.ToString(v), uid, StringComparison.OrdinalIgnoreCase));
    }
}
