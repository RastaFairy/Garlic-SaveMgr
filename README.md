# Garlic SaveMgr

**Garlic SaveMgr v6.8** es un cliente de escritorio para Windows que permite gestionar copias de seguridad y restauraciones de partidas de PS5 mediante la red local y el servicio `garlic-savemgr`.

Esta versión continúa la evolución del proyecto original de **RastaFairy** (`v6.6.1`, Python/PySide6) y publica como implementación principal una reescritura en **C# / .NET 8 / WPF**.

> **Versión:** v6.8  
> **Plataforma:** Windows x64  
> **Framework:** .NET 8 + WPF  
> **Licencia:** GPL-3.0  
> **Proyecto base:** https://github.com/RastaFairy/Garlic-SaveMgr

[![Release](https://img.shields.io/badge/release-v6.8-informational)](https://github.com/RastaFairy/Garlic-SaveMgr/releases)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](./LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)](https://github.com/RastaFairy/Garlic-SaveMgr)

---

## Qué aporta v6.8

La versión pública v6.8 reúne las mejoras desarrolladas después de la base Python 6.6.1:

- Reescritura en C#/.NET 8 + WPF.
- Interfaz clara y moderna, sin modo oscuro.
- Dos modos de interfaz: **Simple** y **Detallada**.
- Vista Simple orientada a biblioteca: **carátula + nombre**.
- Vista Detallada con información técnica, actividad y metadatos.
- Pestañas Copia de seguridad / Restaurar con estilo visual unificado.
- Restauración agrupada por juego en modo Simple.
- Búsqueda y filtrado.
- Tablas ordenables en modo Detallado.
- Tamaño de las copias visible.
- SHA-256 de backups descargados.
- Exportación de copias a ZIP.
- Notificación visual/sonora al terminar operaciones largas.
- Perfiles para múltiples consolas.
- Detección automática de la consola en la red local.
- Detección de la versión de Garlic leyendo el HTML que sirve la consola.
- Comprobación de la versión disponible del payload mediante catálogos PLDMGR.
- Caché local del payload con verificación SHA-256.
- Caché local de carátulas.
- Almacenamiento completamente portable junto al ejecutable.
- Registros y diagnóstico detallado.
- Cancelación controlada de operaciones.

La lógica de backup/restauración conserva el flujo de la aplicación Python 6.6.1, mientras que la interfaz, portabilidad, caching, versionado y ergonomía se han ampliado en la línea C#.

---

## Inicio y conectividad

Al arrancar, la aplicación muestra inmediatamente la ventana principal y continúa en segundo plano con la comprobación de consola y la caché del payload.

Cuando Garlic ya está ejecutándose:

```text
Aplicación
    ↓
Detectar consola
    ↓
Comprobar Garlic
    ↓
Leer versión desde HTML /nav
    ↓
Consultar última versión del payload
    ↓
Comprobar caché local
    ↓
Continuar con el flujo principal
```

La versión de Garlic en ejecución y la versión del payload disponible son valores independientes. La caché local nunca se utiliza como sustituto de la versión realmente servida por la consola.

La aplicación puede cargar automáticamente el payload público cacheado mediante `elfldr` cuando la consola no tiene Garlic activo, siempre que exista un `elfldr` accesible en el puerto correspondiente y el usuario permita continuar.

> La identificación de versiones es independiente del número de versión de esta aplicación. Por ejemplo, la aplicación puede ser `v6.8` mientras Garlic SaveMgr para PS5 está en `v1.13`.

---

## Backup PS5 → PC

La aplicación obtiene los títulos y slots disponibles desde Garlic y permite seleccionar uno o varios títulos.

Las copias se conservan como imágenes `.img` junto con sus metadatos `.json`.

Cada backup puede registrar:

- título;
- Title ID;
- slot/save;
- propietario/UID cuando está disponible;
- fecha de creación;
- tamaño;
- SHA-256.

El nombre de los backups incorpora resolución de milisegundos para reducir colisiones entre operaciones muy próximas.

---

## Restauración PC → PS5

La restauración valida primero el perfil asociado al backup antes de modificar la consola.

En modo Simple, las copias están agrupadas por título para evitar mostrar múltiples tarjetas de una misma partida:

```text
[ CARÁTULA ]
Astro's Playroom
2 copias
```

En modo Detallado se conserva la información individual de cada backup/slot.

Las operaciones destructivas requieren confirmación explícita y las restauraciones por lote se detienen si no se supera la validación necesaria.

---

## Gestión de copias

### Exportación ZIP

Las copias seleccionadas pueden exportarse a un único ZIP para moverlas entre ordenadores.

### Verificación

Los backups descargados pueden verificarse mediante SHA-256. El hash se guarda junto al metadata para facilitar comprobaciones posteriores.

### Eliminación

Se soporta:

- eliminación de títulos/saves de la consola;
- eliminación de copias locales;
- confirmación antes de operaciones destructivas.

---

## Carátulas y metadata

Las carátulas se resuelven a partir del Title ID y se almacenan en caché local.

La aplicación intenta utilizar fuentes públicas para resolver imágenes y metadata sin bloquear el escaneo de Garlic.

Las carátulas se descargan en segundo plano y se reutilizan desde la carpeta local cuando ya existen.

---

## Interfaz

### Modo Simple

Pensado para el uso cotidiano:

- carátula;
- título;
- selección visual;
- acciones principales.

No muestra UID ni detalles internos innecesarios.

### Modo Detallado

Pensado para diagnóstico y usuarios avanzados:

- Title ID;
- UID;
- slots/saves;
- tamaño;
- estado;
- actividad en tiempo real;
- información del payload;
- SHA-256 y metadatos disponibles.

El selector Simple / Detallada se guarda en la configuración de la aplicación y no depende del perfil de consola.

---

## Portabilidad

Garlic SaveMgr es una aplicación **portable**. La carpeta que contiene el ejecutable es la raíz de almacenamiento de la aplicación.

Ejemplo:

```text
Garlic SaveMgr/
├── Garlic_SaveMgr.exe
├── data/
│   ├── settings.json
│   ├── ui_settings.json
│   ├── console_profiles.json
│   └── game_metadata.json
├── covers/
├── payload_cache/
└── garlic_saves/
    ├── enc/
    └── logs/
```

No se utiliza `%AppData%`, `%LocalAppData%` ni el Registro de Windows para la persistencia normal de la aplicación.

Para trasladarla a otro PC basta con copiar la carpeta completa. La ubicación debe ser escribible por el usuario que la ejecute.

---

## Compilación

Requisitos:

- Windows x64.
- .NET 8 SDK.

Desde PowerShell:

```powershell
.\build.ps1
```

O:

```powershell
.\build-one-line.ps1
```

La publicación se genera en:

```text
publish\Garlic_SaveMgr.exe
```

El proyecto utiliza WPF y está preparado para `win-x64`.

---

## Código fuente histórico

La implementación Python que originó este proyecto se conserva en:

```text
legacy/python-6.6.1/
```

Ese snapshot corresponde a la versión pública **6.6.1** de RastaFairy y se mantiene como referencia histórica y funcional para la evolución del port C#.

---

## Payload Garlic

Garlic SaveMgr es el cliente de escritorio. El servicio que corre en la PS5 procede del proyecto `garlic-savemgr`:

https://github.com/earthonion/garlic-savemgr

La aplicación reconoce dinámicamente la versión que está sirviendo la consola y consulta catálogos externos para conocer la última versión disponible.

En el momento de preparar v6.8, los catálogos PLDMGR consultados enumeran `garlic-savemgr v1.13` con el SHA-256 publicado por el propio catálogo.

---

## Licencia y atribuciones

El proyecto original Garlic SaveMgr de RastaFairy está publicado bajo GPL-3.0 y este repositorio conserva la licencia y los avisos correspondientes.

Consulta `CREDITS.md` y `LICENSE-NOTICE.md` para conocer el origen, las dependencias, el port C# y las atribuciones adicionales.

---

## Historial

La evolución detallada se encuentra en [`CHANGELOG.md`](./CHANGELOG.md).

El salto público relevante es:

```text
v6.6.1
   ↓
port C# / .NET 8
   ↓
v6.8
```

La serie 6.7.x se utilizó como línea de transición y mantenimiento durante el port y no se presenta como una serie pública independiente de la base original.
