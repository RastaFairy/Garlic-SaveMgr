namespace GarlicSaveMgr.Modules.Salud.Core;

public enum SaludSeverity
{
    Info,
    Good,
    Warning,
    Critical
}

public sealed record SaludHealth(
    int Score,
    SaludSeverity Severity,
    string Label,
    IReadOnlyList<string> Factors);

public sealed record SaludConsoleStatus(
    bool Configured,
    bool Online,
    string Address,
    int Titles,
    int Slots);

public sealed record SaludStorageStatus(
    long BackupBytes,
    long FreeBytes,
    long TotalBytes,
    double? FreePercent,
    double? BackupPercentOfDisk,
    string Summary);

public sealed record SaludBackupStatus(
    int Count,
    int Games,
    int MissingFiles,
    int WithHash,
    int WithoutHash,
    string Summary);

public sealed record SaludSnapshotStatus(
    int Count,
    int Linked,
    int HistoryOnly,
    DateTime? LatestLocal,
    string Summary);

public sealed record SaludIntegrityStatus(
    int ValidReferences,
    int MissingHash,
    int MissingFiles,
    string Summary);

public sealed record SaludSmartBackupStatus(
    string State,
    bool Healthy,
    string Summary);

public sealed record SaludAnomaly(
    string Id,
    SaludSeverity Severity,
    string Title,
    string Description,
    string Recommendation,
    int HealthImpact);

public sealed record SaludRecommendation(
    string Id,
    SaludSeverity Severity,
    string Title,
    string Explanation,
    string Target);

public sealed record SaludApplicationStatus(
    DateTime StartedLocal,
    DateTime LastHeartbeatLocal,
    TimeSpan HeartbeatAge,
    int RunningOperations,
    int FailedOperations,
    int RecentFailedOperations,
    int StalledOperations,
    int RecentExceptions,
    int RecentUnhandledExceptions,
    int LoadedModules,
    int FailedModules,
    int MissingModules,
    string Summary);

public sealed record SaludCoverStatus(
    int Known,
    int Queued,
    int Running,
    int Completed,
    int NotFound,
    int Failed,
    int Stalled,
    int StalledQueued,
    string Summary);

public sealed record SaludReport(
    DateTime GeneratedLocal,
    SaludHealth Health,
    SaludConsoleStatus Console,
    SaludStorageStatus Storage,
    SaludBackupStatus Backups,
    SaludSnapshotStatus Snapshots,
    SaludIntegrityStatus Integrity,
    SaludSmartBackupStatus SmartBackup,
    SaludApplicationStatus Application,
    SaludCoverStatus Covers,
    IReadOnlyList<SaludAnomaly> Anomalies,
    IReadOnlyList<SaludRecommendation> Recommendations,
    int Connections,
    int ActiveUserIds,
    string SessionSummary);
