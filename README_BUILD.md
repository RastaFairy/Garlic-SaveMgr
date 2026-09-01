# Build notes

The canonical build is the PowerShell script `build.ps1`.

## Requisitos

- Windows x64
- .NET 8 SDK
- Visual Studio 2022 (opcional) con desarrollo de escritorio de .NET si se compila desde el IDE
- Acceso a NuGet para la primera restauración de paquetes

The application is WPF and targets `net8.0-windows` / `win-x64`.

## Compilación local

Desde PowerShell, en la raíz del repositorio:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

El script realiza, en este orden:

1. Comprueba que `dotnet` está disponible.
2. Restaura la solución completa.
3. Compila en `Release` la aplicación y el proyecto de tests.
4. Ejecuta los tests de `GarlicSaveMgr.Tests`.
5. Publica la aplicación como `win-x64`, self-contained y single-file.
6. Verifica que existe `publish\Garlic_SaveMgr.exe`.

## Compilación desde Visual Studio

Abre `GarlicSaveMgr.sln`, selecciona `Release` y `Any CPU`, y compila la solución. El proyecto principal genera el ejecutable para Windows x64; `GarlicSaveMgr.Tests` queda integrado en Test Explorer.

## Tests desde consola

```powershell
dotnet test .\GarlicSaveMgr.Tests\GarlicSaveMgr.Tests.csproj -c Release
```
