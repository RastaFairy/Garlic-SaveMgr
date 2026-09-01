# Garlic SaveMgr — Changelog histórico

Historial técnico reconstruido a partir de los snapshots de desarrollo contenidos en el archivo histórico del proyecto y contrastado con el repositorio público de GitHub.

> **Nota de metodología**
>
> Este documento no trata los directorios `CLAUDE0`–`CLAUDE49` como versiones oficiales de publicación. Son snapshots internos del desarrollo. El orden mostrado a continuación sigue la evolución temporal detectada en el archivo histórico, porque la numeración `CLAUDE*` no es estrictamente cronológica.
>
> En los primeros snapshots coexistieron varias implementaciones del cliente: CLI con `rich`, prototipos GUI con PySide6 y sucesivas reescrituras. Por ello, algunas funciones aparecen, desaparecen y vuelven a aparecer con una arquitectura distinta. Cuando una función dejó de formar parte del producto final, se indica expresamente.

---

## Estado actual

**Versión pública actual:** `v6.6.1`

**Fecha de referencia:** 28 de agosto de 2026

**Repositorio:** `RastaFairy/Garlic-SaveMgr`

**Plataforma:** cliente de escritorio para Windows/PC, desarrollado en Python y PySide6.

**Alcance actual:** gestión de copias de seguridad y restauración de partidas de PS5 mediante el payload `garlic-savemgr.elf` a través de HTTP en la red local.

La evolución del proyecto puede dividirse claramente en cinco etapas:

1. **CLI inicial:** acceso directo a la API, exportación/importación y primeras funciones de resign.
2. **Primera GUI:** migración a PySide6 y organización de las operaciones en páginas y tablas.
3. **Arquitectura de transferencia entre consolas:** modelo Fuente/Destino, flujo E1–E5 y trabajo específico con saves PS4/PS5.
4. **Replanteamiento del producto:** abandono del resign genérico en favor de un administrador de backup/restore para PS5 con validación de perfil.
5. **Endurecimiento y mantenimiento:** verificación de identidad, diagnóstico, simplificación de configuración, borrado seguro y correcciones de errores concretos de API.

---

# 2026-06-16 — CLAUDE0

## Primera implementación funcional del cliente CLI

El proyecto comienza como un cliente de consola para controlar `garlic-savemgr` desde el PC.

### Funciones principales presentes

- Conexión HTTP contra el payload en el puerto `8082`.
- Configuración persistente de IP y puerto.
- Consulta de usuarios de la PS5.
- Consulta y listado de partidas.
- Montaje y desmontaje de saves.
- Lectura de información del save.
- Exportación de saves cifrados.
- Descarga de saves descifrados como ZIP.
- Exportación de todos los saves de un título.
- Importación de imágenes cifradas.
- Resignación de saves mediante `ACCOUNT_ID` y, opcionalmente, `USER_ID`.
- Descarga de imágenes resignadas.
- Flujo `dec.zip -> cifrado -> importación`.
- Consulta del log del servidor.
- Operaciones por lote.

### Soporte inicial PS4/PS5

Desde el primer snapshot el cliente contempla explícitamente dos familias de save:

- PS5: imágenes `.img`.
- PS4: imágenes `sdimg_*` y su `.bin` asociado.

El cliente ya incorporaba el parámetro `ps4` en varios endpoints y mantenía rutas específicas para resignación e importación.

### Modelo de trabajo inicial

El objetivo era exponer prácticamente toda la funcionalidad del payload de forma directa desde el terminal, sin una capa de seguridad ni una experiencia de usuario sofisticada.

### Limitaciones de esta etapa

- Interfaz exclusivamente CLI.
- Flujo muy acoplado a los endpoints disponibles en el payload.
- Lógica de resignación todavía basada en supuestos sobre el formato interno del SFO.
- Gestión de errores y validación de archivos todavía inmaduras.

---

# 2026-06-17 — CLAUDE2

## Primera revisión importante de robustez

El snapshot `CLAUDE2` documenta cuatro correcciones relevantes respecto a la primera implementación.

### FIX-1 — Validación correcta de Account ID

Se corrige el uso incorrecto de:

```python
s.lstrip("0x")
```

Ese método elimina cualquier carácter inicial perteneciente al conjunto `0`, `x` o `X`, en vez de quitar solamente el prefijo literal `0x`.

El problema afectaba especialmente a Account IDs con ceros iniciales.

Se pasa a una validación mediante detección explícita del prefijo.

### FIX-2 — Subidas HTTP en formato raw

Las cargas dejan de enviarse como `multipart/form-data` y pasan a utilizar:

```text
Content-Type: application/octet-stream
```

El cuerpo HTTP contiene directamente los bytes de la imagen o del ZIP.

Esto corrige una incompatibilidad crítica con el servidor, que espera leer el cuerpo directamente para montar una imagen PFS o extraer un ZIP.

### FIX-3 — Preprocesado de `dec.zip`

Se introduce una fase de preparación del ZIP antes de enviarlo a `encrypt_upload`.

Se detecta que el extractor del servidor trabaja correctamente con entradas ZIP `STORED` y no con entradas comprimidas mediante DEFLATE.

El cliente comienza a:

- inspeccionar el ZIP;
- detectar `sce_sys/param.sfo`;
- verificar que el SFO no esté vacío o totalmente a cero;
- reempaquetar el ZIP cuando sea necesario;
- evitar subir archivos que previsiblemente no podrán volver a cifrarse correctamente.

### FIX-4 — Aislamiento de errores en operaciones batch

Un archivo problemático deja de abortar la operación completa. Los procesos por lote pasan a ser tolerantes a fallos individuales.

