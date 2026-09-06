using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Services;
using GarlicSaveMgr.Modules.Salud.Core;
using Xunit;

namespace GarlicSaveMgr.Tests;

public sealed class SaludHealthTests
{
    [Fact]
    public void HistoryOnlySnapshot_DoesNotReduceHealth()
    {
        var anomalies = new[]
        {
            new SaludAnomaly("history-only", SaludSeverity.Info, "Snapshots históricos", "Historial sin backup activo.", "Nada que hacer.", 0)
        };

        var health = SaludHealthCalculator.Calculate(anomalies);

        Assert.Equal(100, health.Score);
        Assert.Equal("BUENA", health.Label);
    }

    [Fact]
    public void WarningWithoutHealthImpact_DoesNotReduceHealth()
    {
        var anomalies = new[]
        {
            new SaludAnomaly("cover-missing", SaludSeverity.Warning, "Carátula no encontrada", "No se encontró una portada.", "Reintentar más tarde.", 0)
        };

        var health = SaludHealthCalculator.Calculate(anomalies);

        Assert.Equal(100, health.Score);
    }

    [Fact]
    public void UnresolvedCovers_DoNotReduceHealth()
    {
        var anomalies = new[]
        {
            new SaludAnomaly("covers-failed", SaludSeverity.Info, "Carátulas no resueltas", "3 no encontradas.", "Reintentar.", 0)
        };

        var health = SaludHealthCalculator.Calculate(anomalies);

        Assert.Equal(100, health.Score);
        Assert.Equal("BUENA", health.Label);
    }

    [Fact]
    public void StalledOperation_ReducesHealthByExplicitImpactOnly()
    {
        var anomalies = new[]
        {
            new SaludAnomaly("stalled", SaludSeverity.Critical, "Operación atascada", "Sin actividad.", "Revisar operación.", 10)
        };

        var health = SaludHealthCalculator.Calculate(anomalies);

        Assert.Equal(90, health.Score);
    }
    [Fact]
    public void UpdaterFailures_AreScoredThroughTheirSpecificAnomaly()
    {
        var anomalies = new[]
        {
            new SaludAnomaly("updater-check-failures", SaludSeverity.Warning, "Actualizador: comprobaciones fallidas", "2 comprobaciones contra GitHub han fallado.", "Revisar GitHub/rate limit.", 10)
        };

        var health = SaludHealthCalculator.Calculate(anomalies);

        Assert.Equal(90, health.Score);
    }

    [Fact]
    public void UpdaterFailures_AreSeparatedFromGenericOperationFailures()
    {
        var now = DateTime.Now;
        var telemetry = new ApplicationTelemetrySnapshot(
            now.AddMinutes(-1),
            now,
            TimeSpan.Zero,
            0,
            3,
            3,
            2,
            0,
            0,
            0,
            Array.Empty<TelemetryModule>(),
            new[]
            {
                new TelemetryOperation(Guid.NewGuid(), "UPDATE_CHECK", "Comprobación de actualización", now.AddMinutes(-1), now, "FAILED", null, "GitHub rate limit", 1),
                new TelemetryOperation(Guid.NewGuid(), "UPDATE_CHECK", "Comprobación de actualización", now.AddMinutes(-1), now, "FAILED", null, "GitHub rate limit", 2),
                new TelemetryOperation(Guid.NewGuid(), "BACKUP", "Backup", now.AddMinutes(-1), now, "FAILED", null, "I/O", 3)
            },
            Array.Empty<TelemetryException>());

        var anomalies = new SaludAnomalyDetector().Detect(
            configured: true,
            online: true,
            backups: new SaludBackupStatus(0, 0, 0, 0, 0, ""),
            snapshots: new SaludSnapshotStatus(0, 0, 0, null, ""),
            integrity: new SaludIntegrityStatus(0, 0, 0, ""),
            storage: new SaludStorageStatus(0, 100, 1000, 10, 0, ""),
            connections: Array.Empty<GarlicSaveMgr.Models.ConsoleConnection>(),
            application: telemetry,
            covers: new CoverTelemetrySnapshot(0, 0, 0, 0, 0, 0, 0, 0));

        Assert.Contains(anomalies, x => x.Id == "updater-check-failures" && x.HealthImpact == 10);
        Assert.Contains(anomalies, x => x.Id == "operation-failures" && x.HealthImpact == 5);
        Assert.DoesNotContain(anomalies, x => x.Id == "operation-failures" && x.HealthImpact == 15);
    }

}
