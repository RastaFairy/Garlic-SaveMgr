using System.Globalization;
using System.Xml.Linq;

namespace GarlicSaveMgr.Infrastructure;

public static class ThemeValidator
{
    public static IReadOnlyDictionary<string, string> RequiredKeys { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppBackground"] = "Fondo general de la aplicación.", ["Surface"] = "Superficie principal.", ["SurfaceAlt"] = "Superficie alternativa.",
            ["SurfaceElevated"] = "Superficie elevada.", ["SurfaceHover"] = "Superficie al pasar el ratón.", ["SurfaceSoft"] = "Superficie suave.",
            ["LogBackground"] = "Fondo del registro.", ["BorderSoft"] = "Borde normal.", ["BorderHover"] = "Borde en hover.",
            ["BorderStrong"] = "Borde destacado.", ["TextPrimary"] = "Texto principal.", ["TextSecondary"] = "Texto secundario.",
            ["TextMuted"] = "Texto atenuado.", ["OnAccent"] = "Texto sobre el color de acento.", ["Accent"] = "Acento principal.",
            ["AccentHover"] = "Acento en hover.", ["AccentPressed"] = "Acento pulsado.", ["AccentSoft"] = "Fondo suave de acento.",
            ["Success"] = "Estado correcto.", ["SuccessSoft"] = "Fondo suave de éxito.", ["Warning"] = "Advertencia.",
            ["WarningSoft"] = "Fondo suave de advertencia.", ["Danger"] = "Estado destructivo/error.", ["DangerSoft"] = "Fondo suave destructivo/error."
        };