### Resultado de la etapa

La herramienta deja de ser solamente un cliente funcional de la API y comienza a incorporar lógica de compatibilidad con los formatos reales utilizados por el payload.

---

# 2026-06-17/18 — CLAUDE3 y CLAUDE3.5

## Consolidación de las validaciones de ZIP y SFO

`CLAUDE3` recupera parte de la arquitectura del cliente original y amplía la validación previa de archivos.

### Nuevas capacidades

- `inspect_dec_zip()` para validar un `dec.zip` antes de subirlo.
- Detección de entradas comprimidas no compatibles.
- Validación del `param.sfo`.
- Montaje y lectura del save desde el cliente.
- Mejor tratamiento de exportaciones agrupadas.
- Selección interactiva de archivos.
- Flujo de importación cifrada y descifrado más estructurado.

### CLAUDE3.5

Se introducen helpers orientados a trabajar con el SFO de forma más controlada:

- lectura de campos concretos;
- verificación de validez del SFO;
- generación de un SFO mínimo cuando faltaba;
- reparación/preparación del ZIP.

Al mismo tiempo, parte de la lógica de validación se reorganiza posteriormente en snapshots siguientes. Este es el comienzo de una fase de experimentación intensa con el formato `PARAM.SFO`.

---

# 2026-06-19 — CLAUDE4

## Primera integración explícita de parches en el payload

Esta etapa marca un cambio fundamental: el problema ya no se intenta resolver exclusivamente desde Python. Se modifica también `garlic-savemgr` en C.

Se documentan varias incidencias y sus soluciones.

### Problema central: `Cannot parse param.sfo`

Se identifica que el fallo no siempre estaba en el transporte del archivo, sino en el estado real de `sce_sys/param.sfo` dentro del save.

### Parche A — Auto-heal de `param.sfo`

Se introduce una función equivalente a `regen_param_sfo_from_db()`.

Cuando el SFO aparece zeroed pero existe información válida en la base de datos del sistema, el servidor puede regenerarlo automáticamente antes de continuar.

Esto cambia el comportamiento del flujo de montaje:

```text
mount
  -> detecta SFO zeroed
  -> consulta la BD
  -> regenera SFO
  -> vuelve a parsear
  -> continúa
```

### Parche B — Extractor ZIP basado en Central Directory

Se reemplaza el enfoque original del extractor.

La implementación anterior asumía que los tamaños del archivo ZIP estaban siempre correctamente informados en el Local File Header. Esto falla con ZIPs que utilizan Data Descriptor.

La nueva implementación:

1. localiza el End Of Central Directory;
2. recorre el Central Directory;
3. obtiene el tamaño real y el desplazamiento de cada entrada;
4. usa el Local File Header solamente para localizar los datos.

Esto evita que una entrada mal interpretada desincronice todo el ZIP.

### Parche C — Parcheo dinámico de `ACCOUNT_ID` y `USER_ID`

Se abandona el uso rígido de offsets para localizar campos del SFO.

En lugar de asumir siempre:

- PS5 `ACCOUNT_ID` en `0x1B8`;
- PS4 `ACCOUNT_ID` en `0x15C`;
- PS5 `USER_ID` en `0x660`;

el código comienza a localizar las entradas por nombre dentro de la tabla SFO.

Esto es crítico porque un SFO real puede presentar un orden de campos diferente.

### Impacto

Esta etapa convierte al proyecto en una solución que modifica tanto el cliente como el servidor para resolver incompatibilidades reales del formato y de la implementación original.

---

# 2026-06-19 — CLAUDE5

## Reorganización del cliente CLI y preparación para una GUI

Se consolidan helpers relacionados con:

- búsqueda de raíces de saves;
- preparación de ZIPs;
- detección del tipo de save;
- división de ZIPs que contienen varios saves;
- validación y generación de SFO.

La estructura sigue siendo CLI, pero ya está claramente orientada a una futura interfaz gráfica y a flujos de trabajo reproducibles.

---

# 2026-06-19/20 — CLAUDE6 y CLAUDE7

## Primera GUI completa y ampliación funcional

### CLAUDE6 — migración a PySide6

Se construye la primera GUI real.

Se introducen:

- ventana principal;
- barra lateral de navegación;
- tablas de partidas;
- configuración de conexión;
- selección de partidas;
- botones de exportación e importación;
- barras de progreso;
- comprobación de conexión;
- manejo de tareas fuera del hilo principal.

La aplicación comienza a comportarse como una herramienta de escritorio en lugar de un simple frontend de consola.

### CLAUDE7 — administración más completa

Se incorporan explícitamente:

- consulta de `Account ID` desde la consola;
- consulta de títulos desde la base de datos;
- eliminación de saves;
- eliminación de un título completo;
- pantalla dedicada para borrado;
- información ampliada del save;
- mejor control de operaciones batch;
- validaciones de ZIP y SFO.

### Importante

En esta etapa el producto todavía conserva una filosofía de administrador multipropósito: exportar, importar, descifrar, cifrar, resignar y eliminar.

---

# 2026-06-20 — CLAUDE8 y CLAUDE9

## Ajustes de compatibilidad y reorganización de la lógica de plataforma

`CLAUDE8` mantiene esencialmente la arquitectura de `CLAUDE7` con correcciones y pequeños ajustes.

`CLAUDE9` reorganiza parte de la detección y preparación de saves y recupera helpers de creación de SFO y preparación de ZIP.

### Tendencia de esta fase

El código alterna entre dos objetivos:

- mantener compatibilidad directa con el payload antiguo;
- reducir la complejidad de la GUI y hacer el flujo de preparación de saves más determinista.

