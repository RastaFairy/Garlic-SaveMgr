# Estructura del repositorio

```text
GarlicSaveMgr.sln
GarlicSaveMgr/                 # host + servicios core
Modules/
  Security/                    # módulo de seguridad
  Trash/                       # módulo de Papelera
  Salud/                       # observabilidad
  Updater/                     # actualizador
GarlicSaveMgr.Tests/           # regresiones
Themes/                        # plantilla de temas
.github/                       # CI / issue / PR templates
docs/                          # documentación canónica
```

El repositorio fuente no incluye datos personales ni artefactos de ejecución. Los siguientes directorios deben permanecer fuera de Git:

```text
bin/
obj/
publish/
data/
covers/
payload_cache/
garlic_saves/
discovery_temp/
```
