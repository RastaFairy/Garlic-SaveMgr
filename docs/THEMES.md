# Sistema de temas

El motor utiliza el esquema vigente y `Default.xml` como fallback.

- Los temas son declarativos.
- Valores ausentes heredan de `Default.xml`.
- Un tema inválido no impide el arranque.
- Los recursos se consumen con `DynamicResource` cuando corresponde.
- No mutar el `Color` de un `SolidColorBrush` existente; sustituir el recurso.
- No introducir código ni XAML arbitrario desde XML.

Plantilla: `Themes/ThemeTemplate.xml`.
