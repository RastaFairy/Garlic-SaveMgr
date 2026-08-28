# Créditos y agradecimientos

Garlic SaveMgr es el resultado de trabajo realizado sobre varias capas del ecosistema de **homebrew, investigación y desarrollo para PlayStation**.

La aplicación es desarrollada y mantenida por **RastaFairy**, pero su existencia depende de software libre, infraestructura de payloads, herramientas de desarrollo y años de investigación compartida por la comunidad.

Este documento tiene como objetivo reconocer esas aportaciones y, al mismo tiempo, distinguir claramente entre:

- software utilizado directamente por Garlic SaveMgr;
- infraestructura utilizada por el payload de PS5;
- herramientas empleadas durante el desarrollo y empaquetado;
- proyectos de la comunidad que aportaron conocimiento o infraestructura;
- influencias históricas que ya no forman parte de las dependencias actuales.

---

## 1. Proyecto y mantenimiento

### RastaFairy — Garlic SaveMgr

**Autor y mantenedor:** RastaFairy  
**Repositorio:** https://github.com/RastaFairy/Garlic-SaveMgr

Garlic SaveMgr es desarrollado y mantenido por **RastaFairy**.

El cliente actual para PC es responsable de:

- la interfaz gráfica;
- la comunicación con la PS5;
- la gestión de copias de seguridad;
- la gestión de restauraciones;
- los metadatos locales de los backups;
- la validación de compatibilidad del perfil;
- los flujos de eliminación;
- los indicadores de progreso;
- el tratamiento de errores;
- el sistema de logs;
- la configuración de la aplicación;
- el empaquetado y preparación de releases;
- la documentación y mantenimiento del proyecto.

La versión pública actual es **v6.6.1**.

---

# 2. Dependencias directas

Las siguientes tecnologías forman parte del entorno utilizado directamente por la aplicación actual para PC.

## Python

**Proyecto:** Python  
**Web:** https://www.python.org/  
**Licencia:** Python Software Foundation License y licencias aplicables al componente distribuido.

Garlic SaveMgr está implementado en Python.

La aplicación utiliza tanto Python como su biblioteca estándar para funciones esenciales como:

- acceso al sistema de archivos;
- procesamiento de JSON;
- registros y logging;
- ejecución y coordinación de procesos;
- hilos;
- gestión de configuración;
- tratamiento de fechas y horas;
- estructuras de datos;
- operaciones auxiliares de la aplicación.

Python constituye la base de ejecución del cliente para PC.

Información legal y de licenciamiento:

https://www.python.org/psf/about/legal-and-policies/

---

## PySide6 / Qt for Python

**Proyecto:** Qt for Python / PySide6  
**Web:** https://doc.qt.io/qtforpython-6/

PySide6 proporciona el framework gráfico utilizado por Garlic SaveMgr.

La aplicación utiliza PySide6 para elementos como:

- ventanas;
- pestañas;
- botones;
- tablas;
- cuadros de diálogo;
- barras de progreso;
- controles de configuración;
- señales y slots;
- hilos de trabajo;
- preferencias persistentes;
- gestión del evento gráfico;
- elementos visuales de la interfaz.

La utilización del modelo de eventos y del sistema de hilos de Qt también permite mantener la interfaz responsiva durante operaciones de backup y restore que pueden tardar varios segundos.

PySide6 es la integración oficial de Qt con Python.

Documentación oficial:

https://doc.qt.io/qtforpython-6/

### Licencia

Qt y PySide6 están sujetos a las condiciones de licencia correspondientes a los componentes utilizados y a la modalidad de distribución elegida, incluyendo las licencias LGPL/GPL de Qt o sus alternativas comerciales cuando correspondan.

Para cualquier redistribución, debe consultarse la documentación y los términos de licencia de la versión concreta utilizada.

---

## Requests

**Proyecto:** Requests  
**Repositorio:** https://github.com/psf/requests  
**Licencia:** Apache License 2.0

Garlic SaveMgr utiliza Requests como biblioteca de comunicación HTTP entre el cliente de PC y el servicio proporcionado en la PS5.

Se utiliza para operaciones como:

- peticiones HTTP;
- transferencias de datos;
- descargas;
- subidas;
- respuestas JSON;
- tiempos de espera;
- tratamiento de errores HTTP;
- transferencia de datos binarios.

Repositorio oficial:

https://github.com/psf/requests

Licencia:

https://github.com/psf/requests/blob/main/LICENSE

