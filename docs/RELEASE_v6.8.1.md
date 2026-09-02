# Release v6.8.1

Garlic SaveMgr v6.8.1 consolida la rama pública C#/.NET 8 + WPF y documenta el método de descubrimiento de consola validado en Windows.

## Destacado

La detección agrupa hasta 255 pings de `ping.exe` por lote, guarda temporalmente sus resultados y solo valida mediante HTTP las IPs que respondieron al ICMP. Este modelo evita bloquear el descubrimiento esperando una dirección inactiva antes de iniciar la siguiente.

## Flujo validado

`ping → 8082 → 9021/elfldr cuando es necesario → arranque Garlic → escaneo`

En la prueba de referencia la consola fue localizada en `192.168.1.211`, el payload v1.13 se envió por `9021` y Garlic quedó operativo en `8082`.

## Compatibilidad

- Windows x64
- .NET 8 SDK para compilación
- Publicación self-contained `win-x64` en un único ejecutable

## Notas

La detección no utiliza datos del router ni presupone una marca concreta de infraestructura de red. El fallback manual permanece disponible para casos excepcionales.