Es una etapa de transición antes de la gran reorganización de `v2/v3`.

---

# 2026-06-21 — CLAUDE10 / CLAUDE11

## Garlic SaveMgr GUI v2.0

Se formaliza por primera vez una versión de GUI identificada como `2.0`.

### Arquitectura

La aplicación pasa a trabajar con un flujo por etapas:

1. arranque y configuración;
2. escaneo de saves;
3. escaneo profundo de Account ID;
4. selección de operaciones;
5. procesamiento en segundo plano.

### Interfaz

Aparecen páginas diferenciadas para:

- Partidas;
- Usuarios / IDs;
- Importar;
- Exportar;
- Resignar;
- Cifrar `dec.zip`;
- Log del servidor;
- Ajustes.

### Experiencia de usuario

Se añaden:

- navegación lateral;
- tablas con selección por casilla;
- menús contextuales;
- exportación ENC/DEC/AMBOS;
- feedback de progreso;
- tabla de usuarios y Account IDs;
- tema claro/oscuro;
- trabajo en `QThread` para no bloquear la interfaz.

### Estado funcional

La GUI v2 sigue ofreciendo resignación y soporte PS4/PS5.

---

# 2026-06-21 — CLAUDE12

## Replanteamiento del flujo como proceso técnico de cinco etapas

El snapshot `CLAUDE12` define explícitamente el flujo:

```text
LEER → DESCIFRAR → EXTRAER → REFIRMAR → CARGAR
```

Este concepto acabaría convirtiéndose en la base de la arquitectura posterior.

### Primera formalización del modelo de trabajo

Se introduce además una estructura de trabajo persistente:

- `saves/` para datos;
- `work/` para temporales;
- `logs/` para trazabilidad.

### Configuración

Se empieza a almacenar información del destino:

- Account ID;
- User ID;
- nombre;
- preferencias de conservación de ENC/DEC.

La herramienta ya no se limita a ejecutar operaciones aisladas. Empieza a representar una transferencia de un save desde un estado de origen hasta un estado de destino.

---

# 2026-06-21 — CLAUDE13 / CLAUDE14 / CLAUDE15

## Gran reestructuración y profundización en el SFO

Esta fase concentra una parte importante de la ingeniería de bajo nivel.

### CLAUDE13

Se recupera una implementación GUI más madura y se incorporan:

- normalización de `dec.zip`;
- lectura de parámetros SFO;
- generación de SFO PS5;
- manejo de importaciones y exportaciones con callbacks.

### CLAUDE15 — punto de inflexión técnico

Se documentan formalmente los parches al payload y se añade una función crítica: el manejo correcto de `ACCOUNT_ID` como bytes.

#### Parche D — corrección de byte order

El parche anterior de `ACCOUNT_ID` había convertido los bytes a un `uint64_t` con una representación que terminaba escribiéndose invertida en una arquitectura little-endian.

El fix consiste en preservar los ocho bytes tal y como deben aparecer en el SFO y escribirlos directamente.

Se añade además un wrapper específico para los casos donde el origen del dato sí es un `uint64_t` nativo del sistema.

#### Parche E — `/api/account_ids`

Aparece un endpoint para devolver:

- UID;
- nombre;
- Account ID;

leyéndolos directamente del registro de sistema, sin necesidad de montar un save.

#### Nueva experiencia de usuario

El cliente empieza a ofrecer un selector de Account IDs en vez de obligar al usuario a escribir el valor manualmente.

#### Gestión de títulos y ZIPs

Se incorpora lógica para:

- distinguir exportaciones de título completo de exportaciones de un único save;
- eliminar prefijos de título de rutas internas del ZIP;
- seleccionar un save concreto cuando un ZIP contiene varios.

#### Eliminación

Aparece una operación explícita para eliminar un save del sistema.

---

# 2026-06-21 — CLAUDE16 / CLAUDE17 / CLAUDE18

## Experimentación con gestión de múltiples consolas

Se introducen estructuras para guardar una colección de consolas y diferenciar sus papeles.

### Modelo Fuente/Destino

La aplicación llega a representar explícitamente:

- consola FUENTE;
- consola DESTINO;
- operaciones de preparación;
- carga posterior.

### Características

- añadir, editar, eliminar y mover consolas;
- conexión independiente con cada consola;
- comprobación de ambos extremos;
- selección de consola origen/destino;
- mantenimiento de Account ID y User ID asociados a cada perfil.

Esta es la base de la arquitectura que posteriormente se formalizaría como `v2.1` y `v3.0`.

---

# 2026-06-21 — CLAUDE19 / CLAUDE20

## Garlic Manager v2.1 — flujo de dos fases

El proceso pasa a describirse como:

### Fase 1 — PREPARAR

```text
E1 — Leer
E2 — Descifrar
E3 — Extraer
E4 — Refirmar / Cifrar
```

### Fase 2 — CARGAR

```text
E5 — Cargar en la consola destino
```

Se introducen pantallas específicas para:

- preparar;
- cargar;
- flujo completo;
- cola de trabajos;
- configuración;
- consolas;
- listado de saves;
- log del servidor.

### Detección y resolución de propietarios

Se empieza a automatizar la identificación del Account ID y User ID del save en lugar de pedir siempre los valores al usuario.

---

# 2026-06-21/25 — CLAUDE21 a CLAUDE24

## Garlic SaveMgr GUI v3.0

Aquí se consolida la arquitectura que durante la siguiente semana se utilizaría como plataforma de pruebas.

### Modelo definitivo de flujo

La documentación de la etapa establece:

```text
FASE 1 — consola FUENTE
  E1 Leer
  E2 Descifrar
  E3 Extraer + parchear
  E4 Cifrar

FASE 2 — consola DESTINO
  E5 Cargar
```