---

# 3. Infraestructura de PS5

## garlic-savemgr — earthonion

**Proyecto:** garlic-savemgr  
**Autor / mantenedor:** earthonion  
**Repositorio:** https://github.com/earthonion/garlic-savemgr

Este es uno de los componentes externos más importantes de la arquitectura de Garlic SaveMgr.

Garlic SaveMgr es el **cliente de PC**. El servicio que permite gestionar los datos de guardado en la PS5 procede del proyecto `garlic-savemgr`, que actúa en el lado de la consola y proporciona la interfaz de red utilizada por el cliente.

De forma conceptual:

```text
┌─────────────────────────────┐
│             PC              │
│                             │
│       Garlic SaveMgr        │
│          PySide6             │
│              │              │
│           Requests          │
└──────────────┼──────────────┘
               │
               │ HTTP / Red local
               │
               ▼
┌─────────────────────────────┐
│             PS5             │
│                             │
│        garlic-savemgr       │
│          payload            │
│                             │
│       Gestión de saves      │
└─────────────────────────────┘
```

El proyecto original proporciona el servicio del lado de la PS5 y la API HTTP con la que se comunica Garlic SaveMgr.

La documentación del proyecto upstream utiliza el puerto `8082` para este servicio.

### Atribución

La implementación del payload `garlic-savemgr` pertenece a su autor y a los colaboradores del proyecto original.

Garlic SaveMgr es un proyecto independiente que proporciona el cliente de escritorio para PC.

Repositorio original:

https://github.com/earthonion/garlic-savemgr

---

# 4. Ecosistema PS5 Payload SDK

## OpenAGC / PS5 Payload SDK

**Proyecto:** PS5 Payload SDK  
**Repositorio:** https://github.com/OpenAGC/ps5-payload-sdk  
**Licencia:** GPL-3.0

El PS5 Payload SDK **no es una dependencia de ejecución directa del cliente Python**.

Forma parte de la infraestructura utilizada para desarrollar y compilar payloads destinados a PS5. En particular, el proyecto `garlic-savemgr` utiliza este tipo de toolchain para construir su payload.

Por tanto, la relación debe entenderse como:

```text
Garlic SaveMgr
    │
    ├── Python
    ├── PySide6
    └── Requests
```

mientras que en el lado de la consola:

```text
garlic-savemgr
    │
    └── PS5 Payload SDK
```

El SDK proporciona herramientas y componentes necesarios para el desarrollo de payloads para PS5.

Repositorio:

https://github.com/OpenAGC/ps5-payload-sdk

---

## ps5-payload-dev

**Organización:** ps5-payload-dev  
**Repositorio:** https://github.com/ps5-payload-dev

El ecosistema `ps5-payload-dev` reúne proyectos relacionados con el desarrollo de payloads y software de bajo nivel para PS5.

Este ecosistema se reconoce como parte de la infraestructura de desarrollo sobre la que se apoya la comunidad de homebrew de PS5.

No debe interpretarse que todos los proyectos de dicha organización sean dependencias directas de Garlic SaveMgr. La atribución individual debe realizarse únicamente cuando un componente concreto se utilice realmente en el proyecto.

---

# 5. Herramientas de empaquetado

## PyInstaller

**Proyecto:** PyInstaller  
**Repositorio:** https://github.com/pyinstaller/pyinstaller  
**Licencia:** GPL-2.0-or-later

PyInstaller se utiliza como herramienta opcional de empaquetado para generar ejecutables independientes de Windows a partir de la aplicación Python.

Su función pertenece al proceso de **build y distribución**, no a la ejecución normal del código fuente.

Conceptualmente:

```text
Código Python
     +
Dependencias
     +
Intérprete Python
          │
          ▼
      PyInstaller
          │
          ▼
Ejecutable Windows
```

Repositorio:

https://github.com/pyinstaller/pyinstaller

Documentación:

https://pyinstaller.org/

---

# 6. Qt y herramientas de desarrollo

Garlic SaveMgr también se beneficia del ecosistema de herramientas que acompaña a Qt for Python.

Esto incluye las herramientas disponibles junto con PySide6 para:

- desarrollo;
- depuración;
- gestión de interfaces;
- distribución;
- mantenimiento de aplicaciones Qt.

Estas herramientas forman parte del ecosistema de desarrollo, pero no necesariamente de la ejecución final de la aplicación.

Documentación:

https://doc.qt.io/qtforpython-6/

