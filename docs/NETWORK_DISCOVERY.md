# Network discovery — v6.8.1

## Objetivo

v6.8.1 utiliza un descubrimiento deliberadamente simple: detectar hosts mediante ICMP y validar posteriormente si el host ejecuta Garlic. No requiere acceso al router y no depende de su marca, credenciales, DHCP UI o API.

## Flujo

```text
Generar IP
   ↓
Lote de hasta 255 direcciones
   ↓
ping.exe oculto por dirección
   ↓
Recoger resultados temporales
   ↓
Filtrar PING OK
   ↓
GET /api/status → 8082
   ↓
Garlic encontrado
   │
   └── si Garlic no está activo → probar TCP 9021 (elfldr)
```

## Persistencia temporal

Los resultados de `ping.exe` se escriben en `discovery_temp/` bajo la raíz portable de la aplicación. Las carpetas de lotes antiguas se limpian para evitar crecimiento indefinido.

## Orden de búsqueda

Las direcciones IPv4 del espacio configurado se generan mediante suma incremental `+1`. En la implementación actual de v6.8.1 el rango determinista de compatibilidad para este modo es `192.168.0.0`–`192.168.255.255`.

## Puertos

- `8082`: API HTTP de Garlic.
- `9021`: `elfldr`; solo sirve para detectar que el inyector está disponible y para cargar el payload. No sustituye a `8082` como API de Garlic.

## Fallback manual

Si no se encuentra ningún host válido, el usuario puede introducir manualmente la IP y el puerto de la consola. La dirección puede guardarse en el perfil de consola.

## Por qué no se usa el router

El descubrimiento no necesita conocer el gateway, el panel web del router ni sus clientes DHCP. Esto permite mover la aplicación entre redes domésticas, routers e infraestructuras diferentes sin cambiar su lógica de descubrimiento.