### Objetivo

Separar completamente:

- obtención de la partida;
- preparación en PC;
- cifrado en la consola adecuada;
- instalación final.

### Detección de plataforma

Se empieza a formalizar una jerarquía de detección para distinguir PS4 y PS5:

1. `FORMAT` del SFO;
2. prefijo del `title_id`;
3. nombre del archivo.

### Manejo de PS4

Se añaden elementos específicos para:

- detectar sesión PS4;
- localizar información de PS4 en el SFO;
- conservar el `.bin` asociado;
- elegir rutas y operaciones distintas según plataforma.

### Filtrado y verificación

La GUI incorpora filtros para trabajar con saves de PS4/PS5 y empieza a preparar una fase de verificación posterior a la carga.

---

# 2026-06-25/27 — CLAUDE25 a CLAUDE30

## Endurecimiento de la detección de plataforma y verificación

Durante estos snapshots se repiten y refinan varias ramas de implementación. El resultado importante no es una sola función, sino la estabilización del flujo.

### Detección basada en SFO

Se abandona progresivamente la dependencia de offsets fijos y nombres de archivo para usar el contenido real del SFO.

Se consolidan helpers equivalentes a:

- lectura de campos SFO;
- detección del formato;
- identificación del `TITLE_ID`;
- detección de PS4/PS5 desde un `dec.zip`;
- detección de PS4/PS5 desde una imagen cifrada.

### Verificación posterior

`CLAUDE28` introduce una fase explícita de verificación.

El sistema puede volver a montar la partida en destino y comprobar:

- Account ID;
- User ID;
- coherencia del propietario real frente al esperado.

La interfaz empieza a representar el resultado como una tabla de comprobación.

### Documentación técnica

`CLAUDE29` incluye una guía de trabajo detallada en `INSTRUCCIONES.md`, que formaliza:

- el flujo E1–E5;
- el tratamiento de PS4/PS5;
- el orden de detección de plataforma;
- las rutas de filesystem;
- los endpoints usados;
- los casos de SFO zeroed;
- errores conocidos como `CE-107173-9`.

### Cambio conceptual importante

El proyecto empieza a priorizar la integridad del proceso y la verificación post-carga por encima de la disponibilidad indiscriminada de todas las operaciones.

---

# 2026-07-01 — CLAUDE32

## Consolidación del flujo de preparación

Se simplifican varios helpers específicos de la fase PS4 y se mantiene el tratamiento general del SFO desde un punto común.

### Mejoras

- filtrado de saves más robusto;
- identificación de backups locales;
- renderizado más consistente de tablas;
- parcheo de `SAVEDATA_BLOCKS` en ciertos escenarios;
- simplificación de la lógica del SFO.

Esta etapa prepara la transición desde el administrador multipropósito hacia una aplicación más específica de backup/restore.

---

# 2026-07-04/05 — CLAUDE33 / CLAUDE34 / CLAUDE35

## Investigación profunda del formato de savedata

Esta es una de las fases técnicas más importantes del proyecto.

### Catálogo de APIs

Se crea un catálogo explícito de los endpoints usados por el cliente y se contrastan contra `main.c`.

Entre los endpoints documentados aparecen:

- `/api/status`
- `/api/users`
- `/api/saves`
- `/api/mount`
- `/api/unmount`
- `/api/download`
- `/api/db_titles`
- `/api/encrypt_upload`
- `/api/encrypt_download`
- `/api/import_encrypted`
- `/api/import_finish`
- `/api/upload_key`
- `/api/regen_sfo`
- `/api/log`

### Catálogo de bugs

Se documentan errores reales encontrados durante las pruebas, entre ellos:

- exportación PS4 en formato incorrecto;
- cifrado en la consola equivocada;
- JSON no escapado;
- `SAVEDATA_BLOCKS` insuficiente;
- offsets de Account ID incorrectos;
- UID PS4 incorrecto;
- problemas de importación cross-console.

### Investigación PFS

Se formaliza el conocimiento sobre:

- offsets del superblock;
- claves selladas;
- tamaño de bloque `0x10000`;
- relación entre `SAVEDATA_BLOCKS` y tamaño de imagen;
- diferencias entre PS4 BC y PS5 nativo.

### Investigación PS4

Se contrastan los comportamientos con Apollo PS4 y otras implementaciones para determinar qué partes del modelo PS4 podían trasladarse realmente a PS5 BC.

### Decisión técnica relevante

Se descarta implementar `sceSaveDataTransferCopy()` como solución genérica en PS5 porque la API no está disponible de la misma forma en PS5 BC.

La estrategia pasa a ser:

```text
extraer -> preparar -> cifrar en destino -> importar
```

en lugar de intentar replicar un mecanismo PS4 no disponible.

---

# 2026-07-10 — CLAUDE36 — Garlic SaveMgr v4.0

## Reescritura del cliente

Aquí comienza la segunda gran generación del producto.

La GUI v3 y su arquitectura de múltiples consolas se consideran demasiado complejas para el objetivo operativo final.

### Nueva filosofía

El cliente se centra en la gestión de saves como backup y restauración, con menos operaciones manuales y menos conceptos internos expuestos al usuario.

### Cambios principales

- reescritura importante del cliente;
- bootstrap automático de dependencias;
- PySide6 + `requests` como base;
- logs estructurados;
- carpeta de trabajo temporal;
- tratamiento de ZIP en memoria/archivos de trabajo;
- tablas de consola y cuentas;
- nuevas pestañas y controles simplificados;
- escaneo de títulos desde `/api/saves`;
- lectura y preparación de saves de manera más controlada.

