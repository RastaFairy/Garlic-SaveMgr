using System.Collections.ObjectModel;

namespace GarlicSaveMgr.Infrastructure;

public enum ThemeMode
{
    Light,
    Dark
}

public sealed class ThemeDefinition
{
    public const int CurrentSchema = 2;

    public string Id { get; init; } = "Default";
    public string Name { get; init; } = "Garlic Default";
    public string Author { get; init; } = "Garlic SaveMgr";
    public string Version { get; init; } = "1.0";
    public int Schema { get; init; } = CurrentSchema;
    public IReadOnlyDictionary<ThemeMode, IReadOnlyDictionary<string, string>> Palettes { get; init; }
        = EmptyModes();
    public IReadOnlyDictionary<ThemeMode, IReadOnlyDictionary<string, string>> Appearance { get; init; }
        = EmptyModes();

    public IReadOnlyDictionary<string, string> GetPalette(ThemeMode mode) =>
        Palettes.TryGetValue(mode, out var palette) ? palette : EmptyDictionary();

    public IReadOnlyDictionary<string, string> GetAppearance(ThemeMode mode) =>
        Appearance.TryGetValue(mode, out var values) ? values : EmptyDictionary();

    private static IReadOnlyDictionary<ThemeMode, IReadOnlyDictionary<string, string>> EmptyModes() =>
        new ReadOnlyDictionary<ThemeMode, IReadOnlyDictionary<string, string>>(
            new Dictionary<ThemeMode, IReadOnlyDictionary<string, string>>());

    private static IReadOnlyDictionary<string, string> EmptyDictionary() =>
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
