# Garlic SaveMgr

**Garlic SaveMgr 6.8.7.50** es un cliente de escritorio para Windows que gestiona copias de seguridad y restauraciones de partidas de PS5 mediante la red local y Garlic.

[![Build Windows](https://github.com/RastaFairy/Garlic-SaveMgr/actions/workflows/build-windows.yml/badge.svg)](https://github.com/RastaFairy/Garlic-SaveMgr/actions/workflows/build-windows.yml)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](./LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)](https://github.com/RastaFairy/Garlic-SaveMgr)

## Estado actual

| Campo | Valor |
|---|---|
| Producto | Garlic SaveMgr |
| Revisión | **6.8.7.50** |
| Versión de producto | 6.8.7 |
| Plataforma | Windows x64 |
| UI | WPF |
| Runtime | .NET 8 |
| Arquitectura | Host estable + módulos DLL autónomos |

> Este árbol es **fuente de GitHub**. No incluye `bin/`, `obj/`, `publish/`, datos de usuario, backups, cachés ni payloads generados localmente.

## Arquitectura

```text
Garlic_SaveMgr.exe
        │
        │ IModuleHostContext / IExecutableModule / ModuleEntry
        ▼
┌────────────┬────────────┬────────────┬────────────┐
│ Seguridad  │ Papelera   │ Salud      │ Actualizador│
│ Module.dll │ Module.dll │ Module.dll │ Module.dll │
└────────────┴────────────┴────────────┴────────────┘
        │
        └── servicios core compartidos
            GarlicSaveMgr/Services
```

El host es el shell estable. Los módulos son autónomos y pueden reemplazarse de forma independiente mientras el contrato común permanezca estable.

## Módulos oficiales

- **Seguridad**: auditoría e integridad de backups.
- **Papelera**: gestiona por separado Papelera PS5 y Papelera PC.
- **Salud**: observabilidad global de host, módulos, operaciones y estado funcional.
- **Actualizador**: consulta releases y actualiza el ejecutable principal.

## Mandatos funcionales no negociables

Estas reglas tienen prioridad sobre refactorizaciones cosméticas:

1. **Papelera PS5 y Papelera PC son almacenes independientes.**
2. **GUARDAR COPIA no crea ni refresca ninguna Papelera.**
3. `backups-changed` afecta a backups activos; `ps5-trash-changed` solo a la Papelera PS5; `pc-trash-changed` solo a la Papelera PC.
4. **RESTORE no es una operación destructiva automática** y no mueve por sí solo un backup a Trash.
5. SHA-256 es una frontera de integridad cuando el flujo la exige.
6. El tamaño de un backup físico procede de `FileInfo.Length` del `.img`; un Snapshot es histórico, no una segunda copia.
7. Smart Backup es conservador: ante duda, copia.
8. El descubrimiento LAN trata las 1.275 direcciones como una fase lógica con concurrencia acotada.
9. Un módulo opcional ausente o fallido no debe impedir el arranque del núcleo.
10. Las carátulas nunca deben bloquear Backup, Restore o Trash.
11. **Salud observa, analiza, informa y recomienda; no ejecuta automáticamente acciones destructivas.**

## Salud

Salud no es solamente un panel de backups. Es la capa de **observabilidad global** de Garlic SaveMgr.

Vigila, cuando existe información suficiente:

- host y dispatcher;
- módulos cargados, ausentes o fallidos;
- excepciones recientes;
- operaciones asíncronas y operaciones atascadas;
- pipeline de carátulas y solicitudes sin actividad;
- conexiones y descubrimiento;
- almacenamiento e integridad;
- backups, snapshots e histórico;
- tendencias y recomendaciones.

### HEALTH ≠ DIAGNOSTICS

`HEALTH` responde a **"¿está funcionando correctamente ahora?"**.

`DIAGNOSTICS` responde a **"¿qué merece atención o qué ha ocurrido?"**.

Por ello, información puramente histórica no degrada la salud por sí misma. Un snapshot `history-only`, una carátula no encontrada o un módulo opcional ausente pueden ser datos de diagnóstico sin reducir el score.

Una anomalía solo afecta a `HEALTH` mediante un **`HealthImpact` explícito**. Un `Warning` genérico no resta puntos automáticamente.

Consulte [docs/SALUD.md](docs/SALUD.md).

## Carátulas

Las carátulas se resuelven por `TitleId`, con caché y deduplicación. El pipeline distingue `QUEUED`, `RUNNING`, `COMPLETED`, `NOT_FOUND`, `FAILED` y `STALLED`.

Una portada que no aparece no debe bloquear otras operaciones. Una tarea que permanece `RUNNING` sin actividad puede ser diagnosticada por Salud como atascada.

## Compilación

Requisitos:

- Windows x64.
- .NET 8 SDK.
- PowerShell.

Compilación completa, tests y publicación:

```powershell
.\build.ps1
```

Compilación aislada de un módulo, con el host ya compilado:

```powershell
.\build-one-module.ps1 Salud
```

Módulos válidos: `Security`, `Trash`, `Salud`, `Updater`.

Consulte [docs/BUILD.md](docs/BUILD.md).

## Estructura

```text
GarlicSaveMgr.sln
GarlicSaveMgr/                 # host y servicios core
Modules/
  Security/                    # seguridad
  Trash/                       # papeleras
  Salud/                       # observabilidad
  Updater/                     # actualizador
GarlicSaveMgr.Tests/           # regresiones
Themes/                        # plantilla pública de temas
.github/                       # CI y plantillas GitHub
docs/                          # documentación canónica
```

## Documentación

- [PROJECT.md](PROJECT.md) — contrato funcional y reglas canónicas.
- [MODULES.md](MODULES.md) — arquitectura modular.
- [docs/SALUD.md](docs/SALUD.md) — contrato de observabilidad.
- [docs/FLOWS.md](docs/FLOWS.md) — flujos y máquinas de estado.
- [docs/THEMES.md](docs/THEMES.md) — sistema de temas.
- [docs/BUILD.md](docs/BUILD.md) — build, tests y publicación.
- [docs/RELEASE_6.8.7.50.md](docs/RELEASE_6.8.7.50.md) — estado de esta revisión.
- [CREDITS.md](CREDITS.md) — créditos y atribuciones.

## Licencia

GPL-3.0. Consulte [LICENSE](LICENSE) y [LICENSE-NOTICE.md](LICENSE-NOTICE.md).
