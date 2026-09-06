# Salud — contrato de observabilidad

Salud es el subsistema de observabilidad global de Garlic SaveMgr. Debe detectar problemas del host, módulos, servicios, operaciones y datos sin convertirse en el ejecutor de acciones destructivas.

## HEALTH ≠ DIAGNOSTICS

`HEALTH` responde «¿está funcionando correctamente ahora?». `DIAGNOSTICS` responde «¿qué merece atención o qué ha ocurrido?». Una anomalía diagnóstica no tiene por qué reducir la salud.

No reducen `HEALTH` por sí solos: snapshots `history-only`, historial antiguo, carátulas `NOT_FOUND`, módulos opcionales ausentes o un `Warning` genérico. Cualquier penalización debe venir de un `HealthImpact` explícito y justificado.

Sí pueden reducirla: fallos operativos reales, integridad comprometida, recursos críticos, operaciones atascadas o componentes esenciales fallidos.

## Observabilidad

Debe monitorizar, cuando haya datos disponibles:

- host, dispatcher y excepciones relevantes;
- módulos cargados/ausentes/fallidos;
- operaciones asíncronas;
- cola y actividad de carátulas;
- conexiones y descubrimiento LAN;
- backups, Restore e integridad;
- almacenamiento;
- snapshots, histórico y tendencias.

## Operaciones atascadas

Estados observables: `QUEUED`, `RUNNING`, `COMPLETED`, `FAILED`, `CANCELLED`, `TIMEOUT`, `STALLED`.

Una operación `RUNNING` puede marcarse `STALLED` tras más de 15 segundos sin actividad/progreso observable. Ser lenta no basta para considerarla fallida.

Una carátula `QUEUED` puede marcarse atascada después de 30 segundos sin iniciar.

## Carátulas

El pipeline distingue `QUEUED`, `RUNNING`, `COMPLETED`, `NOT_FOUND`, `FAILED` y `STALLED`. Los errores de portada nunca deben bloquear Backup, Restore o Trash.

## Host

Salud puede detectar heartbeats retrasados cuando vuelve a ejecutarse. Una aplicación completamente bloqueada o terminada no puede autodiagnosticar un crash externo desde el mismo proceso; no debe afirmarse lo contrario sin un supervisor externo.

## No doble contabilización

Un mismo fallo no puede descontarse dos veces por aparecer en fuentes distintas. Las anomalías deben tener identidad e impacto explícitos.
