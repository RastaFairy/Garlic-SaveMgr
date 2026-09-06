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
            if (!File.Exists(FilePath)) return [];
            var json = File.ReadAllText(FilePath);
            var profiles = JsonSerializer.Deserialize<List<ConsoleConfig>>(json) ?? [];
            return Normalize(profiles);
        }
        catch { return []; }
    }

    public static void Save(IEnumerable<ConsoleConfig> profiles)
    {
        AppPaths.EnsureDirectories();
        var normalized = Normalize(profiles);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }

    public static void Upsert(ConsoleConfig profile)
    {
        var profiles = Load();
        var key = $"{profile.Ip.Trim()}:{profile.Port}";
        var existing = profiles.FindIndex(p => string.Equals($"{p.Ip.Trim()}:{p.Port}", key, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) profiles[existing] = Clone(profile);
        else profiles.Add(Clone(profile));
        Save(profiles);
    }

    public static ConsoleConfig? FindByAddress(string ip, int port)
        => Load().FirstOrDefault(p => string.Equals(p.Ip, ip, StringComparison.OrdinalIgnoreCase) && p.Port == port);

    public static ConsoleConfig? Find(string name)
        => Load().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static List<ConsoleConfig> Normalize(IEnumerable<ConsoleConfig> profiles)
        => profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Ip) && p.Port is >= 1 and <= 65535)
            .GroupBy(p => $"{p.Ip.Trim()}:{p.Port}", StringComparer.OrdinalIgnoreCase)
            .Select(g => Clone(g.Last()))
            .OrderBy(p => p.Ip, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Port)
            .ToList();

    private static ConsoleConfig Clone(ConsoleConfig p) => new()
    {
        Name = string.IsNullOrWhiteSpace(p.Name) ? "PS5" : p.Name.Trim(),
        Ip = p.Ip.Trim(),
        Port = p.Port
    };
}
