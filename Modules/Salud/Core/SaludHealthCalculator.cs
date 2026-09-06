namespace GarlicSaveMgr.Modules.Salud.Core;

public static class SaludHealthCalculator
{
    public static SaludHealth Calculate(IReadOnlyList<SaludAnomaly> anomalies)
    {
        var impacts = anomalies
            .Where(a => a.HealthImpact > 0)
            .OrderByDescending(a => a.HealthImpact)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var score = Math.Clamp(100 - impacts.Sum(a => a.HealthImpact), 0, 100);
        var severity = score >= 90 ? SaludSeverity.Good : score >= 70 ? SaludSeverity.Info : score >= 45 ? SaludSeverity.Warning : SaludSeverity.Critical;
        var label = severity switch
        {
            SaludSeverity.Good => "BUENA",
            SaludSeverity.Info => "ESTABLE",
            SaludSeverity.Warning => "ATENCIÓN",
            _ => "CRÍTICA"
        };
        var factors = impacts
            .Select(a => a.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        if (factors.Length == 0)
            factors = ["No se han detectado condiciones operativas relevantes"];

        return new SaludHealth(score, severity, label, factors);
    }
}
