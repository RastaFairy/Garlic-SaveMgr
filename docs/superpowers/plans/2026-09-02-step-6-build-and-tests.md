# Paso 6 — Build y tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preparar Garlic SaveMgr para compilación reproducible en Windows, añadir pruebas xUnit para la lógica independiente y automatizar build/test en GitHub Actions.

**Architecture:** Se mantiene el proyecto WPF existente como aplicación `net8.0-windows` x64. Se añade un proyecto de pruebas `net8.0-windows` separado que referencia al ejecutable y usa xUnit v3; el workflow ejecutará restore, build y tests sobre `windows-latest`.

**Tech Stack:** .NET 8, C#, WPF, xUnit.net v3 4.0.0, Microsoft.NET.Test.Sdk 18.9.0, xUnit Visual Studio adapter 4.0.0, GitHub Actions.

**Spec:** Paso 6 de la lista de mantenimiento de Garlic SaveMgr.

## Global Constraints

- Mantener la aplicación en `net8.0-windows` y `win-x64`.
- No introducir CommunityToolkit.Mvvm ni refactorizar `MainWindow.xaml.cs` en este paso.
- No modificar el manejo global de excepciones de `App.xaml.cs`.
- No alterar la verificación SHA-256 ni la confirmación MessageBox antes del payload.
- Registrar todos los cambios del paso en `CHANGELOG.md`.

---

### Task 1: Crear proyecto de tests

**Files:**
- Create: `GarlicSaveMgr.Tests/GarlicSaveMgr.Tests.csproj`
- Create: `GarlicSaveMgr.Tests/PayloadLauncherServiceTests.cs`

**Interfaces:**
- Consumes: `GarlicSaveMgr.Services.PayloadLauncherService.CompareVersions(string, string)`.
- Produces: un proyecto xUnit ejecutable por `dotnet test` que permita ampliar pruebas de `Services/`.

- [ ] **Step 1:** Crear el `.csproj` apuntando a `net8.0-windows`, x64, con `EnableWindowsTargeting=true`, referencia al proyecto principal y paquetes xUnit v3 + adapter + Test SDK.

- [ ] **Step 2:** Crear pruebas que cubran mayor, menor, igualdad y versiones con formatos `v6.8`, `6.8.0`, `1.10`.

- [ ] **Step 3:** Ejecutar `dotnet test GarlicSaveMgr.Tests/GarlicSaveMgr.Tests.csproj -c Release` en Windows y comprobar todas las pruebas.

- [ ] **Step 4:** Commit independiente del proyecto de tests.

### Task 2: Integrar tests en la solución y preparar build local

**Files:**
- Modify: `GarlicSaveMgr.sln`
- Modify: `build.ps1`
- Modify: `README_BUILD.md`

**Interfaces:**
- Consumes: `GarlicSaveMgr.Tests`.
- Produces: solución compilable con aplicación + tests y script local que valida restore/build/test antes de publicar.

- [ ] **Step 1:** Añadir `GarlicSaveMgr.Tests` a la solución con configuración Debug/Release Any CPU.

- [ ] **Step 2:** Actualizar `build.ps1` para restaurar la solución, ejecutar `dotnet build` y `dotnet test` antes de publicar `win-x64`.

- [ ] **Step 3:** Documentar requisitos locales: Windows, .NET 8 SDK, Visual Studio 2022 con workload de escritorio .NET cuando se use IDE, y ejecución de `powershell -ExecutionPolicy Bypass -File .\build.ps1`.

- [ ] **Step 4:** Ejecutar build/test local si el entorno disponible lo permite; no declarar éxito si faltan SDK o acceso a NuGet.

- [ ] **Step 5:** Commit de la integración local.

### Task 3: CI en GitHub Actions

**Files:**
- Create: `.github/workflows/build.yml`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: solución, proyecto WPF y proyecto de tests.
- Produces: workflow que ante `push` a `main` y `pull_request` ejecuta restore, build Release y tests en Windows.

- [ ] **Step 1:** Crear workflow `windows-latest` usando `actions/checkout` y `actions/setup-dotnet` con canal `8.0.x`.

- [ ] **Step 2:** Ejecutar `dotnet restore GarlicSaveMgr.sln`, `dotnet build GarlicSaveMgr.sln -c Release --no-restore` y `dotnet test GarlicSaveMgr.Tests/GarlicSaveMgr.Tests.csproj -c Release --no-build --verbosity normal`.

- [ ] **Step 3:** Registrar en `CHANGELOG.md` la incorporación de tests, build local y CI.

- [ ] **Step 4:** Revisar los archivos finales y comprobar que los cambios no alteran la configuración funcional de la aplicación.

- [ ] **Step 5:** Commit final del CI y changelog.
