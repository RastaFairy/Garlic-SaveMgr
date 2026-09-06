# Build y validación

Requisitos: Windows x64, .NET 8 SDK y PowerShell.

## Build completa

```powershell
.\build.ps1
```

Compila host + módulos + tests y publica `win-x64` en `publish/`.

## Módulo aislado

Con el host ya compilado:

```powershell
.\build-one-module.ps1 Salud
```

Valores: `Security`, `Trash`, `Salud`, `Updater`.

## Regla de evidencia

No declarar `PASS`, `build OK` o `tests OK` sin ejecutar realmente la comprobación. `publish/` y todos los datos de ejecución quedan fuera de Git.