### Eliminaciones / reducción de alcance

Esta generación empieza a retirar o a dejar fuera de la interfaz varias funciones heredadas:

- gestión explícita de varias consolas;
- flujo Fuente/Destino como entidades independientes del GUI;
- cola compleja de trabajos;
- varias rutas de resignación genérica;
- partes del soporte PS4 que habían complicado la arquitectura.

Este recorte no es un retroceso accidental: es un cambio deliberado de producto.

---

# 2026-07-14 — CLAUDE37 — Garlic SaveMgr v4.1

## Simplificación y correcciones de compatibilidad

Se mantiene el nuevo enfoque, pero se recuperan algunas piezas útiles de compatibilidad interna.

### Presentes

- detección de saves;
- agrupación por título;
- exportación raw;
- utilidades relacionadas con PS4;
- operaciones de resignación a nivel de API interno.

### Importante

Aunque algunos helpers siguen presentes, la aplicación ya no vuelve al antiguo modelo de "administrador universal". La dirección del proyecto es claramente hacia backup/restore controlado.

---

# 2026-08-26 — CLAUDE38 — Garlic SaveMgr v5.0

## Cambio de producto: de Save Transfer a Backup/Restore

La versión `5.0` elimina explícitamente varias capacidades heredadas.

### Nuevo objetivo

```text
Copia de seguridad de saves PS5
            ↓
Almacenamiento local
            ↓
Restauración segura
```

### Se elimina o deja fuera del producto

- resignación genérica desde el GUI;
- detección y manipulación directa de PS4;
- carga de claves PS4;
- helpers de SFO destinados a parchear la plataforma;
- soporte específico de `sdimg_*`;
- modelo multipropósito de administración de PS4/PS5.

### Se mantiene

- bootstrap de dependencias;
- conexión HTTP al payload;
- consulta de cuentas;
- consulta de saves;
- escaneo de títulos;
- exportación de backups;
- importación/restauración.

Este es el punto en el que el proyecto adquiere la identidad conceptual que mantiene en las versiones 6.x.

---

# 2026-08-26 — CLAUDE39 — Garlic SaveMgr v6.0

## Introducción de seguridad de perfil

`v6.0` introduce el mecanismo que define el comportamiento actual de restauración.

### Regla principal

El cliente no realiza refirmado de partidas.

Una copia solo se puede restaurar cuando el perfil de origen registrado en el backup coincide con un perfil válido disponible en la consola de destino.

Los campos considerados pueden variar según la API:

- `uid`;
- `id`;
- `account_id`;
- `aid`.

### Nuevo modelo de backup

Los backups pasan a disponer de metadatos laterales (`sidecar`) para conservar información del propietario y del contenido.

### Nuevas pestañas

Se consolidan dos áreas principales:

- **Copia de seguridad**
- **Restaurar**

### Verificación previa a la restauración

Antes de modificar la consola, el cliente analiza las copias seleccionadas y verifica que el perfil esperado exista.

Si alguna no coincide, la restauración completa se aborta antes de comenzar.

### Objetivo de seguridad

Evitar que una operación equivocada pueda terminar escribiendo un save sobre una cuenta distinta o crear un escenario difícil de diagnosticar posteriormente.

---

# 2026-08-26 — CLAUDE40 — Garlic SaveMgr v6.1

## Corrección de estabilidad

Se corrigen problemas detectados durante el uso real de `v6.0`.

El trabajo se centra en:

- progreso;
- actualización de la barra durante las operaciones;
- callbacks de red;
- estabilidad general de las tareas de backup/restore.

El sistema comienza a usar de forma más consistente callbacks seguros y actualización de UI fuera del hilo de red.

---

# 2026-08-27 — CLAUDE41 — Garlic SaveMgr v6.2

## Eliminación del modelo Fuente/Destino

Esta es una de las decisiones arquitectónicas más relevantes de la serie 6.x.

### Antes

El programa podía mantener varias consolas y distinguir entre "Fuente" y "Destino".

### Después

Se utiliza una sola consola configurable en Ajustes.

La misma conexión sirve para:

- escanear;
- crear backups;
- restaurar.

### Consecuencia

La configuración antigua deja de ser directamente migrable: las claves usadas por `v6.0/v6.1` no se reutilizan automáticamente.

### Ventaja

La aplicación pasa a reflejar mejor su propósito real: **backup y restore sobre una consola configurada**, no gestión de un laboratorio de transferencia entre múltiples PS5.

---

# 2026-08-27 — CLAUDE42

## Endurecimiento de callbacks

Se añade una capa específica de callback seguro para evitar actualizaciones de interfaz desde contextos no adecuados.

### Objetivo

Evitar:

- errores intermitentes de Qt;
- actualizaciones de widgets desde hilos incorrectos;
- estados visuales inconsistentes durante operaciones largas.

La infraestructura asíncrona pasa a ser más robusta y predecible.

---

# 2026-08-27 — CLAUDE43 — Garlic SaveMgr v6.3

## Corrección del diagnóstico de perfil

Se detecta un problema importante: una restauración legítima procedente de la misma consola también podía ser rechazada.

### Causa

El código de comprobación no siempre interpretaba de la misma forma los distintos nombres de campo usados por las respuestas de la API.

### Solución

Se añaden mecanismos para:

- detectar qué campos de identidad existen realmente;
- normalizar valores;
- seleccionar el valor correcto para reenviarlo al import.

### Resultado

La comparación deja de depender de que el servidor utilice exactamente una clave concreta.

---

# 2026-08-28 — CLAUDE44 — Garlic SaveMgr v6.4

## Revisión de `/account_ids`