    private static readonly IReadOnlyDictionary<string, Func<string, bool>> AppearanceValidators =
        new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Typography.FontSize"] = IsDouble, ["Typography.SmallFontSize"] = IsDouble, ["Typography.TitleFontSize"] = IsDouble, ["Typography.SectionFontSize"] = IsDouble,
            ["Typography.DialogTitleFontSize"] = IsDouble, ["Typography.FontWeight"] = IsFontWeight, ["Typography.EmphasisFontWeight"] = IsFontWeight,
            ["Typography.FontFamily"] = IsNonEmpty,
            ["Geometry.ButtonHeight"] = IsPositiveDouble, ["Geometry.ButtonPadding"] = IsThickness, ["Geometry.ButtonMargin"] = IsThickness, ["Geometry.ButtonCornerRadius"] = IsCornerRadius, ["Geometry.ButtonMinWidth"] = IsPositiveDouble, ["Geometry.PrimaryButtonMinWidth"] = IsPositiveDouble, ["Geometry.DangerButtonMinWidth"] = IsPositiveDouble,
            ["Geometry.InputHeight"] = IsPositiveDouble, ["Geometry.InputPadding"] = IsThickness, ["Geometry.InputCornerRadius"] = IsCornerRadius,
            ["Geometry.ComboBoxHeight"] = IsPositiveDouble, ["Geometry.ComboBoxPadding"] = IsThickness, ["Geometry.ComboBoxCornerRadius"] = IsCornerRadius,
            ["Geometry.ComboBoxItemPadding"] = IsThickness, ["Geometry.ComboBoxItemCornerRadius"] = IsCornerRadius,
            ["Geometry.CardCornerRadius"] = IsCornerRadius, ["Geometry.CardPadding"] = IsThickness, ["Geometry.DialogCornerRadius"] = IsCornerRadius, ["Geometry.DialogPadding"] = IsThickness,
            ["Geometry.DataGridRowHeight"] = IsPositiveDouble, ["Geometry.DataGridCellPadding"] = IsThickness, ["Geometry.DataGridHeaderPadding"] = IsThickness,
            ["Geometry.TabHeight"] = IsPositiveDouble, ["Geometry.TabPadding"] = IsThickness, ["Geometry.TabCornerRadius"] = IsCornerRadius,
            ["Geometry.SwitchWidth"] = IsPositiveDouble, ["Geometry.SwitchHeight"] = IsPositiveDouble, ["Geometry.SwitchThumbSize"] = IsPositiveDouble, ["Geometry.SwitchCornerRadius"] = IsCornerRadius,
            ["Layout.SettingsWidth"] = IsPositiveDouble, ["Layout.SettingsMinWidth"] = IsPositiveDouble, ["Layout.SettingsMinHeight"] = IsPositiveDouble,
            ["Layout.DialogWidth"] = IsPositiveDouble, ["Layout.DialogMinWidth"] = IsPositiveDouble, ["Layout.DialogMaxWidth"] = IsPositiveDouble,
            ["Animation.Enabled"] = IsBool, ["Animation.HoverDurationMs"] = IsPositiveDoubleOrZero, ["Animation.PressedDurationMs"] = IsPositiveDoubleOrZero, ["Animation.HoverScale"] = IsPositiveDouble,
        };

    public static bool TryValidateDocument(XDocument document, out List<string> errors)
    {
        errors = [];
        if (!string.Equals(document.Root?.Name.LocalName, "Theme", StringComparison.OrdinalIgnoreCase)) errors.Add("La raíz XML debe ser <Theme>.");
        var root = document.Root;
        if (root is null) return false;

        var schemaText = root.Element("Metadata")?.Element("ThemeSchema")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(schemaText) && int.TryParse(schemaText, out var schema) && schema > ThemeDefinition.CurrentSchema)
            errors.Add($"ThemeSchema {schema} no es compatible con el esquema {ThemeDefinition.CurrentSchema}.");

        foreach (var mode in new[] { "Light", "Dark" })
        {
            var modeElement = root.Element(mode);
            var colors = modeElement?.Element("Colors");
            if (colors is null) { errors.Add($"Falta <{mode}><Colors>."); continue; }
            foreach (var key in RequiredKeys.Keys)
            {
                var element = colors.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, key, StringComparison.OrdinalIgnoreCase));
                if (element is null) continue;
                if (!TryNormalizeHex(element.Value.Trim(), out _)) errors.Add($"{mode}.{key}: color inválido '{element.Value.Trim()}'.");
            }
            ValidateAppearance(modeElement, mode, errors);
        }
        return errors.Count == 0;
    }

    private static void ValidateAppearance(XElement? mode, string modeName, ICollection<string> errors)
    {
        if (mode is null) return;
        foreach (var section in mode.Elements().Where(e => !string.Equals(e.Name.LocalName, "Colors", StringComparison.OrdinalIgnoreCase)))
            ValidateNode(section, section.Name.LocalName, modeName, errors);
    }

    private static void ValidateNode(XElement element, string path, string modeName, ICollection<string> errors)
    {
        if (!element.Elements().Any())
        {
            if (AppearanceValidators.TryGetValue(path, out var validator) && !validator(element.Value.Trim()))
                errors.Add($"{modeName}.{path}: valor no válido '{element.Value.Trim()}'.");
            return;
        }
        foreach (var child in element.Elements()) ValidateNode(child, path + "." + child.Name.LocalName, modeName, errors);
    }

    public static bool TryNormalizeHex(string value, out string normalized)
    {
        normalized = "";
        var v = value?.Trim() ?? "";
        if (!v.StartsWith('#')) return false;
        var hex = v[1..];
        if (hex.Length is not (6 or 8) || !hex.All(Uri.IsHexDigit)) return false;
        normalized = "#" + hex.ToUpperInvariant();
        return true;
    }

    private static bool IsNonEmpty(string s) => !string.IsNullOrWhiteSpace(s);
    private static bool IsBool(string s) => bool.TryParse(s, out _);
    private static bool IsDouble(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    private static bool IsPositiveDouble(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0;
    private static bool IsPositiveDoubleOrZero(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0;

    private static bool IsThickness(string s) => TryThickness(s, out _);
    private static bool IsCornerRadius(string s) => TryCornerRadius(s, out _);
    private static bool IsFontWeight(string s) => TryFontWeight(s, out _);

    public static bool TryThickness(string value, out System.Windows.Thickness thickness)
    {
        thickness = new System.Windows.Thickness();
        var p = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length is not (1 or 2 or 4)) return false;
        if (!p.All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var _))) return false;
        var d = p.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        thickness = d.Length switch { 1 => new System.Windows.Thickness(d[0]), 2 => new System.Windows.Thickness(d[0], d[1], d[0], d[1]), _ => new System.Windows.Thickness(d[0], d[1], d[2], d[3]) };
        return d.All(x => x >= 0);
    }

    internal static bool TryCornerRadius(string value, out System.Windows.CornerRadius radius)
    {
        radius = new System.Windows.CornerRadius();
        var p = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length is not (1 or 4)) return false;
        if (!p.All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var _))) return false;
        var d = p.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        if (!d.All(x => x >= 0)) return false;
        radius = d.Length == 1 ? new System.Windows.CornerRadius(d[0]) : new System.Windows.CornerRadius(d[0], d[1], d[2], d[3]);
        return true;
    }

    internal static bool TryFontWeight(string value, out System.Windows.FontWeight weight)
    {
        var normalized = value.Trim();
        var named = normalized.ToLowerInvariant() switch
        {
            "thin" => System.Windows.FontWeights.Thin, "extralight" => System.Windows.FontWeights.ExtraLight, "light" => System.Windows.FontWeights.Light,
            "normal" => System.Windows.FontWeights.Normal, "medium" => System.Windows.FontWeights.Medium, "semibold" => System.Windows.FontWeights.SemiBold,
            "bold" => System.Windows.FontWeights.Bold, "extrabold" => System.Windows.FontWeights.ExtraBold, "black" => System.Windows.FontWeights.Black,
            "heavy" => System.Windows.FontWeights.Heavy, _ => (System.Windows.FontWeight?)null
        };
        if (named.HasValue) { weight = named.Value; return true; }
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) && numeric is >= 1 and <= 999)
        {
            weight = System.Windows.FontWeight.FromOpenTypeWeight(numeric);
            return true;
        }
        weight = System.Windows.FontWeights.Normal;
        return false;
    }

}
