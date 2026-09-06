using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GarlicSaveMgr.Infrastructure;

public static class ThemeManager
{
    public static event EventHandler? ThemeChanged;

    private static readonly string[] RuntimeColorKeys =
    [
        "AppBackground", "Surface", "SurfaceAlt", "SurfaceElevated", "SurfaceHover", "SurfaceSoft", "LogBackground",
        "BorderSoft", "BorderHover", "BorderStrong", "TextPrimary", "TextSecondary", "TextMuted", "OnAccent",
        "Accent", "AccentHover", "AccentPressed", "AccentSoft", "Success", "SuccessSoft", "Warning", "WarningSoft",
        "Danger", "DangerSoft", "OnAccentMuted", "SwitchOff", "SwitchOn", "SwitchThumb"
    ];

    private static readonly IReadOnlyDictionary<string, object> AppearanceDefaults = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
    {
        ["Theme.FontFamily"] = new FontFamily("Segoe UI"),
        ["Theme.FontSize"] = 13d, ["Theme.SmallFontSize"] = 12d, ["Theme.TitleFontSize"] = 18d, ["Theme.SectionFontSize"] = 16d, ["Theme.DialogTitleFontSize"] = 18d,
        ["Theme.FontWeight"] = FontWeights.Normal, ["Theme.EmphasisFontWeight"] = FontWeights.SemiBold,
        ["Theme.ButtonHeight"] = 34d, ["Theme.ButtonPadding"] = new Thickness(12, 6, 12, 6), ["Theme.ButtonMargin"] = new Thickness(0), ["Theme.ButtonCornerRadius"] = new CornerRadius(7),
        ["Theme.ButtonMinWidth"] = 82d, ["Theme.PrimaryButtonMinWidth"] = 110d, ["Theme.DangerButtonMinWidth"] = 140d,
        ["Theme.InputHeight"] = 38d, ["Theme.InputPadding"] = new Thickness(10, 7, 10, 7), ["Theme.InputCornerRadius"] = new CornerRadius(7),
        ["Theme.ComboBoxHeight"] = 38d, ["Theme.ComboBoxPadding"] = new Thickness(10, 0, 10, 0), ["Theme.ComboBoxCornerRadius"] = new CornerRadius(7),
        ["Theme.ComboBoxItemPadding"] = new Thickness(10, 8, 10, 8), ["Theme.ComboBoxItemCornerRadius"] = new CornerRadius(5),
        ["Theme.CardCornerRadius"] = new CornerRadius(10), ["Theme.CardPadding"] = new Thickness(14), ["Theme.DialogCornerRadius"] = new CornerRadius(10), ["Theme.DialogPadding"] = new Thickness(18),
        ["Theme.DataGridRowHeight"] = 30d, ["Theme.DataGridCellPadding"] = new Thickness(4, 0, 4, 0), ["Theme.DataGridHeaderPadding"] = new Thickness(8, 7, 8, 7),
        ["Theme.TabHeight"] = 36d, ["Theme.TabPadding"] = new Thickness(16, 8, 16, 8), ["Theme.TabCornerRadius"] = new CornerRadius(7),
        ["Theme.SwitchWidth"] = 46d, ["Theme.SwitchHeight"] = 24d, ["Theme.SwitchThumbSize"] = 18d, ["Theme.SwitchCornerRadius"] = new CornerRadius(12),
        ["Theme.SettingsWidth"] = 660d, ["Theme.SettingsMinWidth"] = 600d, ["Theme.SettingsMinHeight"] = 520d,
        ["Theme.DialogWidth"] = 520d, ["Theme.DialogMinWidth"] = 440d, ["Theme.DialogMaxWidth"] = 720d,
        ["Theme.AnimationEnabled"] = true, ["Theme.HoverDuration"] = new Duration(TimeSpan.FromMilliseconds(120)), ["Theme.PressedDuration"] = new Duration(TimeSpan.FromMilliseconds(70)), ["Theme.HoverScale"] = 1d
    };

