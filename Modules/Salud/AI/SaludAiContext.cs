namespace GarlicSaveMgr.Modules.Salud.Core;

/// <summary>
/// Contexto estable para el futuro analista IA. Esta primera entrega no ejecuta ninguna llamada externa.
/// </summary>
public sealed record SaludAiContext(
    DateTime GeneratedLocal,
    string Summary,
    int HealthScore,
    IReadOnlyList<SaludAnomaly> Anomalies,
    IReadOnlyList<SaludRecommendation> Recommendations,
    SaludStorageStatus Storage,
    SaludBackupStatus Backups,
    SaludSnapshotStatus Snapshots,
    SaludIntegrityStatus Integrity,
    SaludApplicationStatus Application,
    SaludCoverStatus Covers);

public static class SaludAiContextBuilder
{
    public static SaludAiContext Build(SaludReport report) => new(
        report.GeneratedLocal,
        report.SessionSummary,
        report.Health.Score,
        report.Anomalies,
        report.Recommendations,
        report.Storage,
        report.Backups,
        report.Snapshots,
        report.Integrity,
        report.Application,
        report.Covers);
}
