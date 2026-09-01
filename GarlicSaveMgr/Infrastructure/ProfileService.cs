using System.Text.Json;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Infrastructure;

public static class ProfileService
{
    private static string FilePath => AppPaths.ProfilesFile;

    public static List<ConsoleConfig> Load()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(FilePath)) return [];
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ConsoleConfig>>(json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogService.Write($"No se pudieron cargar perfiles: {ex.Message}", "WARN");
            return [];
        }
    }

    public static void Save(IEnumerable<ConsoleConfig> profiles)
    {
        AppPaths.EnsureDirectories();
        var normalized = profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => Clone(g.Last()))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Upsert(ConsoleConfig profile)
    {
        var profiles = Load();
        var existing = profiles.FindIndex(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) profiles[existing] = Clone(profile);
        else profiles.Add(Clone(profile));
        Save(profiles);
    }

    public static ConsoleConfig? Find(string name)
        => Load().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static ConsoleConfig Clone(ConsoleConfig p) => new() { Name = p.Name.Trim(), Ip = p.Ip.Trim(), Port = p.Port };
}
