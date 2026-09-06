# Changelog — Garlic SaveMgr

Este changelog resume las revisiones publicadas y las correcciones funcionales relevantes. La documentación canónica de reglas se mantiene en `PROJECT.md`.

# v6.8.7.50 — selector de consolas legible y consola activa más reciente
- El combo de consolas mostraba el nombre del tipo (`GarlicSaveMgr.Models.Co…`) en lugar de la IP. Reproducido en vivo contra el EXE publicado 6.8.7.49 con `DisplayMemberPath` presente en el BAML: la caja de selección de un `ComboBox` con plantilla personalizada no aplicó el `DisplayMemberPath`. Fix: `ItemTemplate` explícito con `Binding DisplayName` (patrón ya usado por el combo de temas de Ajustes) y `ConsoleConnection.ToString()` → `DisplayName` como red de seguridad.
- `EnsureIp()`: con varios perfiles históricos (p. ej. 192.168.1.211 y 192.168.1.240), la recuperación de consola activa pasa de "primera por orden de IP" a "vista más recientemente" (`LastSeenLocal`).
- `SALUD-2.2-OBSERVABILIDAD.md`: la sección "Resolución de carátulas" describía PlayStation Catalog v2 como canónico y negaba el uso de Chihiro, contradictorio con el código desde 6.8.7.45; corregida al recorrido Chihiro `gb/en` → `us/en` → `es/es`. Solo documentación.
- Comentario de timeout en `CoverCacheService` precisado; sin cambio funcional.

# v6.8.7.49 — el aviso de consola no válida espera al resultado de la conexión inicial
- Causa raíz: `TrashView` refresca el ámbito PS5 al entrar en el árbol visual; su `EnsureIp()` mostraba "No hay una consola válida conectada" durante el arranque, antes de que la detección de hasta 1.275 direcciones entregara resultado y con la configuración todavía sin consola.
- Nueva regla `InitialConnectionGate` (`GarlicSaveMgr/Infrastructure/InitialConnectionGate.cs`): el aviso queda suprimido desde la construcción de la ventana hasta que termina `ConnectOrDiscoverAsync()`; cada intento de conexión/detección (arranque, ajustes, perfil) abre un ámbito que re-suprime el aviso mientras vive y lo libera al terminar, incluso con excepción.
- Durante esa ventana `EnsureIp()` retorna `false` en silencio; el aviso vuelve a estar disponible en cuanto la conexión termina sin consola utilizable.
- Regresión nueva `InitialConnectionGateTests` (supresión desde construcción, ámbito de reconexión, dispose idempotente).

# v6.8.7.48 — fallback de región Chihiro para carátulas region-locked
- `CoverCacheService` recorre `gb/en` → `us/en` → `es/es` en el endpoint directo `titlecontainer/{region}/999/{TitleId}_00/image`. Verificado contra el endpoint real: títulos como PPSA-01924/01736/02474/04716/03351/14251 devuelven 404 en gb/en pero imagen válida en us/en (causa de las 8 no encontradas sobre 25).
- `NOT_FOUND` solo se declara tras agotar las regiones; la región ganadora queda memorizada por sesión para que los reintentos sigan costando una única petición; los timeouts remotos mantienen el contrato de 6.8.7.47 (`FAILED`/`remote-timeout`, sin iterar más regiones).
- `EnsureIp()` deja de borrar la IP configurada antes del aviso de consola no válida.
- Limpieza de código muerto en `CoverCacheService` (`IsUsableImageUrl`, `FormatTitleName`).

# v6.8.7.47 — CoverCacheService syntax fix
- Fixed missing brace in CoverCacheService.cs introduced during timeout handling.
- No functional change to source selection or SALUD telemetry.

# v6.8.7.45 — PlayStation Catalog endpoint + console-state fix
- Corrige la ruta de detalle multimedia por `Title ID` a `/catalog/v2/titles/{TitleId}_00/concepts`.
- Mantiene PlayStation Catalog v2 como única fuente de resolución y usa únicamente el CDN de imagen que devuelve el catálogo para descargar los bytes.
- Añade recuperación de la consola runtime marcada como Garlic activa en `EnsureIp()` para evitar el falso diálogo de consola inválida.
- Ajusta los tests de `CoverCacheService` a la ruta `/concepts`.

# v6.8.7.43 — Official PlayStation Catalog single-source covers
- Sustituye el índice KytyPS5 por el catálogo oficial de PlayStation Catalog v2 consultado directamente por Title ID.
- Usa `PPSA######_00`/`CUSA######_00` y extrae imágenes desde `media.images[]`.
- Mantiene caché local, deduplicación, concurrencia limitada y descarga desde el CDN oficial devuelto por el catálogo.
- Regresión de pruebas alineada con una única fuente de resolución.

# v6.8.7.43 — KytyPS5 single-source compilation fix
- Restaura `IsUsableImageUrl` en `CoverCacheService`, requerido por la resolución del índice único KytyPS5.
- Corrige el aviso CS8604 evitando asignar una portada nula al diccionario.
- No cambia la arquitectura de fuente única ni la lógica de SALUD/telemetría.
- Base funcional: v6.8.7.41; pendiente de validación Windows mediante `build.ps1`.

# v6.8.7.43 — Fuente única de carátulas por Title ID
- Se sustituye el resolver SerialStation por el índice único KytyPS5 `compat-index.json`.
- Se corrige el manejo del timeout/errores del índice para que una fuente caída produzca `NOT_FOUND` sin excepción de UI.
- Resolución directa `TitleId -> cover URL`, con una sola descarga de índice compartida entre todos los títulos.
- Se elimina el acceso remoto del pipeline de carátulas a SerialStation, PlayStation GraphQL, Chihiro, Prospero y catálogos secundarios.

# v6.8.7.43 — SerialStation timeout regression fix
- Corrige la regresión del test `EnsureCoverAsync_BoundsRemoteResolutionWhenIndexHangs`: el timeout ahora se prueba sobre la única fuente remota real, SerialStation.
- El test verifica `SerialStationCalls=1` e `IndexCalls=0`, alineado con la arquitectura de fuente única.
- Se mantiene el timeout global de resolución en 5 s.
- Sin cambios funcionales en SALUD, telemetría ni ACTUALIZADOR.

# v6.8.7.39 — SerialStation covers / regression fixes
- CoverCacheService usa SerialStation como única fuente remota de carátulas.
- Resolución por Title ID en formato canónico, sin segunda consulta con variantes.
- Timeout de resolución remota de 5 s para mantener la precarga acotada.
- Regresión de tests actualizada: sin dependencia de Kyty/GraphQL/Chihiro/Prospero.

# v6.8.7.32 — portadas fiables + SALUD/telemetría
- Mantiene caché local y deduplicación por `TitleId`.
- Prioriza el índice público Kyty cuando ya contiene la relación `titleId -> cover` con URL directa del CDN de PlayStation.
- Añade catálogo `andshrew/PlayStation-Titles` para resolver `TitleId -> ContentId`, cacheado 7 días.
- Consulta la imagen oficial de PlayStation Store/Chihiro cuando Kyty no tiene entrada.

# v6.8.7.29 — Carátulas no intrusivas y resiliencia reforzada del payload
- La carga de carátulas usa una cola compartida de máximo 2 trabajos y cede inmediatamente al iniciar cualquier operación de usuario.
- Al terminar la operación, la carga se reanuda contra el estado actual; la caché de disco/memoria evita repetir trabajo ya completado.
- La resolución remota de cada TitleId tiene una ventana total corta y timeouts acotados por fuente.
- Se elimina el logging de éxito por cada carátula para no bombardear el dispatcher de la UI.