---

# 7. Comunidad de investigación y Homebrew de PlayStation

Garlic SaveMgr no existiría sin los años de trabajo realizados por la comunidad de investigación y homebrew de PlayStation.

El proyecto se encuentra al final de una cadena de investigación y desarrollo que incluye, entre muchos otros ámbitos:

- ingeniería inversa;
- análisis de sistemas;
- investigación de payloads;
- desarrollo de SDKs;
- investigación de sistemas de archivos;
- análisis de savedata;
- análisis de estructuras SFO;
- investigación de PFS;
- debugging;
- desarrollo de herramientas;
- documentación técnica;
- pruebas y reproducción de errores.

Una gran parte de este conocimiento ha sido desarrollado, documentado y compartido públicamente por investigadores y desarrolladores de la comunidad a lo largo de muchos años.

Por ello, este proyecto reconoce expresamente a esa comunidad aunque un determinado desarrollo no forme parte de sus dependencias directas.

Este reconocimiento implica **agradecimiento técnico y comunitario**, no una atribución de autoría sobre Garlic SaveMgr.

---

# 8. Ecosistema de Homebrew de PS4

Las primeras etapas del desarrollo de Garlic SaveMgr incluyeron diferentes experimentos relacionados con PS4, gestión de saves, resignado, transferencia entre perfiles y otros modelos de trabajo que posteriormente fueron eliminados o rediseñados.

Aunque esas funciones ya no forman parte de la arquitectura actual centrada en PS5, el conocimiento generado por el ecosistema de PS4 fue una influencia relevante durante las primeras fases del proyecto.

Por ese motivo se incluye aquí como **influencia histórica**, no como dependencia actual.

---

## GoldHEN

**Proyecto:** GoldHEN  
**Repositorio:** https://github.com/GoldHEN/GoldHEN

GoldHEN es un Homebrew Enabler para PS4 y **no es una dependencia actual de Garlic SaveMgr**.

Se incluye en esta sección como reconocimiento al ecosistema de desarrollo y conocimiento generado alrededor del proyecto y de la comunidad de PS4.

El repositorio de GoldHEN mantiene sus propios créditos y reconoce a numerosos desarrolladores e investigadores de la escena.

Garlic SaveMgr no atribuye a esas personas la autoría del proyecto ni implica una relación oficial con GoldHEN.

Repositorio:

https://github.com/GoldHEN/GoldHEN

---

# 9. Licencias de terceros

La siguiente tabla resume las tecnologías y proyectos principales mencionados en este documento:

| Componente | Función | Relación con Garlic SaveMgr | Licencia |
|---|---|---|---|
| Python | Runtime y biblioteca estándar | Dependencia directa | Python Software Foundation License y licencias aplicables |
| PySide6 / Qt | Interfaz gráfica | Dependencia directa | LGPL/GPL o licencia comercial de Qt, según corresponda |
| Requests | Comunicación HTTP | Dependencia directa | Apache License 2.0 |
| PyInstaller | Empaquetado | Herramienta de build | GPL-2.0-or-later |
| garlic-savemgr | Servicio de gestión de saves en PS5 | Infraestructura externa fundamental | Consultar repositorio upstream |
| PS5 Payload SDK | Toolchain del payload | Dependencia del lado del payload | GPL-3.0 |
| ps5-payload-dev | Ecosistema de desarrollo PS5 | Infraestructura comunitaria | Según cada repositorio |

Esta tabla es un resumen orientativo y no sustituye los archivos de licencia de cada proyecto.

Cuando se redistribuya software de terceros dentro de un ejecutable o paquete, deben comprobarse las obligaciones concretas derivadas de todas las dependencias realmente incluidas.

---

# 10. Principios de atribución

El proyecto intenta mantener una regla sencilla:

> **Dar crédito al trabajo original por aquello que realmente aporta.**

Por esta razón, este documento diferencia entre autoría, dependencia, infraestructura e influencia.

La relación puede resumirse así:

```text
Garlic SaveMgr
    = Cliente de PC
    = Desarrollado y mantenido por RastaFairy
```

```text
garlic-savemgr
    = Servicio / payload de gestión de saves en PS5
```

```text
PS5 Payload SDK
    = Infraestructura para desarrollo y compilación de payloads
```

```text
PySide6 / Qt
    = Framework de interfaz gráfica
```

```text
Requests
    = Biblioteca de comunicación HTTP
```

