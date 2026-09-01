# Garlic SaveMgr — Changelog

## v6.8 — 2026-09-01

### Public release
- Primera versión pública de la nueva implementación C#/.NET 8 + WPF.
- La versión pública se identifica simplemente como `v6.8`; los sufijos internos de desarrollo no forman parte del producto publicado.
- `AssemblyVersion` y `FileVersion`: `6.8.0.0`.
- Target: Windows x64.

### Interfaz y experiencia de usuario
- Nueva interfaz moderna en tema claro.
- Selector persistente **Simple / Detallada**.
- Modo Simple centrado en biblioteca: carátula + título.
- Modo Detallado con información técnica y actividad en tiempo real.
- Pestañas Copia de seguridad y Restaurar con estilos y geometría unificados.
- Restauración agrupada por juego/Título ID en modo Simple.
- Tarjetas de biblioteca con cuatro esquinas redondeadas y proporciones coherentes.
- Controles inferiores responsive.
- Selector de perfil y ventana de Ajustes con estilo propio de la aplicación.
- Icono Garlic integrado en la ventana y en el ejecutable.
- Eliminación del UID y columnas técnicas de la vista Simple.
- Estados mostrados mediante texto y color.
- Notificación visual/sonora al finalizar operaciones largas.

### Biblioteca, metadata y carátulas
- Resolución de nombres por Title ID con fuentes públicas.
- Caché local de metadata.
- Resolución de carátulas a partir del Title ID.
- Caché local de carátulas en `covers/`.
- Descarga de carátulas en segundo plano para no bloquear el escaneo.
- Vista Simple consistente entre backup y restore.
- Búsqueda y filtrado.
- Ordenación en las tablas técnicas.

### Backup y restauración
- Backup PS5 → PC de imágenes `.img`.
- Sidecar JSON para metadatos asociados.
- Tamaño de copia visible.
- SHA-256 de las copias descargadas.
- Persistencia del hash en metadata.
- Exportación de copias seleccionadas a ZIP.
- Protección frente a colisiones de nombres mediante timestamps con milisegundos.
- Restauración PC → PS5 con validación del perfil de destino.
- Eliminación de saves de la consola con confirmación.
- Eliminación de copias locales.
- Cancelación controlada mediante `CancellationToken`.
- Protección frente a archivos `.img` parciales cuando una transferencia se cancela.

### Consolas y arranque
- Perfiles para múltiples consolas.
- Detección automática de consola en la red local.
- Comprobación rápida del servicio Garlic.
- La ventana principal abre sin quedar bloqueada por la caché del payload.
- La caché del payload se ejecuta en segundo plano.
- El log muestra una bienvenida con la versión de la aplicación.

### Garlic y payload
- Detección de la versión de Garlic en ejecución mediante el HTML servido en `/`.
- La extracción se limita al bloque `<nav>` para reducir falsos positivos.
- Fallback a `/api/status` y a la cabecera `Server` cuando el HTML no expone una versión reconocible.
- Consulta de catálogos PLDMGR para conocer la última versión disponible.
- Comparación independiente entre:
  - Garlic en ejecución;
  - payload cacheado;
  - última versión disponible.
- Caché local del payload en `payload_cache/`.
- Descarga mediante archivo temporal antes de promoverlo a la caché final.
- Verificación SHA-256 del payload.
- Carga del payload público mediante `elfldr` cuando el usuario permite iniciar Garlic y existe un loader accesible en el puerto correspondiente.
- Durante la preparación de v6.8, los catálogos públicos consultados enumeran `garlic-savemgr v1.13`.

### Portabilidad
- `AppContext.BaseDirectory` es la raíz de almacenamiento de la aplicación.
- Configuración en `data/`.
- Perfiles en `data/console_profiles.json`.
- Preferencia de UI en `data/ui_settings.json`.
- Metadata en `data/game_metadata.json`.
- Carátulas en `covers/`.
- Payloads en `payload_cache/`.
- Copias y logs en `garlic_saves/`.
- No se usa `%AppData%`, `%LocalAppData%` ni el Registro para la persistencia normal.
- La carpeta completa puede trasladarse a otro PC siempre que sea escribible.