Se descubre otra inconsistencia de API.

El endpoint `/account_ids` podía devolver una estructura JSON distinta de la que el cliente suponía, pese a responder con HTTP 200.

### Problema

El cliente leía siempre la clave `users`, que coincidía con otro endpoint pero no necesariamente con `/account_ids`.

### Solución

Se amplía el parser para reconocer diferentes nombres plausibles de colección y normalizar el resultado.

### Mejora de diagnóstico

El mensaje "`/account_ids` no disponible" deja de aparecer como alarma cuando la información sí se pudo recuperar por otro formato válido.

---

# 2026-08-28 — CLAUDE45 — Garlic SaveMgr v6.5

## Coincidencia robusta de identidad

Se corrige un segundo nivel del problema de perfil.

### Causa raíz

`/api/saves` podía identificar al propietario mediante un campo distinto del utilizado por la lista de cuentas.

Por tanto, el sistema podía tener:

- una identidad válida en el backup;
- un perfil válido en la consola;
- pero dos nombres de campo distintos;
- y concluir incorrectamente que no existía coincidencia.

### Solución

Se introduce normalización de campos de identidad y un modelo de comparación capaz de trabajar con:

- `uid`;
- `id`;
- `account_id`;
- `aid`.

La información que produce la coincidencia se conserva para reutilizarla al realizar la restauración.

### Resultado

La restauración de una copia legítima deja de depender del nombre exacto de la propiedad JSON utilizada por una determinada versión del payload.

---

# 2026-08-28/29 — CLAUDE46 — Garlic SaveMgr v6.6

## Nueva función: eliminar saves

La aplicación deja de ser únicamente un administrador de backup/restore y añade operaciones de limpieza controlada.

### Eliminar de consola

En la pestaña **Copia de seguridad** aparece el botón:

```text
Eliminar de consola
```

Permite seleccionar uno o varios títulos y eliminar directamente los saves de la PS5.

### Seguridad del borrado

- requiere confirmación explícita;
- utiliza un pipeline separado;
- elimina los índices de mayor a menor para evitar desplazamientos al borrar entradas consecutivas.

### Eliminar copia local

En la pestaña **Restaurar** se incorpora:

```text
Eliminar copia local
```

Permite borrar las copias seleccionadas almacenadas en el PC.

### Limpieza del diagnóstico

El aviso sobre la ausencia de `/account_ids` deja de ocupar espacio en el panel principal cuando ya existe una vía alternativa válida para obtener los perfiles.

---

# 2026-08-29 — CLAUDE47

## Capa HTTP de borrado

Se añade una operación DELETE interna al cliente para separar claramente:

- GET;
- POST;
- DELETE.

La intención es representar correctamente semánticamente las operaciones de administración.

Esta implementación, sin embargo, descubre posteriormente una incompatibilidad con el payload real.

---

# 2026-08-29 — CLAUDE48 — Garlic SaveMgr v6.6.1

## Corrección crítica de eliminación

Se localiza el último bug funcional de la rama 6.6.

### Problema

El cliente intentaba realizar:

```http
DELETE /api/delete?idx=N
```

pero el payload no dispone de ese endpoint ni de ese método HTTP.

### Endpoint correcto

La API real utiliza:

```http
GET /api/delete_save?idx=N
```

### Fix

La lógica de borrado vuelve a utilizar el cliente GET y la ruta correcta.

### Impacto

`v6.6` podía mostrar correctamente la interfaz de borrado, pero no eliminaba realmente el save debido a respuestas `404`.

`v6.6.1` corrige esa incompatibilidad y convierte la función de eliminación en operativa.

---

# 2026-08-29 — CLAUDE49

## Snapshot de empaquetado final

`CLAUDE49` conserva esencialmente la implementación funcional de `v6.6.1`, pero renombra el archivo principal del cliente a:

```text
Garlic_SaveMgr_main.py
```

La lógica funcional es equivalente al snapshot anterior.

Este snapshot representa el estado de trabajo utilizado como referencia para el repositorio público actual.

---

# Evolución funcional por áreas

## Backup / Exportación

### Etapa inicial

Desde el primer cliente CLI ya era posible exportar:

- imágenes cifradas;
- ZIPs descifrados;
- títulos completos.

### Evolución

Con el tiempo se añadieron:

- operaciones batch;
- selección múltiple;
- agrupación por título;
- metadatos laterales;
- validación de integridad;
- almacenamiento estructurado en el PC;
- UI específica para backups.

### Estado actual

El backup está claramente orientado a generar una copia local trazable, conservando la información necesaria para decidir posteriormente si una restauración es segura.

---

## Restore / Importación

### Etapa inicial

La importación se hacía directamente sobre imágenes cifradas, y posteriormente también se automatizó el flujo `dec.zip -> cifrado -> importación`.

### Etapa de transferencia entre consolas

Se llegó a un flujo completo E1–E5 con consola origen y consola destino.

### Estado actual

La restauración ya no intenta ser un proceso de resignación arbitraria. La decisión depende primero de la coincidencia del perfil de origen con un perfil existente en la consola.

La restauración es, por diseño, conservadora.

---

## Resignación

### Existía en las primeras generaciones

La CLI inicial podía:

- cambiar Account ID;
- cambiar User ID;
- resignar lotes;
- descargar imágenes resignadas.

### Auge de la función

Durante `v2/v3` se convirtió en una parte importante de la preparación de transferencias entre consolas.

### Eliminación

A partir de `v5.0`, la resignación deja de ser una función de usuario del producto.

El motivo arquitectónico es claro: la aplicación moderna no pretende editar o refirmar arbitrariamente un save, sino restaurar únicamente una copia cuyo propietario sea coherente con el perfil de destino.

