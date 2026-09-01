# Depuración y cambios – Garlic SaveMgr C#

## Cambios de esta revisión

- Icono `garlicon.ico` integrado como recurso WPF y `ApplicationIcon` del ejecutable.
- Icono configurado también en la ventana principal.
- Estados de filas corregidos mediante `INotifyPropertyChanged` y `ElementStyle` para que los colores se apliquen a los `TextBlock` generados por `DataGridTextColumn`.
- Detección automática de consola añadida.
  - Prioriza la subred `/24` 192.168.x.0 donde está conectado el PC.
  - Si no encuentra Garlic, continúa por el resto de `192.168.0.0/16`.
  - Prueba el puerto Garlic configurado (8082 por defecto).
  - Deja como máximo 10 ms entre candidatos.
  - Se puede cancelar.
  - Al encontrar Garlic guarda la IP y realiza el escaneo de títulos automáticamente.
- El arranque intenta validar la IP guardada; si no responde, inicia detección automática.
- Se conserva la información de compatibilidad/hash del payload Garlic v1.13 para validación local.

## Compilación

El SDK .NET 8.0.424 está instalado en la VM, pero el SDK Linux no contiene `Microsoft.WindowsDesktop.App.Ref`; el contenedor tampoco tiene acceso a NuGet. Por ello la compilación WPF completa queda pendiente de restaurar el paquete WindowsDesktop desde una máquina con acceso a NuGet.

La primera llamada a `dotnet build GarlicSaveMgr.sln` además recibió una configuración externa inválida (`Debug|linux/amd64`). La solución solo declara `Any CPU`; el proyecto debe publicarse como `win-x64` mediante `build.ps1`.

## Payload

La versión publicada de Garlic SaveMgr se distribuye como ELF y se carga mediante un ELF loader antes de exponer el servidor HTTP en el puerto 8082. La aplicación conserva la identificación y el SHA-256 conocido del payload para comprobación, pero esta revisión **no implementa el envío automático de un payload de jailbreak a la consola**.

El flujo seguro implementado es detectar si el servicio Garlic ya está activo y continuar automáticamente con la aplicación.

## Mejoras UX incorporadas
La interfaz mantiene exclusivamente el tema claro y ahora permite ordenar ambas tablas, filtrar por texto, ver el tamaño real de cada IMG y consultar el SHA-256 almacenado.

Cada backup nuevo calcula automáticamente SHA-256 después de descargarse por completo y lo guarda en el sidecar JSON. Las copias seleccionadas pueden exportarse juntas como ZIP.

Se añadieron perfiles de consola persistentes. El selector superior cambia de IP/puerto/nombre sin exigir reconfiguración manual.

Al finalizar escaneos, backups, restauraciones, eliminaciones y exportaciones se emite una notificación visual/sonora.


## v6.7.4-gpt
- Flujo de arranque revisado: no se ejecutan ni transfieren payloads.
- Se comprueba `/api/status` tras detectar/seleccionar la consola.
- Si Garlic no responde, se muestra un diálogo de reintento.
- Cuando vuelve a responder, se inicia automáticamente el escaneo de títulos.

## 6.7.8-gpt
- Problema corregido: la versión del Garlic en ejecución no podía obtenerse porque el cliente solo consultaba `/api/status`.
- Solución: consulta ligera a `/` y extracción de versión exclusivamente dentro de `<nav>`.
- Fallback conservado: campos de versión en `/api/status` y cabecera `Server`.
- Validación del extractor realizada contra el HTML conocido de Garlic v1.13.

## 6.7.9 - Portabilidad y UI Simple

- UID y controles asociados se ocultan por completo en modo Simple.
- El panel de log no conserva ancho reservado en modo Simple.
- La raíz portable es `AppContext.BaseDirectory`.
- Se eliminó la persistencia mediante Registro de Windows.
- Toda la configuración y caché de la aplicación se almacena bajo la raíz portable.

## Build validation correction

The v6.7.10 source package originally contained three source errors that prevented compilation:
- `Clip="True"` on a `Border` (Clip expects Geometry); removed.
- Four verbatim C# regex strings incorrectly escaped with `\"`; corrected to doubled quotes.
- `MainWindow.xaml.cs` referenced `Border` without `System.Windows.Controls` and referenced missing `CoverImage` properties on `TitleRow`/`BackupRow`; restored these declarations.

After correction: `dotnet build -c Release --no-restore` = 0 warnings, 0 errors.
`dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore` = successful.
