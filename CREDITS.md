# Créditos y atribuciones

Garlic SaveMgr es un proyecto desarrollado y mantenido por **RastaFairy**. La versión pública actual del cliente de escritorio es **v6.8.1**, implementada en **C# / .NET 8 / WPF**.

Este documento separa deliberadamente las dependencias actuales del cliente, la infraestructura externa de la PS5 y los componentes que pertenecen a la implementación histórica Python 6.6.1.

---

## 1. Proyecto

**Garlic SaveMgr**  
Repositorio: https://github.com/RastaFairy/Garlic-SaveMgr

La implementación actual es responsable de la interfaz, comunicación con Garlic, backup, restore, perfiles, detección de consola, metadata, carátulas, caché de payload, logs y empaquetado.

La implementación Python histórica se conserva en `legacy/python-6.6.1/`.

---

## 2. Dependencias actuales del cliente de PC

### C# / .NET 8

La aplicación actual utiliza C# y el runtime .NET 8 para su lógica de aplicación, comunicaciones, acceso a disco, serialización JSON, criptografía y ejecución de procesos.

### WPF

WPF proporciona la interfaz gráfica de Windows de la versión actual.

### API HTTP / bibliotecas de .NET

El cliente utiliza `HttpClient`, `SocketsHttpHandler`, `System.Text.Json`, `System.IO.Compression` y las bibliotecas estándar de .NET. No hay una dependencia NuGet externa necesaria para la lógica principal del cliente publicada en este repositorio.

### ping.exe de Windows

La autodetección de v6.8.1 utiliza el `ping.exe` nativo de Windows como herramienta del sistema. Cada proceso se ejecuta oculto y su salida se guarda temporalmente durante el descubrimiento.

---

## 3. Infraestructura de PS5

### garlic-savemgr — earthonion

Repositorio: https://github.com/earthonion/garlic-savemgr

`garlic-savemgr` es el servicio/payload que se ejecuta en la PS5 y proporciona la API HTTP utilizada por el cliente. La API Garlic utiliza el puerto `8082`.

Garlic SaveMgr es el **cliente para PC**; no reclama la autoría del payload `garlic-savemgr`.

### elfldr

El flujo de arranque utiliza un ELF loader accesible en `9021` para transferir el payload cuando Garlic todavía no está activo. `9021` no sustituye a `8082` como API de Garlic.

---

## 4. Toolchain y comunidad PS5

### PS5 Payload SDK / OpenAGC

Repositorio: https://github.com/OpenAGC/ps5-payload-sdk

Se reconoce como parte del ecosistema de desarrollo de payloads de PS5. No constituye una dependencia NuGet ni una biblioteca incorporada directamente al cliente C#.

### ps5-payload-dev

Repositorio: https://github.com/ps5-payload-dev

Proyecto/ecosistema comunitario relacionado con desarrollo de payloads para PS5. La inclusión aquí es como referencia comunitaria, no como afirmación de dependencia directa de cada repositorio individual.

---

## 5. Implementación histórica Python 6.6.1

El snapshot histórico bajo `legacy/python-6.6.1/` utilizó tecnologías que ya no forman parte de la implementación actual publicada.

### Python

https://www.python.org/

### PySide6 / Qt for Python

https://doc.qt.io/qtforpython-6/

### Requests

https://github.com/psf/requests

### PyInstaller

https://github.com/pyinstaller/pyinstaller

Estas referencias corresponden a la **arquitectura histórica**, no a dependencias de ejecución del cliente C#/.NET 8.

---

## 6. Recursos y datos externos

El cliente consulta servicios públicos para completar metadata, carátulas y catálogos de payload. Estas fuentes pueden cambiar o dejar de estar disponibles y no deben interpretarse como parte del código fuente de Garlic SaveMgr.

Las fuentes de payload utilizadas actualmente incluyen catálogos PLDMGR públicos. La versión v1.13 validada durante la preparación de 6.8.1 tiene como referencia el SHA-256:

```text
b6d366f4101fa2fcc14a353d083ef7e45e1cc86ef457bb20502bd8680dce4d73
```

La aplicación consulta los catálogos dinámicamente para versiones posteriores.

---

## 7. Atribución y licencia

El proyecto original Garlic SaveMgr de RastaFairy se distribuye bajo **GPL-3.0**. Este repositorio conserva `LICENSE` y `LICENSE-NOTICE.md`.

Consulta también el repositorio de cada componente externo para comprobar sus licencias y obligaciones concretas de redistribución.

La aparición de un proyecto o persona en este documento no implica patrocinio, relación oficial ni respaldo mutuo salvo que exista una declaración explícita de las partes correspondientes.

---

## 8. Agradecimientos

Se agradece a los desarrolladores y mantenedores de `garlic-savemgr`, de los toolchains de payloads de PS5, de .NET, WPF y de las herramientas de la implementación histórica, así como a la comunidad de investigación y homebrew de PlayStation que comparte documentación, herramientas, pruebas y conocimiento técnico.

---

## 9. Enlaces

- Garlic SaveMgr: https://github.com/RastaFairy/Garlic-SaveMgr
- garlic-savemgr: https://github.com/earthonion/garlic-savemgr
- PS5 Payload SDK: https://github.com/OpenAGC/ps5-payload-sdk
- ps5-payload-dev: https://github.com/ps5-payload-dev
- Qt for Python: https://doc.qt.io/qtforpython-6/
- Requests: https://github.com/psf/requests
- PyInstaller: https://github.com/pyinstaller/pyinstaller
- Python: https://www.python.org/

---

## Última actualización

Este documento corresponde a la arquitectura y las atribuciones de **Garlic SaveMgr v6.8.1**.
