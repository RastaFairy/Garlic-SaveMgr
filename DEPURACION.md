# Depuración y mantenimiento — Garlic SaveMgr v6.8.1

Este documento reúne notas de mantenimiento y decisiones técnicas que afectan al cliente C#/.NET 8 + WPF. No es la documentación de usuario; para el flujo actual consulta `README.md` y `docs/`.

## Estado de la implementación actual

- Cliente de escritorio: C# / .NET 8 / WPF.
- Plataforma de publicación: Windows x64.
- Publicación: self-contained, single-file.
- Persistencia: portable, bajo la carpeta del ejecutable.
- Garlic HTTP: puerto `8082`.
- `elfldr`: puerto `9021`.

## Descubrimiento de consola v6.8.1

El método actualmente publicado es deliberadamente determinista y no depende del router:

1. Se recorre `192.168.0.0` → `192.168.255.255`.
2. Las direcciones se procesan en lotes de hasta 255 pings simultáneos.
3. Cada dirección utiliza el `ping.exe` nativo de Windows con un timeout de 100 ms.
4. La salida de cada proceso se guarda temporalmente bajo `discovery_temp/`.
5. Solo los hosts con respuesta ICMP positiva pasan a la validación de Garlic.
6. Se prueba `GET /api/status` en `8082`.
7. Si Garlic todavía no responde, `9021` se comprueba como puerto TCP de `elfldr` y no se confunde con la API de Garlic.

Las carpetas temporales de lotes antiguos se eliminan automáticamente. El fallback manual de IP/puerto permanece disponible.

## Arranque de Garlic

Cuando Garlic no está activo, la aplicación puede preparar la caché del payload y, con autorización del usuario, enviarlo a `elfldr` en `9021`. Después del envío se vuelve a comprobar `8082` hasta que Garlic responde o se agota el tiempo de espera.

El payload seleccionado por el catálogo puede cambiar con el tiempo. La aplicación separa:

- versión de Garlic realmente ejecutándose en la consola;
- versión del payload almacenada en caché;
- última versión anunciada por los catálogos.

## Portabilidad

La raíz de almacenamiento es `AppContext.BaseDirectory`. La aplicación no usa `%AppData%`, `%LocalAppData%` ni el Registro para la persistencia normal.

## Validación de v6.8.1

Durante la validación funcional en Windows se confirmó:

- descubrimiento mediante ping en lotes;
- consola encontrada en `192.168.1.211`;
- Garlic operativo en `8082`;
- envío del payload `v1.13` a `9021`;
- arranque posterior de Garlic;
- escaneo de 41 títulos.

El detalle del registro histórico está en `docs/VALIDATION_v6.8.1.md`.

## Problemas históricos

Las primeras iteraciones utilizaron otras estrategias de descubrimiento y diferentes configuraciones de UI. Esas notas se conservan en `changelog-history.md`; no describen necesariamente el comportamiento del cliente actual.
