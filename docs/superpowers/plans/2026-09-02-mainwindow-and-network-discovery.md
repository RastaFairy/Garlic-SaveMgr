# MainWindow y descubrimiento de consola Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** extraer responsabilidades de `MainWindow.xaml.cs` de forma incremental y corregir la autodetección de consola para redes locales reales.

**Architecture:** `ConsoleDiscoveryService` delegará la planificación de direcciones a una clase testeable que acepta instantáneas de interfaces. El scanner utilizará todas las interfaces activas, conexión HTTP directa sin proxy y una estrategia rápida `/24` seguida de expansión cuando proceda. La ventana mantendrá WPF code-behind como coordinador, sin migración completa a MVVM.

**Tech Stack:** .NET 8, C#, WPF, xUnit v3, `NetworkInterface`, `HttpClient`/`SocketsHttpHandler`.

**Spec:** `docs/superpowers/specs/2026-09-02-mainwindow-and-network-discovery-design.md`

## Global Constraints

- Mantener `net8.0-windows` y `win-x64`.
- No introducir CommunityToolkit.Mvvm.
- No modificar `App.xaml.cs`.
- No alterar SHA-256, confirmaciones ni el flujo funcional del payload.
- Registrar todos los cambios en `CHANGELOG.md`.
- No declarar una compilación como verificada sin evidencia del build/test/publish.

---

### Task 1: Red fiable y testeable

**Files:**
- Create: `GarlicSaveMgr/Services/ConsoleDiscoveryPlanner.cs`
- Create: `GarlicSaveMgr.Tests/ConsoleDiscoveryPlannerTests.cs`
- Modify: `GarlicSaveMgr/Services/ConsoleDiscoveryService.cs`

**Interfaces:**
- `ConsoleDiscoveryPlanner.NetworkSnapshot` contiene IP, máscara y gateways.
- `ConsoleDiscoveryPlanner.BuildCandidates(...)` devuelve candidatos únicos, con host/gateway primero.
- `ConsoleDiscoveryPlanner.BuildExpandedCandidates(...)` permite ampliar una red grande después del `/24` rápido.

- [ ] **Step 1:** Añadir tests para `/24`, `/16`, múltiples interfaces y deduplicación.
- [ ] **Step 2:** Añadir el planificador mínimo para que esos tests pasen.
- [ ] **Step 3:** Cambiar `ConsoleDiscoveryService` para enumerar todas las interfaces IPv4 activas y usar el planificador.
- [ ] **Step 4:** Usar `SocketsHttpHandler.UseProxy = false`, mantener 32 sondas y cancelación inmediata.
- [ ] **Step 5:** Usar búsqueda rápida `/24` por interfaz y, si falla, continuar con el resto de la subred.
- [ ] **Step 6:** Ejecutar `dotnet test` y `build.ps1` en Windows.

### Task 2: Extraer modelos de presentación

**Files:**
- Create: `GarlicSaveMgr/Presentation/StatusRowBase.cs`
- Create: `GarlicSaveMgr/Presentation/TitleRow.cs`
- Create: `GarlicSaveMgr/Presentation/BackupRow.cs`
- Create: `GarlicSaveMgr/Presentation/RestoreGameGroup.cs`
- Modify: `GarlicSaveMgr/MainWindow.xaml.cs`

**Interfaces:**
- Las clases mantienen las propiedades públicas usadas por el XAML y por los handlers actuales.

- [ ] **Step 1:** Mover `StatusRowBase`, `TitleRow`, `BackupRow` y `RestoreGameGroup` sin cambiar comportamiento.
- [ ] **Step 2:** Actualizar namespaces/usings y eliminar las clases del code-behind.
- [ ] **Step 3:** Ejecutar tests y build.

### Task 3: Extraer coordinación de payload y consola

**Files:**
- Create: `GarlicSaveMgr/Services/PayloadCoordinator.cs`
- Create: `GarlicSaveMgr/Services/ConsoleSessionService.cs`
- Modify: `GarlicSaveMgr/MainWindow.xaml.cs`
- Modify: `GarlicSaveMgr.Tests/...` según pruebas necesarias

- [ ] **Step 1:** Mover la coordinación de payload manteniendo los mismos servicios y mensajes.
- [ ] **Step 2:** Mover perfil/IP/detección/conexión a `ConsoleSessionService`.
- [ ] **Step 3:** Sustituir llamadas de `MainWindow` por esos coordinadores.
- [ ] **Step 4:** Ejecutar tests y build.

### Task 4: Extraer backup/restore y utilidades de UI

**Files:**
- Create: `GarlicSaveMgr/Services/BackupRestoreCoordinator.cs`
- Create: `GarlicSaveMgr/Presentation/UiFormatters.cs`
- Modify: `GarlicSaveMgr/MainWindow.xaml.cs`
- Modify: `CHANGELOG.md`

- [ ] **Step 1:** Mover backup, restore, eliminación y exportación manteniendo `OperationRunner` y `BackupService`.
- [ ] **Step 2:** Mover `FormatBytes` y helpers puramente presentacionales.
- [ ] **Step 3:** Reducir `MainWindow` a handlers y coordinación visual.
- [ ] **Step 4:** Ejecutar `build.ps1` y verificar publish.
- [ ] **Step 5:** Registrar el resultado final en `CHANGELOG.md`.