### Mantenimiento y robustez
- User-Agent unificado con la versión pública.
- Eliminadas referencias de versiones internas en la información pública del producto.
- Separación de la preferencia de UI respecto de los perfiles de consola.
- Bloqueo de cambio de perfil, ajustes y pestañas durante operaciones activas.
- Correcciones de XAML y recursos de interfaz detectadas durante la compilación de las iteraciones de transición.
- Eliminado el duplicado histórico de `Garlic_SaveMgr_main.py` de la raíz; se conserva el snapshot de `legacy/python-6.6.1/`.
- Descubrimiento de consola limitado al `/24` de la interfaz IPv4 activa, en lugar de recorrer el bloque completo `192.168.0.0/16`.
- Soporte de descubrimiento para redes IPv4 privadas `10.x.x.x`, `172.16.x.x–172.31.x.x` y `192.168.x.x`.
- Escaneo de descubrimiento mediante un pool continuo de 32 sondas, con timeouts LAN más realistas y cancelación inmediata al encontrar una consola.
- Prioridad inicial para la IP local y las puertas de enlace sin perder la cobertura completa del `/24`.
- Fuentes del payload desacopladas del ejecutable mediante `data/payload_sources.json`, con una lista extensible de catálogos PLDMGR y API de GitHub configurable.
- Los valores predeterminados de las fuentes del payload se mantienen cuando la configuración externa no existe o no es válida.
- Registro de diagnóstico añadido en `PayloadLauncherService` para fallos al consultar Garlic, leer metadata de caché, verificar SHA-256 y limpiar archivos temporales.
- La limpieza de temporales se realiza mediante una rutina de diagnóstico que registra el error sin ocultarlo.
- Añadido `GarlicSaveMgr.Tests` como proyecto xUnit v3 para la lógica de `Services`, integrado en `GarlicSaveMgr.sln`.
- Añadidas pruebas deterministas para la comparación de versiones del payload mediante `PayloadLauncherService.CompareVersions`.
- `build.ps1` ahora restaura, compila y ejecuta tests antes de publicar el ejecutable `win-x64` self-contained single-file.
- Actualizadas las instrucciones de compilación local y el archivo `BUILD.txt` para eliminar el resultado histórico de una build antigua.
- Añadido workflow de GitHub Actions sobre `windows-latest` para ejecutar restore, build Release y tests en cada `push` a `main` y en pull requests.
- Corregida la importación del espacio de nombres `Xunit` en el proyecto de tests para que `[Fact]`, `[Theory]` e `[InlineData]` sean reconocidos durante la compilación.
- Añadido `global.json` para activar `Microsoft.Testing.Platform` cuando se utiliza .NET 10 SDK.
- Adaptado `build.ps1` para usar `dotnet test --project` en .NET 10+ y mantener la sintaxis compatible con SDK anteriores.
- GitHub Actions actualizado a .NET 10 para reproducir el mismo flujo MTP utilizado en entornos actuales.

---

## Línea 6.7 — transición C#

La serie 6.7 fue la línea de trabajo utilizada para convertir el cliente Python a C#/.NET 8 + WPF y depurarlo antes de la publicación de v6.8.

### 6.7.1
- Corrección de la detección de IP inválida.
- Reducción de I/O de log durante el escaneo.
- Sondas concurrentes en la subred prioritaria.
- Timeout corto de detección.
- Corrección de estados al cancelar operaciones.
- Nombres de backup con resolución de milisegundos y protección contra colisiones.

### 6.7.4
- Flujo de arranque que comprueba Garlic y permite al usuario recuperarlo cuando no está disponible.
- Reintento desde la interfaz.

### 6.7.6
- Caché del payload desacoplada del arranque.
- Comparación separada entre payload en ejecución y payload catalogado.
- Preparación de la caché sin bloquear la UI.

### 6.7.7
- Lectura de la versión de Garlic desde el HTML servido por la consola.
- Priorización del bloque `<nav>` del HTML.

### 6.7.8
- Primera UI moderna con vistas Simple y Detallada.
- Panel técnico opcional.
- Rediseño de cabecera, controles y tarjetas.

### 6.7.9
- Portabilidad de configuración y datos junto al ejecutable.
- Ajustes responsive de la vista Simple.

### 6.7.10
- Introducción de carátulas y vista Simple de biblioteca.
- Correcciones de compilación del sistema de plantillas de carátulas.

### 6.7.11
- Ajustes de proporción de carátulas.
- Integración del icono Garlic suministrado para la aplicación.

### 6.7.12
- Unificación visual de las pestañas Copia de seguridad / Restaurar.
- Restauración Simple basada en tarjetas.

### 6.7.13
- Agrupación de restauraciones por juego/Título ID para evitar repetir la misma carátula por cada save.

### 6.7.14
- Rediseño del selector de perfil y de los controles de pestaña.

### 6.7.15
- Corrección de las cuatro esquinas de las pestañas activa/inactiva.
- Separación del indicador azul del borde inferior de la superficie de la pestaña.

### 6.7.16
- Auditoría de mantenimiento.
- Separación del modo Simple de la configuración de consola.
- Mejora de Ajustes y persistencia portable.
- Bloqueo de controles durante operaciones.
- Metadata preparada para asociar la ruta de las carátulas.

---

## v6.6.1 — Python original

Versión pública de referencia del proyecto original de RastaFairy.

- Corrección definitiva del flujo de eliminación de saves en consola.
- Base funcional utilizada para la reimplementación C#.
- Snapshot histórico preservado en `legacy/python-6.6.1/`.

---

## Principio de versionado

La versión de la aplicación de escritorio y la versión del payload Garlic son independientes.

```text
Aplicación PC:  v6.8
Payload Garlic: v1.13
```

El número del payload no sustituye al número de versión de Garlic SaveMgr.