```text
PyInstaller
    = Herramienta opcional de empaquetado
```

```text
Comunidad PS4 / PS5
    = Investigación, conocimientos, herramientas y ecosistema
```

Esta separación pretende evitar tanto la apropiación involuntaria de trabajo ajeno como la atribución incorrecta de responsabilidades.

---

# 11. Agradecimientos especiales

Un agradecimiento especial a:

- los desarrolladores y mantenedores de `garlic-savemgr`;
- los desarrolladores y mantenedores del PS5 Payload SDK;
- los proyectos y mantenedores de `ps5-payload-dev`;
- el equipo de Qt y los desarrolladores de PySide6;
- los mantenedores y colaboradores de Requests;
- los mantenedores y colaboradores de PyInstaller;
- los investigadores de PS4 y PS5;
- los desarrolladores de herramientas de análisis y debugging;
- las personas que publican documentación y resultados de investigación;
- los usuarios y testers que reproducen problemas y proporcionan información útil para corregirlos;
- todas las personas que han contribuido de forma abierta al ecosistema de PlayStation Homebrew.

Sin ese trabajo acumulado, una herramienta como Garlic SaveMgr no sería viable.

---

# 12. Evolución del proyecto

Garlic SaveMgr no surgió directamente con su arquitectura actual.

El desarrollo pasó por numerosas etapas experimentales, entre ellas:

- soporte de PS4;
- soporte de PS5;
- distintos modelos de consola origen/destino;
- transferencia de saves;
- resignado;
- transformaciones de perfiles;
- gestión de SFO;
- diferentes variantes de payload;
- distintas arquitecturas de interfaz;
- diferentes diseños de API;
- múltiples estrategias de backup y restore.

Muchas de estas aproximaciones fueron finalmente retiradas.

Su desaparición no debe interpretarse necesariamente como pérdida de trabajo: varias de ellas permitieron identificar problemas de compatibilidad, seguridad, integridad de los datos o complejidad de mantenimiento que llevaron a la arquitectura actual.

La evolución completa está documentada en:

[Historial completo del proyecto — changelog.md](./changelog.md)

---

# 13. Reconocimiento a la comunidad

El desarrollo de software libre y de herramientas de investigación es acumulativo.

Garlic SaveMgr puede entenderse como una parte de una cadena mucho mayor:

```text
Investigación
      ↓
Ingeniería inversa
      ↓
Herramientas y SDKs
      ↓
Payloads
      ↓
Homebrew de PS4 / PS5
      ↓
Investigación de savedata
      ↓
garlic-savemgr
      ↓
Garlic SaveMgr
```

El proyecto reconoce esta realidad y considera que dar crédito a las herramientas, proyectos y personas que hicieron posible este ecosistema forma parte de una práctica responsable de desarrollo open source.

Asimismo, si en futuras versiones se incorporan componentes externos adicionales, sus autores, proyectos y licencias deberán añadirse a este documento.

---

# 14. Enlaces

## Garlic SaveMgr

https://github.com/RastaFairy/Garlic-SaveMgr

## Releases

https://github.com/RastaFairy/Garlic-SaveMgr/releases

## Historial de cambios

./changelog.md

## garlic-savemgr

https://github.com/earthonion/garlic-savemgr

## PS5 Payload SDK

https://github.com/OpenAGC/ps5-payload-sdk

## ps5-payload-dev

https://github.com/ps5-payload-dev

## Qt for Python / PySide6

https://doc.qt.io/qtforpython-6/

## Requests

https://github.com/psf/requests

## PyInstaller

https://github.com/pyinstaller/pyinstaller

## Python

https://www.python.org/

## GoldHEN

https://github.com/GoldHEN/GoldHEN

---

# 15. Aviso sobre las atribuciones

La aparición de un proyecto, organización o persona en este documento no implica que exista una relación oficial, patrocinio o respaldo de Garlic SaveMgr, salvo que dicha relación haya sido declarada expresamente por las partes correspondientes.

Del mismo modo, un agradecimiento a un proyecto no significa que Garlic SaveMgr incorpore necesariamente su código.

Los nombres de proyectos, marcas y demás elementos identificativos pertenecen a sus respectivos propietarios.

---

## Última actualización

Este documento corresponde a la arquitectura y dependencias conocidas del proyecto **Garlic SaveMgr v6.6.1**.

Cualquier cambio futuro en las dependencias, componentes externos o infraestructura utilizada deberá reflejarse en este archivo.
