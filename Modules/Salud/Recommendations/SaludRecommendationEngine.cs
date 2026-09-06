namespace GarlicSaveMgr.Modules.Salud.Core;

public sealed class SaludRecommendationEngine
{
    public IReadOnlyList<SaludRecommendation> Build(
        IReadOnlyList<SaludAnomaly> anomalies,
        SaludHistoryRecord current,
        IReadOnlyList<SaludHistoryRecord> recent)
    {
        var result = anomalies
            .Where(a => a.Severity is SaludSeverity.Warning or SaludSeverity.Critical)
            .Take(3)
            .Select(a => new SaludRecommendation(a.Id, a.Severity, a.Title, a.Recommendation, "Diagnóstico"))
            .ToList();

        if (recent.Count >= 2)
        {
            var previous = recent[^2];
            var deltaBytes = current.BackupBytes - previous.BackupBytes;
            if (deltaBytes > 0)
            {
                result.Add(new SaludRecommendation(
                    "storage-growth",
                    SaludSeverity.Info,
                    "El almacenamiento está creciendo",
                    $"El volumen de backups ha aumentado {FormatBytes(deltaBytes)} desde la observación anterior.",
                    "Tendencias"));
            }
        }

        return result
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(5)
            .ToArray();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        foreach (var unit in new[] { "KB", "MB", "GB", "TB" })
        {
            value /= 1024;
            if (value < 1024) return $"{value:0.0} {unit}";
        }
        return $"{value:0.0} PB";
    }
}
