# Garlic SaveMgr — contrato canónico del proyecto

## Estado

- **Revisión activa:** 6.8.7.50
- **Versión de producto:** 6.8.7
- **Assembly/FileVersion:** 6.8.7.0
- **Framework:** .NET 8 / WPF
- **Target:** Windows x64
- **Arquitectura:** host estable + módulos DLL autónomos

Este documento es la referencia funcional actual. Los documentos de revisión antiguos son históricos y no deben utilizarse para recuperar comportamientos descartados.

## Regla de evolución

Los cambios deben ser mínimos, localizados y compatibles con el contrato existente. Una modificación aislada de un módulo debe limitarse a su `*Module.dll` mientras no cambie `IModuleHostContext`, `IExecutableModule`, `ModuleEntry`, `ModuleManager` ni servicios core compartidos.

No se debe recompilar o modificar el host por cambios visuales o internos de un módulo que no afecten al contrato.

## Host y módulos

```text
Garlic_SaveMgr.exe
 └─ ModuleManager
     ├─ SecurityModule.dll
     ├─ TrashModule.dll
     ├─ SaludModule.dll
     └─ UpdaterModule.dll
```

Un módulo ausente, inválido o incompatible se degrada sin impedir el arranque de las funciones nucleares cuando no sean esenciales para ellas.

## Contrato funcional

### Backup

`GUARDAR COPIA` descarga el savedata, genera el `.img` y metadata, registra integridad cuando corresponde y crea el Snapshot histórico.

**No crea ni refresca ninguna Papelera.**

### Papelera PS5 / Papelera PC

Son almacenes completamente independientes:

```text
PS5 ACTIVE ──eliminar de consola──> PS5 TRASH

PC ACTIVE ──eliminar localmente──> PC TRASH
```

Un evento de backup activo nunca debe refrescar o reconstruir una Papelera.

### Eventos

- `backups-changed`: backups activos.
- `ps5-trash-changed`: Papelera PS5.
- `pc-trash-changed`: Papelera PC.
- `connections-changed`: conectividad.
- `selection-changed`: selección.
- `theme-changed`: tema.
- `config-changed`: configuración.

No reintroducir un evento genérico `trash-changed` para enrutamiento ambiguo.

### Restore

Restore valida el perfil cuando corresponde y ejecuta la operación mediante los servicios core. No mueve automáticamente el backup activo a Trash.

### SHA-256

Las verificaciones de integridad deben ejecutarse fuera del hilo UI. Un hash ausente no equivale automáticamente a corrupción; una discrepancia real de hash sí es una condición de integridad.

### Tamaños y snapshots

El tamaño de un backup físico se obtiene del archivo `.img` mediante `FileInfo.Length`.

Un Snapshot es un **manifest/historial**. No representa una segunda copia física y no debe sumarse como almacenamiento adicional.

## Smart Backup

La equivalencia debe demostrarse de forma segura. Si la identidad o el estado son ambiguos, se copia.

## Descubrimiento LAN

Las 1.275 direcciones del espacio de descubrimiento constituyen una fase lógica única. La concurrencia debe estar controlada; no se deben crear indiscriminadamente 1.275 conexiones Garlic simultáneas.

## Carátulas

Las carátulas son una capa visual secundaria:

- caché primero;
- resolución por TitleId;
- deduplicación por TitleId;
- publicación individual;
- cancelación y `await` antes de `Dispose`;
- ningún fallo de portada bloquea Backup/Restore/Trash.

Estados observables:

```text
QUEUED → RUNNING → COMPLETED
                  ├→ NOT_FOUND
                  ├→ FAILED
                  └→ STALLED
```

Una operación `RUNNING` solo debe declararse `STALLED` cuando deja de registrar actividad/progreso durante el umbral definido por Salud.

## Salud

Salud es el subsistema de observabilidad global.

Debe poder observar:

- host/dispatcher;
- módulos;
- errores y excepciones relevantes;
- operaciones de fondo;
- carátulas y colas atascadas;
- conexiones/LAN;
- almacenamiento;
- integridad;
- backups/snapshots/histórico;
- tendencias y recomendaciones.

### HEALTH frente a DIAGNOSTICS

`HEALTH` mide el funcionamiento operativo actual.

`DIAGNOSTICS` contiene información que merece atención, aunque no sea un fallo.

Por tanto, **no deben penalizar `HEALTH` por sí solos**:

- snapshots `history-only`;
- historial antiguo;
- carátulas `NOT_FOUND`;
- ausencia de módulos opcionales;
- un `Warning` genérico.

Toda anomalía que pueda modificar el score debe tener `HealthImpact` explícito.

Un bloqueo real, una tarea `STALLED`, un fallo operativo o una condición crítica de recursos sí puede reducir la salud cuando esté justificado.

### Límites de observabilidad

Una aplicación completamente bloqueada o terminada no puede autodiagnosticarse desde dentro del mismo proceso. Salud puede detectar heartbeats retrasados cuando vuelve a ejecutarse; no debe afirmar detección externa de un crash sin un supervisor externo.

### Historial de Salud

El histórico de Salud es telemetría de sesión/histórica y no debe reinterpretarse automáticamente como estado operativo instantáneo.

## Seguridad y automatización

Salud observa, analiza, informa y recomienda. No ejecuta automáticamente operaciones destructivas.

Las futuras automatizaciones deben seguir:

```text
Salud → propuesta → confirmación del usuario → OperationRunner
```

## Temas

El motor de temas utiliza el esquema vigente y `Default.xml` como fallback. Los temas son declarativos; no deben introducir código o XAML arbitrario. No mutar el `Color` de un `SolidColorBrush` existente: sustituir el recurso por un brush nuevo.

## Actualizador

`UpdaterModule.dll` consulta releases de GitHub, compara la versión completa y descarga el asset de actualización previsto. La sustitución del ejecutable se realiza mediante un mecanismo temporal porque el host está en ejecución.

## Regla de validación

Nunca declarar `PASS`, `build OK` o `tests OK` sin ejecución real. Si el SDK no está disponible en el entorno de trabajo, debe indicarse como limitación.
