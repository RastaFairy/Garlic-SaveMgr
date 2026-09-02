# Garlic SaveMgr — Changelog

## v6.8.1 — 2026-09-02

### Descubrimiento de consola
- Consolidación del descubrimiento determinista mediante `ping.exe` nativo de Windows.
- Recorrido del espacio configurado `192.168.0.0` → `192.168.255.255`.
- Lotes de hasta 255 procesos de ping simultáneos.
- Salida temporal individual de cada ping en `discovery_temp/`.
- Solo los hosts con respuesta ICMP pasan a la validación de Garlic.
- `8082` es el puerto primario de la API Garlic.
- `9021` se comprueba como `elfldr` cuando Garlic todavía no está activo.
- Fallback de IP manual mantenido.
- Sin dependencia del router, ARP, SSDP o mDNS.
- Limpieza automática de lotes de diagnóstico antiguos.

### Payload y arranque
- Preparación y caché del payload desacopladas del arranque de la UI.
- Envío autorizado del payload a `9021/elfldr` cuando Garlic no está activo.
- Espera posterior de Garlic en `8082`.
- Separación entre versión de Garlic en ejecución, versión cacheada y versión anunciada por catálogo.
- Referencia de `garlic-savemgr v1.13` validada con SHA-256 `b6d366f4101fa2fcc14a353d083ef7e45e1cc86ef457bb20502bd8680dce4d73`.

### Validación
- Prueba funcional en Windows con descubrimiento satisfactorio en `192.168.1.211`.
- Confirmación de Garlic en `8082`.
- Envío del payload v1.13 mediante `192.168.1.211:9021`.
- Confirmación del arranque de Garlic y escaneo posterior de 41 títulos.

### Identidad de versión
- `AppInfo.Version`: `6.8.1`.
- `AssemblyVersion`: `6.8.1.0`.
- `FileVersion`: `6.8.1.0`.
- `InformationalVersion`: `6.8.1`.
- Artifact de CI: `Garlic_SaveMgr-v6.8.1-win-x64`.

### Interfaz, biblioteca y operaciones
- C#/.NET 8 + WPF como implementación principal.
- Tema claro y selector persistente Simple / Detallada.
- Biblioteca con carátulas y agrupación por juego.
- Vista detallada con información técnica y actividad.
- Búsqueda, filtrado y ordenación.
- Backup PS5 → PC y restauración PC → PS5.
- SHA-256 de backups y sidecar JSON.
- Exportación de copias a ZIP.
- Confirmación antes de operaciones destructivas.
- Cancelación controlada mediante `CancellationToken`.
- Perfiles para múltiples consolas.
- Almacenamiento portable junto al ejecutable.

---

## v6.8 — 2026-09-01

Primera publicación de la nueva implementación C#/.NET 8 + WPF. Incluyó la nueva interfaz, perfiles, backup/restore, metadata, carátulas, caché de payload y el primer sistema de autodetección.

La autodetección de la release inicial fue sustituida por el método de lotes de 255 pings consolidado en v6.8.1.

---

## Línea 6.7 — transición C#

La serie 6.7 fue la línea de trabajo utilizada para convertir el cliente Python a C#/.NET 8 + WPF y depurarlo antes de la publicación de v6.8.

### 6.7.1
- Corrección de la detección de IP inválida.
- Reducción de I/O de log durante el escaneo.
- Sondas concurrentes y timeout corto.
- Corrección de estados al cancelar operaciones.
- Nombres de backup con resolución de milisegundos y protección contra colisiones.

### 6.7.4
- Flujo de arranque que comprueba Garlic y permite recuperarlo cuando no está disponible.
- Reintento desde la interfaz.

### 6.7.6
- Caché del payload desacoplada del arranque.
- Comparación separada entre payload en ejecución y payload catalogado.
- Preparación de la caché sin bloquear la UI.

### 6.7.7
- Lectura de la versión de Garlic desde el HTML servido por la consola.
- Priorización del bloque `<nav>`.

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
- Agrupación de restauraciones por juego/Título ID.

### 6.7.14
- Rediseño del selector de perfil y de los controles de pestaña.

### 6.7.15
- Corrección de las cuatro esquinas de las pestañas.
- Separación del indicador azul del borde inferior.

### 6.7.16
- Auditoría de mantenimiento.
- Separación del modo Simple de la configuración de consola.
- Mejora de Ajustes y persistencia portable.
- Bloqueo de controles durante operaciones.
- Metadata preparada para asociar rutas de carátulas.

---

## v6.6.1 — Python original

Versión pública histórica de referencia del proyecto original de RastaFairy. Su snapshot se conserva en `legacy/python-6.6.1/`.

- Corrección definitiva del flujo de eliminación de saves en consola.
- Base funcional utilizada para la reimplementación C#.

---

## Principio de versionado

La versión de la aplicación de escritorio y la versión del payload Garlic son independientes.

```text
Aplicación PC:  v6.8.1
Payload Garlic: v1.13 (referencia validada)
```

Una actualización del payload no cambia automáticamente la versión del cliente de escritorio.
