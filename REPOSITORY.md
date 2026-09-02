# Estructura del repositorio

```text
GarlicSaveMgr/
  Código fuente C#/.NET 8 + WPF de la versión pública v6.8.1.

legacy/python-6.6.1/
  Snapshot del cliente Python original que sirvió como referencia funcional.

.github/workflows/
  Integración continua para compilar Windows x64.

.github/ISSUE_TEMPLATE/
  Plantillas para incidencias.

.github/PULL_REQUEST_TEMPLATE.md
  Plantilla para pull requests.

docs/
  Documentación de build, release, descubrimiento y validación.

*.png / *.ico / *.jpg
  Recursos y capturas históricas del proyecto.
```

El repositorio no incluye datos personales, perfiles de consola, carátulas descargadas, payloads cacheados ni backups generados por el usuario.

Los directorios generados localmente (`bin/`, `obj/`, `publish/`, `data/`, `covers/`, `payload_cache/`, `garlic_saves/` y `discovery_temp/`) deben permanecer fuera de Git mediante `.gitignore`.
