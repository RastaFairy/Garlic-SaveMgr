# SALUD 2.2 · Observabilidad

SALUD es el subsistema de observabilidad, diagnóstico e inteligencia de Garlic SaveMgr. Sustituye el nombre histórico MOTOR, manteniendo la misma frontera modular: `SaludModule.dll` es independiente mientras no cambie el contrato compartido.

## Responsabilidades

- Health Score determinista y explicable.
- Diagnóstico del estado operativo actual.
- Observación del host, módulos, operaciones asíncronas y errores registrados.
- Monitorización del pipeline de carátulas, incluyendo operaciones atascadas/stale.
- Estado de backups, integridad y almacenamiento.
- Historial y tendencias sin convertir datos históricos en fallos de salud.
- Recomendaciones no destructivas.
- Contexto estructurado para una futura IA, sin llamadas externas en esta revisión.

## Regla HEALTH vs DIAGNOSTICS

`Health` responde a: «¿Está funcionando correctamente el sistema ahora?».

`Diagnostics` responde a: «¿Qué merece atención o qué ha ocurrido?».

Un dato meramente histórico no penaliza `Health`. En particular, un snapshot `history-only` puede mostrarse como información sin reducir el score. La severidad de una anomalía tampoco implica automáticamente impacto en salud: cada anomalía declara explícitamente `HealthImpact`.

## Observabilidad

SALUD consume la telemetría compartida del proceso para identificar: excepciones recientes, operaciones fallidas, operaciones `RUNNING` sin actividad durante más de 15 segundos, estado de módulos y heartbeat del host. También consume el estado de `CoverCacheService` para distinguir carátulas en cola, activas, resueltas, no encontradas y atascadas.

La observabilidad no convierte a SALUD en un ejecutor. SALUD observa, analiza, informa y recomienda. No ejecuta automáticamente acciones destructivas.

## Persistencia

El histórico analítico de SALUD se conserva en `data/Salud/history.json`. La telemetría operativa del proceso vive en memoria y no sustituye el histórico.

## Compilación aislada

```powershell
dotnet build .\Modules\Salud\SaludModule.csproj -c Release
```

Salida esperada: `Modules/Salud/bin/Release/net8.0-windows/GarlicSaveMgr.SaludModule.dll`.
