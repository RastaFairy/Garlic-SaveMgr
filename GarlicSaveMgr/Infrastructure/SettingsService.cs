using System.Text.Json;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Infrastructure;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string UiSettingsFile => Path.Combine(AppPaths.AppDataDirectory, "ui_settings.json");

    public static ConsoleConfig Load()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.SettingsFile)) return new ConsoleConfig();
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<ConsoleConfig>(json) ?? new ConsoleConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogService.Write($"No se pudieron cargar ajustes portátiles: {ex.Message}", "WARN");
            return new ConsoleConfig();
        }
    }

    public static void Save(ConsoleConfig cfg)
    {
        AppPaths.EnsureDirectories();
        var temp = AppPaths.SettingsFile + ".tmp";
        var json = JsonSerializer.Serialize(new ConsoleConfig { Name = cfg.Name, Ip = cfg.Ip, Port = cfg.Port }, JsonOptions);
        File.WriteAllText(temp, json);
        File.Move(temp, AppPaths.SettingsFile, true);
    }

    public static bool LoadSimpleUi() => LoadUiSettings().SimpleUi;

    public static void SaveSimpleUi(bool simple)
    {
        var current = LoadUiSettings();
        SaveUiSettings(current with { SimpleUi = simple });
    }

    public static ThemePreferences LoadThemePreferences()
    {
        var ui = LoadUiSettings();
        var mode = string.Equals(ui.ThemeMode, nameof(ThemeMode.Dark), StringComparison.OrdinalIgnoreCase)
            ? ThemeMode.Dark : ThemeMode.Light;
        return new ThemePreferences(string.IsNullOrWhiteSpace(ui.ThemeId) ? "Default" : ui.ThemeId, mode);
    }

    public static void SaveThemePreferences(string themeId, ThemeMode mode)
    {
        var current = LoadUiSettings();
        SaveUiSettings(current with
        {
            ThemeId = string.IsNullOrWhiteSpace(themeId) ? "Default" : themeId,
            ThemeMode = mode.ToString()
        });
    }

    private static UiSettings LoadUiSettings()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (File.Exists(UiSettingsFile))
            {
                var ui = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(UiSettingsFile));
                if (ui is not null) return ui;
            }

            if (File.Exists(AppPaths.SettingsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
                var simple = true;
                if (doc.RootElement.TryGetProperty("SimpleUi", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    simple = value.GetBoolean();
                return new UiSettings(simple, "Default", nameof(ThemeMode.Light));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogService.Write($"No se pudieron cargar preferencias de interfaz: {ex.Message}", "WARN");
        }

        return new UiSettings(true, "Default", nameof(ThemeMode.Light));
    }

    private static void SaveUiSettings(UiSettings settings)
    {
        AppPaths.EnsureDirectories();
        var temp = UiSettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, UiSettingsFile, true);
    }

    private sealed record UiSettings(bool SimpleUi, string? ThemeId, string? ThemeMode);
}