### Estado actual

**No existe resignación interactiva en el producto v6.x.**

Quedan elementos históricos y de compatibilidad en snapshots anteriores, pero no forman parte del flujo de uso actual.

---

## PS4

### Existía desde el inicio

Los primeros clientes soportaban explícitamente:

- `sdimg_*`;
- `.bin` companion;
- detección PS4;
- resignación PS4;
- importación PS4;
- parámetros `ps4=1`.

### Investigación profunda

Se llegó a documentar el uso de saves PS4 en modo BC de PS5 y sus particularidades.

### Abandono

Con `v5.0` el soporte directo PS4 deja de formar parte del producto.

### Estado actual

El administrador moderno es deliberadamente **PS5-only**.

Esto reduce considerablemente la complejidad del código y elimina rutas de procesamiento que habían generado una parte significativa de los bugs históricos.

---

## Account ID / User ID

La gestión de identidad es probablemente el área que más evolución ha sufrido.

### Primera etapa

Valores introducidos manualmente por el usuario.

### Segunda etapa

Lectura automática desde SFO y desde el registro de sistema.

### Tercera etapa

Parcheo dinámico por nombre de campo en lugar de offsets fijos.

### Cuarta etapa

Normalización entre distintas respuestas JSON de la API.

### Quinta etapa

Verificación de perfil previa a la restauración.

### Estado actual

El producto utiliza la identidad como **mecanismo de seguridad**, no como simple dato informativo.

---

## PARAM.SFO

### Evolución

El tratamiento pasó de asumir layouts conocidos a interpretar la tabla real del SFO.

Se añadieron:

- búsqueda dinámica de campos;
- regeneración desde la BD;
- detección de SFO zeroed;
- reparación automática en el servidor;
- validación previa en el PC;
- corrección de byte order;
- extracción de `FORMAT` y `TITLE_ID`.

### Resultado

El proyecto deja atrás el paradigma de "offset conocido" y pasa a tratar el SFO como una estructura binaria real.

---

## ZIP / Decoding

### Problemas históricos

- `multipart/form-data` enviado a un endpoint que esperaba bytes raw;
- ZIPs DEFLATE no compatibles con el extractor;
- Data Descriptor ignorado;
- `param.sfo` ausente o zeroed;
- prefijos de título en rutas internas;
- múltiples saves dentro del mismo archivo.

### Soluciones

- `application/octet-stream`;
- reempaquetado como `STORED`;
- extracción basada en Central Directory;
- reparación de SFO;
- detección y separación de saves;
- normalización de rutas.

Esta área fue una de las mayores fuentes de errores y finalmente quedó mucho más robusta.

---

## API HTTP

La relación del cliente con el payload evolucionó desde llamadas directas poco abstractas hasta una capa de API más definida.

### Primera etapa

Llamadas individuales y específicas para cada operación.

### Etapa intermedia

Catálogo de APIs y helpers de cliente.

### Etapa actual

La aplicación utiliza una superficie pequeña y controlada de endpoints, orientada a:

- status;
- usuarios;
- saves;
- lectura;
- backup;
- restore;
- borrado;
- diagnóstico.

El objetivo ya no es exponer todas las capacidades del payload, sino aquellas necesarias para el producto final.

---

# Funciones que existieron y posteriormente desaparecieron

## Eliminadas del producto moderno

| Función | Históricamente disponible | Estado actual |
|---|---:|---|
| Resignar Account ID | Sí | Eliminada del flujo v6.x |
| Resignar User ID | Sí | Eliminada del flujo v6.x |
| Resign ALL | Sí | Eliminada |
| Cifrar + importar como operación manual independiente | Sí | Simplificada dentro del restore |
| Gestión de varias consolas | Sí | Eliminada en v6.2 |
| Modelo Fuente/Destino | Sí | Eliminado en v6.2 |
| Soporte directo PS4 | Sí | Eliminado desde v5.0 |
| `sdimg_*` | Sí | Fuera del producto actual |
| `.bin` PS4 companion | Sí | Fuera del producto actual |
| Selector manual de plataforma PS4/PS5 | Sí | Eliminado por especialización PS5 |
| Parches interactivos de SFO para resign | Sí | Fuera del producto moderno |
| Gestión de cola compleja E1–E5 | Sí | Sustituida por pipeline backup/restore |

---

# Funciones nuevas del producto moderno

## Perfil de restauración seguro

La funcionalidad más importante añadida en la serie 6.x no es una operación de archivo, sino una política de seguridad.

Antes de restaurar:

1. se identifica el propietario de la copia;
2. se consulta el perfil disponible en la consola;
3. se normalizan los campos de identidad;
4. se exige coincidencia;
5. si no existe una coincidencia válida, se aborta toda la operación.

Esto evita que una copia incompatible llegue a modificar el sistema.

## Sidecars de metadatos

Las copias locales conservan información adicional junto al archivo principal, permitiendo recuperar el contexto del backup sin tener que interpretar de nuevo todo el save en cada restauración.

## Eliminación controlada

`v6.6/v6.6.1` añaden por primera vez un flujo de borrado integrado con:

- selección múltiple;
- confirmación;
- eliminación ordenada de índices;
- borrado local de backups.

## Logging persistente

La aplicación mantiene logs por sesión para facilitar el diagnóstico de errores de red, perfil y operaciones de almacenamiento.

## Bootstrap automático

Las versiones modernas comprueban e instalan automáticamente:

- PySide6;
- requests.

Esto reduce el trabajo de configuración inicial para usuarios de Windows.

---

# Arquitectura: evolución resumida

