# Arquitectura modular

## Estado 6.8.7.50

| Módulo | DLL | Responsabilidad |
|---|---|---|
| Seguridad | `GarlicSaveMgr.SecurityModule.dll` | Integridad y auditoría |
| Papelera | `GarlicSaveMgr.TrashModule.dll` | Papelera PS5 + PC |
| Salud | `GarlicSaveMgr.SaludModule.dll` | Observabilidad global |
| Actualizador | `GarlicSaveMgr.UpdaterModule.dll` | Releases y actualización |

## Contrato

```text
ModuleEntry.CreateModule(IModuleHostContext)
                    ↓
              IExecutableModule
              ├── Id
              ├── Header
              ├── View
              └── OnHostStateChanged(state)
```

El host no conoce handlers concretos del dominio.

## Independencia

- Seguridad → `SecurityModule.dll`.
- Papelera → `TrashModule.dll`.
- Salud → `SaludModule.dll`.
- Actualizador → `UpdaterModule.dll`.

Cambiar contratos o servicios core compartidos requiere recompilar el host.

## Degradación

La ausencia de un módulo opcional no debe impedir que el host inicie ni bloquear Backup, Restore, descubrimiento, escaneo o Ajustes cuando estas funciones no dependan de él.
