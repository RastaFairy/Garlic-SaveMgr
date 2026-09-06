using System.Collections.ObjectModel;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace GarlicSaveMgr.Infrastructure;

public static class ThemeLoader
{
    private const string DefaultResourceName = "GarlicSaveMgr.Resources.Themes.Default.xml";

    public static ThemeDefinition LoadDefault()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException($"No se encontró el tema interno '{DefaultResourceName}'.");
        using var reader = new StreamReader(stream);
        var xml = XDocument.Parse(reader.ReadToEnd(), LoadOptions.PreserveWhitespace);
        if (!ThemeValidator.TryValidateDocument(xml, out var errors))
            throw new InvalidDataException($"El tema interno estándar no es válido: {string.Join(" ", errors)}");
        return ParseAndMerge(xml, null, "Default");
    }

    public static ThemeDefinition LoadSelected(string? themeIdOrPath, out bool usedFallback)
    {
        usedFallback = false;
        var defaultTheme = LoadDefault();
        if (string.IsNullOrWhiteSpace(themeIdOrPath) || string.Equals(themeIdOrPath.Trim(), "Default", StringComparison.OrdinalIgnoreCase))
            return defaultTheme;

        try
        {
            var file = ResolveExternalTheme(themeIdOrPath);
            if (file is null || !File.Exists(file)) throw new FileNotFoundException("Tema externo no encontrado.");
            var xml = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            if (!ThemeValidator.TryValidateDocument(xml, out var errors))
                throw new InvalidDataException(string.Join(" ", errors));
            return ParseAndMerge(xml, defaultTheme, Path.GetFileNameWithoutExtension(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or ArgumentException)
        {
            LogService.Write($"Tema externo no válido; se usará el tema interno estándar: {ex.Message}", "WARN");
            usedFallback = true;
            return defaultTheme;
        }
    }

    public static IReadOnlyList<ThemeCatalogItem> ListAvailableThemes()
    {
        AppPaths.EnsureDirectories();
        var result = new List<ThemeCatalogItem> { new("Default", "Garlic Default", "Garlic SaveMgr") };

        foreach (var file in Directory.EnumerateFiles(AppPaths.ThemesDirectory, "*.xml", SearchOption.TopDirectoryOnly)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFileName(file), "ThemeTemplate.xml", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var xml = XDocument.Load(file, LoadOptions.None);
                var name = xml.Root?.Element("Metadata")?.Element("Name")?.Value?.Trim();
                var author = xml.Root?.Element("Metadata")?.Element("Author")?.Value?.Trim();
                result.Add(new ThemeCatalogItem(Path.GetFileNameWithoutExtension(file),
                    string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file) : name, author ?? ""));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
            {
                LogService.Write($"No se pudo enumerar el tema '{Path.GetFileName(file)}': {ex.Message}", "WARN");
            }
        }

        return result;
    }

    private static string? ResolveExternalTheme(string idOrPath)
    {
        AppPaths.EnsureDirectories();
        var input = idOrPath.Trim();
        if (Path.IsPathRooted(input)) return IsInsideThemeDirectory(input) ? input : null;
        var byId = Path.Combine(AppPaths.ThemesDirectory, input.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? input : input + ".xml");
        return IsInsideThemeDirectory(byId) ? byId : null;
    }

    private static bool IsInsideThemeDirectory(string path)
    {
        var root = Path.GetFullPath(AppPaths.ThemesDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static ThemeDefinition ParseAndMerge(XDocument xml, ThemeDefinition? fallback, string id)
    {
        var metadata = xml.Root?.Element("Metadata");
        var name = metadata?.Element("Name")?.Value?.Trim();
        var author = metadata?.Element("Author")?.Value?.Trim();
        var version = metadata?.Element("Version")?.Value?.Trim();
        var schemaText = metadata?.Element("ThemeSchema")?.Value?.Trim();
        _ = int.TryParse(schemaText, out var schema);
        if (schema <= 0) schema = ThemeDefinition.CurrentSchema;

        var palettes = new Dictionary<ThemeMode, IReadOnlyDictionary<string, string>>
        {
            [ThemeMode.Light] = Merge(fallback?.GetPalette(ThemeMode.Light), ReadColors(xml.Root?.Element("Light")?.Element("Colors"))),
            [ThemeMode.Dark] = Merge(fallback?.GetPalette(ThemeMode.Dark), ReadColors(xml.Root?.Element("Dark")?.Element("Colors")))
        };
        var appearance = new Dictionary<ThemeMode, IReadOnlyDictionary<string, string>>
        {
            [ThemeMode.Light] = Merge(fallback?.GetAppearance(ThemeMode.Light), ReadTokens(xml.Root?.Element("Light"))),
            [ThemeMode.Dark] = Merge(fallback?.GetAppearance(ThemeMode.Dark), ReadTokens(xml.Root?.Element("Dark")))
        };

        return new ThemeDefinition
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? fallback?.Name ?? id : name,
            Author = string.IsNullOrWhiteSpace(author) ? fallback?.Author ?? "" : author,
            Version = string.IsNullOrWhiteSpace(version) ? fallback?.Version ?? "1.0" : version,
            Schema = schema,
            Palettes = new ReadOnlyDictionary<ThemeMode, IReadOnlyDictionary<string, string>>(palettes),
            Appearance = new ReadOnlyDictionary<ThemeMode, IReadOnlyDictionary<string, string>>(appearance)
        };
    }

    private static IReadOnlyDictionary<string, string> ReadColors(XElement? colors)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (colors is null) return result.AsReadOnly();
        foreach (var element in colors.Elements())
            if (ThemeValidator.TryNormalizeHex(element.Value.Trim(), out var normalized)) result[element.Name.LocalName] = normalized;
        return result.AsReadOnly();
    }

    private static IReadOnlyDictionary<string, string> ReadTokens(XElement? mode)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (mode is null) return result.AsReadOnly();

        foreach (var section in mode.Elements().Where(x => !string.Equals(x.Name.LocalName, "Colors", StringComparison.OrdinalIgnoreCase)))
            Walk(section, section.Name.LocalName, result);
        return result.AsReadOnly();
    }

    private static void Walk(XElement element, string prefix, IDictionary<string, string> target)
    {
        if (!element.Elements().Any())
        {
            target[prefix] = element.Value.Trim();
            return;
        }
        foreach (var child in element.Elements())
            Walk(child, prefix + "." + child.Name.LocalName, target);
    }

    private static IReadOnlyDictionary<string, string> Merge(IReadOnlyDictionary<string, string>? fallback, IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(fallback ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overrides) merged[pair.Key] = pair.Value;
        return new ReadOnlyDictionary<string, string>(merged);
    }
}

public sealed record ThemeCatalogItem(string Id, string Name, string Author)
{
    public override string ToString() => Name;
}
