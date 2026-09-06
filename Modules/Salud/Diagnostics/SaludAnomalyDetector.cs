using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr.Modules.Salud.Core;

public sealed class SaludAnomalyDetector
{
    public IReadOnlyList<SaludAnomaly> Detect(
        bool configured,
        bool online,
        SaludBackupStatus backups,
        SaludSnapshotStatus snapshots,
        SaludIntegrityStatus integrity,
        SaludStorageStatus storage,
        IReadOnlyList<ConsoleConnection> connections,
        ApplicationTelemetrySnapshot application,
        CoverTelemetrySnapshot covers)
    {
        var result = new List<SaludAnomaly>();

        if (configured && !online)
            result.Add(new("console-offline", SaludSeverity.Warning, "Consola sin conexión", "La consola configurada no responde como conexión Garlic activa.", "Revisar la conexión antes de ejecutar operaciones que dependan de la PS5.", 15));

        if (backups.MissingFiles > 0)
            result.Add(new("missing-backups", SaludSeverity.Warning, "Backups físicos ausentes", $"Hay {backups.MissingFiles} referencia(s) de backup cuyo fichero físico no está disponible.", "Revisar el listado de backups antes de limpiar historial.", Math.Min(20, backups.MissingFiles * 5)));

        if (snapshots.HistoryOnly > 0)
            result.Add(new("history-only-snapshots", SaludSeverity.Info, "Snapshots históricos", $"Hay {snapshots.HistoryOnly} snapshot(s) sin un backup activo resoluble. Esto es historial y no reduce la salud por sí mismo.", "Revisar esos snapshots solo si se desea auditar el historial.", 0));

        if (integrity.MissingHash > 0)
            result.Add(new("missing-hash", SaludSeverity.Info, "Integridad incompleta", $"Hay {integrity.MissingHash} backup(s) sin SHA-256 de referencia.", "Ejecutar una verificación de integridad cuando Seguridad esté disponible.", 0));

        if (storage.FreePercent is < 10)
            result.Add(new("storage-critical", SaludSeverity.Critical, "Espacio libre crítico", $"Solo queda {storage.FreePercent:0.#}% de espacio libre en la unidad de la aplicación.", "Liberar espacio antes de continuar acumulando backups.", 20));
        else if (storage.FreePercent is < 20)
            result.Add(new("storage-low", SaludSeverity.Warning, "Espacio libre bajo", $"Queda {storage.FreePercent:0.#}% de espacio libre.", "Revisar el crecimiento de backups y el espacio disponible.", 10));

        if (connections.Count > 1)
            result.Add(new("multiple-connections", SaludSeverity.Info, "Múltiples consolas detectadas", $"Se han detectado {connections.Count} conexiones en el inventario de SALUD.", "Comprobar que la conexión activa coincide con la consola configurada.", 0));

        var recentNonUpdaterFailures = Math.Max(0, application.RecentFailedOperations - application.RecentFailedUpdateChecks);
        if (recentNonUpdaterFailures > 0)
            result.Add(new("operation-failures", SaludSeverity.Warning, "Operaciones fallidas recientes", $"Se han registrado {recentNonUpdaterFailures} operación(es) fallida(s) fuera del actualizador durante la sesión reciente.", "Revisar el detalle de la operación y el log asociado.", Math.Min(20, recentNonUpdaterFailures * 5)));

        if (application.RecentFailedUpdateChecks > 0)
            result.Add(new("updater-check-failures", SaludSeverity.Warning, "Actualizador: comprobaciones fallidas", $"El actualizador ha registrado {application.RecentFailedUpdateChecks} comprobación(es) contra GitHub fallida(s) durante los últimos 10 minutos.", "Revisar el estado de GitHub, el rate limit y la última respuesta cacheada del actualizador.", Math.Min(20, application.RecentFailedUpdateChecks * 5)));

        if (application.StalledOperations > 0)
            result.Add(new("operation-stalled", SaludSeverity.Critical, "Operación atascada", $"Hay {application.StalledOperations} operación(es) en estado RUNNING sin actividad durante más de 15 segundos.", "Revisar el componente afectado y cancelar/reiniciar la operación solo desde su flujo autorizado.", Math.Min(25, application.StalledOperations * 10)));

        if (application.RecentUnhandledExceptions > 0)
            result.Add(new("unhandled-exception", SaludSeverity.Critical, "Excepciones no controladas", $"Se han registrado {application.RecentUnhandledExceptions} excepción(es) no controlada(s) en la sesión reciente.", "Revisar el log de la aplicación para identificar el componente responsable.", Math.Min(20, application.RecentUnhandledExceptions * 10)));

        if (application.HeartbeatAge > TimeSpan.FromSeconds(10))
            result.Add(new("host-heartbeat-stale", SaludSeverity.Warning, "Heartbeat del host retrasado", $"El último heartbeat del host tiene {application.HeartbeatAge.TotalSeconds:0} s.", "Comprobar si el dispatcher o el host estuvieron bloqueados.", 15));

        if (application.FailedModules > 0)
            result.Add(new("module-load-failures", SaludSeverity.Warning, "Módulos con errores de carga", $"Hay {application.FailedModules} módulo(s) que no se han podido cargar correctamente.", "Revisar el log de carga del módulo afectado.", 5));
        if (application.MissingModules > 0)
            result.Add(new("module-missing", SaludSeverity.Info, "Módulos opcionales no disponibles", $"Hay {application.MissingModules} módulo(s) recomendado(s) ausente(s). La aplicación básica puede seguir funcionando.", "Revisar los módulos instalados solo si necesitas sus funciones avanzadas.", 0));

        if (covers.Stalled > 0)
        {
            var detail = covers.StalledQueued > 0
                ? $"Hay {covers.Stalled} carátula(s) atascada(s); {covers.StalledQueued} llevan más de 30 segundos en cola."
                : $"Hay {covers.Stalled} carátula(s) en ejecución sin actividad durante más de 15 segundos.";
            result.Add(new("covers-stalled", SaludSeverity.Warning, "Carátulas atascadas", detail, "Revisar el pipeline de carátulas; un fallo de portada no debe bloquear backups ni restore.", Math.Min(10, covers.Stalled * 5)));
        }

        if (covers.NotFound > 0 || covers.Failed > 0)
        {
            var parts = new List<string>();
            if (covers.NotFound > 0) parts.Add($"{covers.NotFound} no encontrada(s)");
            if (covers.Failed > 0) parts.Add($"{covers.Failed} fallida(s)");
            result.Add(new("covers-failed", SaludSeverity.Info, "Carátulas no resueltas", $"Hay {string.Join(" · ", parts)} en el pipeline de carátulas. La falta de imagen es diagnóstica y no reduce HEALTH.", "Reintentar la carga de portadas cuando las fuentes estén disponibles; no debe bloquear Backup/Restore/Trash.", 0));
        }

        return result;
    }
}