## Generación 1 — CLI

```text
Python
  |
  +-- requests
  |
  +-- rich
  |
  +-- garlic-savemgr HTTP API
```

Objetivo: disponer rápidamente de acceso a la API.

## Generación 2 — GUI

```text
PySide6 GUI
    |
    +-- GarlicAPI
    |
    +-- workers / callbacks
    |
    +-- garlic-savemgr
```

Objetivo: mejorar la operativa diaria.

## Generación 3 — Transferencia entre consolas

```text
FUENTE
  E1 -> E2 -> E3 -> E4
                    |
                    v
DESTINO
                    E5
```

Objetivo: automatizar transferencias y resignaciones.

## Generación 4 — Backup/Restore seguro

```text
PS5
 |
 +-- Scan
 +-- Backup
 |     |
 |     +-- metadata
 |
 +-- Restore
       |
       +-- profile validation
       |
       +-- safe import
```

Objetivo: convertir Garlic SaveMgr en una herramienta de backup fiable, no en una herramienta genérica de manipulación de saves.

---

# Resumen de hitos principales

| Fecha | Versión / Snapshot | Hito |
|---|---|---|
| 16/06/2026 | CLAUDE0 | Primera CLI funcional |
| 17/06/2026 | CLAUDE2 | Primer gran paquete de fixes de transporte y ZIP |
| 19/06/2026 | CLAUDE4 | Parches C del payload: SFO, ZIP, reparación automática |
| 20/06/2026 | CLAUDE7 | Primera GUI completa y borrado |
| 21/06/2026 | v2.0 | GUI estructurada por etapas |
| 21/06/2026 | v2.1 | Modelo Fuente/Destino |
| 21–29/06/2026 | v3.0 | Flujo E1–E5 y transferencia entre consolas |
| 04–05/07/2026 | CLAUDE33–35 | Investigación profunda PFS/SFO/PS4 |
| 10/07/2026 | v4.0 | Reescritura del cliente |
| 14/07/2026 | v4.1 | Estabilización |
| 26/08/2026 | v5.0 | Producto PS5-only, backup/restore |
| 26/08/2026 | v6.0 | Validación de perfil antes de restaurar |
| 26/08/2026 | v6.1 | Estabilidad y progreso |
| 27/08/2026 | v6.2 | Eliminación del modelo Fuente/Destino |
| 27/08/2026 | v6.3 | Normalización de perfiles |
| 28/08/2026 | v6.4 | Compatibilidad de `/account_ids` |
| 28/08/2026 | v6.5 | Coincidencia robusta de campos de identidad |
| 28/08/2026 | v6.6 | Borrado de saves y backups |
| 29/08/2026 | v6.6.1 | Corrección definitiva del endpoint de borrado |

---

# Filosofía actual del proyecto

La evolución completa muestra un cambio de objetivo muy claro.

El proyecto comenzó como un cliente que intentaba exponer prácticamente todas las capacidades de `garlic-savemgr`, incluidas funciones delicadas de resignación, manipulación de SFO y soporte simultáneo de PS4 y PS5.

La experiencia obtenida durante las pruebas reales demostró que ese enfoque generaba demasiados caminos de fallo:

- distintos layouts SFO;
- diferencias PS4/PS5;
- claves específicas de consola;
- respuestas JSON inconsistentes;
- ZIPs con formatos distintos;
- operaciones realizadas sobre la consola incorrecta;
- riesgo de restaurar una copia con el perfil equivocado.

La serie moderna responde a esos problemas reduciendo deliberadamente el alcance.

**Garlic SaveMgr actual no intenta hacerlo todo. Intenta hacer correctamente un conjunto pequeño de operaciones críticas.**

La versión `6.6.1` representa, por tanto, un producto mucho más definido que las primeras generaciones:

- PS5-only;
- backup local;
- restore con comprobación previa de identidad;
- gestión de copias;
- eliminación controlada;
- logs para diagnóstico;
- comunicación local con el payload;
- sin resignación arbitraria desde la interfaz.

---

# Estado histórico de compatibilidad

## PS5

**Estado actual:** objetivo principal y único del producto moderno.

## PS4 en PS5 BC

**Estado histórico:** soportado experimentalmente en múltiples generaciones, con investigación profunda y varios parches específicos.

**Estado actual:** fuera del alcance de v5/v6.

## Resignación cross-console

**Estado histórico:** parte central de v2/v3.

**Estado actual:** reemplazada por validación de perfil y restauración segura.

---

# Consideraciones para futuros releases

A partir de `v6.6.1`, el proyecto debería tratar la documentación y el versionado público como parte de la ingeniería, no como una tarea secundaria.

Las futuras versiones deberían distinguir claramente entre:

- **Added** — nueva funcionalidad;
- **Changed** — cambio de comportamiento;
- **Fixed** — corrección;
- **Removed** — retirada de funcionalidad;
- **Security / Safety** — cambios que afectan a integridad o seguridad de restauración;
- **Compatibility** — cambios específicos de payload/API;
- **Documentation** — cambios del repositorio y guía de usuario.

Esto ayudará a que el historial público no vuelva a mezclar snapshots de desarrollo, reescrituras arquitectónicas y releases.

---

# Referencia de producto actual

**Garlic SaveMgr v6.6.1** es la culminación de una transición desde un cliente experimental de acceso a la API hacia una herramienta especializada de backup y restauración de saves de PS5.

La característica que mejor define la versión actual no es solamente la capacidad de copiar o restaurar archivos, sino la decisión de **no restaurar una copia cuando la identidad del propietario no puede verificarse de forma consistente**.

Ese cambio de filosofía constituye el principal salto de madurez del proyecto.
