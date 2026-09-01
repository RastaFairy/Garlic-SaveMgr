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

    public static bool LoadSimpleUi()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (File.Exists(UiSettingsFile))
            {
                var ui = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(UiSettingsFile));
                return ui?.SimpleUi ?? false;
            }
            // Migración de instalaciones anteriores donde SimpleUi vivía en settings.json.
            if (File.Exists(AppPaths.SettingsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
                if (doc.RootElement.TryGetProperty("SimpleUi", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return value.GetBoolean();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogService.Write($"No se pudo cargar el modo de interfaz: {ex.Message}", "WARN");
        }
        return false;
    }

    public static void SaveSimpleUi(bool simple)
    {
        AppPaths.EnsureDirectories();
        var temp = UiSettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new UiSettings(simple), JsonOptions));
        File.Move(temp, UiSettingsFile, true);
    }

    private sealed record UiSettings(bool SimpleUi);
}
