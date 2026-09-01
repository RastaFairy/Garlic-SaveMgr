namespace GarlicSaveMgr.Infrastructure;

public static class AppPaths
{
    /// <summary>Directorio raíz de la aplicación portátil: la carpeta donde se ejecuta el EXE.</summary>
    public static string RootDirectory { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Datos de configuración persistente, siempre dentro de la aplicación.</summary>
    public static string AppDataDirectory => Path.Combine(RootDirectory, "data");

    /// <summary>Copias y datos de usuario, siempre dentro de la aplicación.</summary>
    public static string BaseDirectory => Path.Combine(RootDirectory, "garlic_saves");
    public static string EncDirectory => Path.Combine(BaseDirectory, "enc");
    public static string LogsDirectory => Path.Combine(BaseDirectory, "logs");

    /// <summary>Caché local del payload, separada de las copias de seguridad.</summary>
    public static string PayloadDirectory => Path.Combine(RootDirectory, "payload_cache");
    /// <summary>Caché local de carátulas, siempre junto al ejecutable.</summary>
    public static string CoversDirectory => Path.Combine(RootDirectory, "covers");

    public static string SettingsFile => Path.Combine(AppDataDirectory, "settings.json");
    public static string ProfilesFile => Path.Combine(AppDataDirectory, "console_profiles.json");
    public static string MetadataFile => Path.Combine(AppDataDirectory, "game_metadata.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(EncDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(PayloadDirectory);
        Directory.CreateDirectory(CoversDirectory);
    }
}