    private static readonly IReadOnlyDictionary<string, string> AppearanceResourceAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Typography.FontFamily"] = "Theme.FontFamily", ["Typography.FontSize"] = "Theme.FontSize", ["Typography.SmallFontSize"] = "Theme.SmallFontSize",
            ["Typography.TitleFontSize"] = "Theme.TitleFontSize", ["Typography.SectionFontSize"] = "Theme.SectionFontSize", ["Typography.DialogTitleFontSize"] = "Theme.DialogTitleFontSize",
            ["Typography.FontWeight"] = "Theme.FontWeight", ["Typography.EmphasisFontWeight"] = "Theme.EmphasisFontWeight",
            ["Geometry.ButtonHeight"] = "Theme.ButtonHeight", ["Geometry.ButtonPadding"] = "Theme.ButtonPadding", ["Geometry.ButtonMargin"] = "Theme.ButtonMargin", ["Geometry.ButtonCornerRadius"] = "Theme.ButtonCornerRadius",
            ["Geometry.ButtonMinWidth"] = "Theme.ButtonMinWidth", ["Geometry.PrimaryButtonMinWidth"] = "Theme.PrimaryButtonMinWidth", ["Geometry.DangerButtonMinWidth"] = "Theme.DangerButtonMinWidth",
            ["Geometry.InputHeight"] = "Theme.InputHeight", ["Geometry.InputPadding"] = "Theme.InputPadding", ["Geometry.InputCornerRadius"] = "Theme.InputCornerRadius",
            ["Geometry.ComboBoxHeight"] = "Theme.ComboBoxHeight", ["Geometry.ComboBoxPadding"] = "Theme.ComboBoxPadding", ["Geometry.ComboBoxCornerRadius"] = "Theme.ComboBoxCornerRadius",
            ["Geometry.ComboBoxItemPadding"] = "Theme.ComboBoxItemPadding", ["Geometry.ComboBoxItemCornerRadius"] = "Theme.ComboBoxItemCornerRadius",
            ["Geometry.CardCornerRadius"] = "Theme.CardCornerRadius", ["Geometry.CardPadding"] = "Theme.CardPadding", ["Geometry.DialogCornerRadius"] = "Theme.DialogCornerRadius", ["Geometry.DialogPadding"] = "Theme.DialogPadding",
            ["Geometry.DataGridRowHeight"] = "Theme.DataGridRowHeight", ["Geometry.DataGridCellPadding"] = "Theme.DataGridCellPadding", ["Geometry.DataGridHeaderPadding"] = "Theme.DataGridHeaderPadding",
            ["Geometry.TabHeight"] = "Theme.TabHeight", ["Geometry.TabPadding"] = "Theme.TabPadding", ["Geometry.TabCornerRadius"] = "Theme.TabCornerRadius",
            ["Geometry.SwitchWidth"] = "Theme.SwitchWidth", ["Geometry.SwitchHeight"] = "Theme.SwitchHeight", ["Geometry.SwitchThumbSize"] = "Theme.SwitchThumbSize", ["Geometry.SwitchCornerRadius"] = "Theme.SwitchCornerRadius",
            ["Layout.SettingsWidth"] = "Theme.SettingsWidth", ["Layout.SettingsMinWidth"] = "Theme.SettingsMinWidth", ["Layout.SettingsMinHeight"] = "Theme.SettingsMinHeight",
            ["Layout.DialogWidth"] = "Theme.DialogWidth", ["Layout.DialogMinWidth"] = "Theme.DialogMinWidth", ["Layout.DialogMaxWidth"] = "Theme.DialogMaxWidth",
            ["Animation.Enabled"] = "Theme.AnimationEnabled", ["Animation.HoverDurationMs"] = "Theme.HoverDuration", ["Animation.PressedDurationMs"] = "Theme.PressedDuration", ["Animation.HoverScale"] = "Theme.HoverScale"
        };

    public static ThemeDefinition CurrentTheme { get; private set; } = null!;
    public static ThemeMode CurrentMode { get; private set; } = ThemeMode.Light;
    public static bool IsInitialized { get; private set; }

    public static void Initialize()
    {
        if (IsInitialized) return;
        AppPaths.EnsureDirectories();
        var preferences = SettingsService.LoadThemePreferences();
        CurrentTheme = ThemeLoader.LoadSelected(preferences.ThemeId, out var fallback);
        CurrentMode = preferences.Mode;
        ApplyToApplication(CurrentTheme, CurrentMode);
        if (fallback && !string.Equals(preferences.ThemeId, "Default", StringComparison.OrdinalIgnoreCase)) SettingsService.SaveThemePreferences("Default", CurrentMode);
        IsInitialized = true;
    }

    public static void Apply(string themeId, ThemeMode mode, bool persist = true)
    {
        var theme = ThemeLoader.LoadSelected(themeId, out var fallback);
        ApplyToApplication(theme, mode);
        CurrentTheme = theme; CurrentMode = mode;
        if (persist) SettingsService.SaveThemePreferences(fallback ? "Default" : theme.Id, mode);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static Brush GetBrush(string key) => Application.Current.Resources[key] as Brush ?? Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.Gray;

    public static Brush GetStatusBrush(string? state)
    {
        if (string.Equals(state, "ok", StringComparison.OrdinalIgnoreCase) || StartsWith(state, "OK")) return GetBrush("Success");
        if (string.Equals(state, "err", StringComparison.OrdinalIgnoreCase) || StartsWith(state, "ERROR")) return GetBrush("Danger");
        if (string.Equals(state, "proc", StringComparison.OrdinalIgnoreCase) || StartsWith(state, "EN CURSO") || StartsWith(state, "SIN HASH")) return GetBrush("Warning");
        return GetBrush("TextMuted");
    }

    private static bool StartsWith(string? value, string prefix) => value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;
    public static Brush GetLogBrush(string? level) => string.Equals(level, "error", StringComparison.OrdinalIgnoreCase) ? GetBrush("Danger") : string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase) ? GetBrush("Warning") : string.Equals(level, "ok", StringComparison.OrdinalIgnoreCase) ? GetBrush("Success") : GetBrush("TextSecondary");
    public static IReadOnlyList<ThemeCatalogItem> GetAvailableThemes() => ThemeLoader.ListAvailableThemes();

    private static void ApplyToApplication(ThemeDefinition theme, ThemeMode mode)
    {
        var palette = theme.GetPalette(mode); var appearance = theme.GetAppearance(mode); var resources = Application.Current.Resources;
        foreach (var key in RuntimeColorKeys) { if (palette.TryGetValue(key, out var hex)) resources[key] = CreateBrush(hex); else resources.Remove(key); }
        foreach (var pair in AppearanceDefaults) resources[pair.Key] = pair.Value;
        foreach (var pair in appearance)
        {
            if (!AppearanceResourceAliases.TryGetValue(pair.Key, out var resourceKey)) continue;
            if (TryBuildResource(resourceKey, pair.Value, out var resource)) resources[resourceKey] = resource;
        }
        resources["ThemeName"] = theme.Name; resources["ThemeMode"] = mode.ToString(); resources["Theme.Schema"] = theme.Schema;
    }

    private static bool TryBuildResource(string resourceKey, string value, out object resource)
    {
        resource = value;
        try
        {
            switch (resourceKey.ToLowerInvariant())
            {
                case "theme.fontfamily": resource = new FontFamily(value); return true;
                case "theme.fontweight":
                case "theme.emphasisfontweight":
                    if (TryFontWeight(value, out var weight)) { resource = weight; return true; } return false;
                case "theme.animationenabled":
                    if (bool.TryParse(value, out var enabled)) { resource = enabled; return true; } return false;
                case "theme.buttonpadding": case "theme.buttonmargin": case "theme.inputpadding": case "theme.comboboxpadding": case "theme.comboboxitempadding": case "theme.cardpadding": case "theme.dialogpadding": case "theme.datagridcellpadding": case "theme.datagridheaderpadding": case "theme.tabpadding":
                    if (ThemeValidator.TryThickness(value, out var thickness)) { resource = thickness; return true; } return false;
                case "theme.buttoncornerradius": case "theme.inputcornerradius": case "theme.comboboxcornerradius": case "theme.comboboxitemcornerradius": case "theme.cardcornerradius": case "theme.dialogcornerradius": case "theme.tabcornerradius": case "theme.switchcornerradius":
                    if (ThemeValidator.TryCornerRadius(value, out var corner)) { resource = corner; return true; } return false;
                case "theme.hoverduration":
                    if (TryDouble(value, out var hoverMs)) { resource = new Duration(TimeSpan.FromMilliseconds(Math.Max(0, hoverMs))); return true; } return false;
                case "theme.pressedduration":
                    if (TryDouble(value, out var pressedMs)) { resource = new Duration(TimeSpan.FromMilliseconds(Math.Max(0, pressedMs))); return true; } return false;
                default:
                    if (TryDouble(value, out var number)) { resource = number; return true; }
                    resource = value; return true;
            }
        }
        catch { return false; }
    }

    private static bool TryFontWeight(string value, out FontWeight weight)
    {
        var normalized = value.Trim();
        weight = normalized.ToLowerInvariant() switch
        {
            "thin" => FontWeights.Thin, "extralight" => FontWeights.ExtraLight, "light" => FontWeights.Light, "normal" => FontWeights.Normal,
            "medium" => FontWeights.Medium, "semibold" => FontWeights.SemiBold, "bold" => FontWeights.Bold, "extrabold" => FontWeights.ExtraBold,
            "black" => FontWeights.Black, "heavy" => FontWeights.Heavy, _ => FontWeights.Normal
        };
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) && numeric is >= 1 and <= 999) { weight = FontWeight.FromOpenTypeWeight(numeric); return true; }
        return normalized.ToLowerInvariant() is "thin" or "extralight" or "light" or "normal" or "medium" or "semibold" or "bold" or "extrabold" or "black" or "heavy";
    }

    private static bool TryDouble(string value, out double number) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    private static SolidColorBrush CreateBrush(string hex) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze(); return brush; }
}

public sealed record ThemePreferences(string ThemeId, ThemeMode Mode);
