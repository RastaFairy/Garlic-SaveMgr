# MainWindow y descubrimiento de consola — Diseño

**Objetivo:** reducir el acoplamiento de `MainWindow.xaml.cs` y hacer fiable la autodetección de Garlic en redes locales reales, sin cambiar el comportamiento funcional de backup/restore.

## Diseño

La aplicación mantiene WPF y `net8.0-windows`. La ventana seguirá siendo el coordinador de eventos visuales, pero la lógica de descubrimiento se separará en un planificador testeable y los modelos de presentación se irán sacando de `MainWindow` de forma incremental.

El descubrimiento utilizará todas las interfaces IPv4 activas no-loopback, calculará la subred usando la máscara real, priorizará IP local y gateway, eliminará duplicados y probará el puerto Garlic sin proxy. Primero se probarán rápidamente los hosts del `/24` local cuando la red sea mayor; si no aparece Garlic, se ampliará al resto de la subred para conservar cobertura.

## Restricciones

- Mantener `net8.0-windows`, WPF y `win-x64`.
- No modificar `App.xaml.cs`.
- No alterar SHA-256, confirmaciones de eliminación ni flujo de payload salvo el desacoplamiento de coordinación.
- No introducir CommunityToolkit.Mvvm en esta fase.
- Cada cambio debe quedar registrado en `CHANGELOG.md`.
- Build, tests y publish siguen siendo las puertas de verificación.
