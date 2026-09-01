using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

public static class BackupService
{
    public static List<BackupEntry> LoadLocalBackups()
    {
        AppPaths.EnsureDirectories();
        var result = new List<BackupEntry>();
        foreach (var jf in Directory.EnumerateFiles(AppPaths.EncDirectory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var img = Path.ChangeExtension(jf, ".img");
            if (!File.Exists(img)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jf));
                var root = doc.RootElement;
                result.Add(new BackupEntry
                {
                    ImgPath = img,
                    TitleId = Str(root, "title_id"),
                    SaveName = Str(root, "save_name"),
                    TitleName = Str(root, "title_name"),
                    Owner = Dict(root, "propietario"),
                    Origin = Dict(root, "origen"),
                    Date = Str(root, "fecha"),
                    Size = Long(root, "tamano", new FileInfo(img).Length),
                    Sha256 = Str(root, "sha256")
                });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                LogService.Write($"Backup ilegible {jf}: {ex.Message}", "WARN");
            }
        }
        return result;
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool VerifySha256(BackupEntry backup, out string actual)
    {
        actual = "";
        try
        {
            actual = ComputeSha256(backup.ImgPath);
            return !string.IsNullOrWhiteSpace(backup.Sha256) &&
                   string.Equals(actual, backup.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            LogService.Write($"No se pudo verificar SHA-256 de {backup.ImgPath}: {ex.Message}", "ERROR");
            return false;
        }
    }

    public static void SaveSidecar(string imgPath, TitleInfo title, string saveName, JsonElement sourceSave, ConsoleConfig console, long size)
    {
        var sha256 = ComputeSha256(imgPath);
        var meta = new
        {
            title_id = title.TitleId,
            save_name = saveName,
            title_name = title.TitleName,
            propietario = GarlicApi.Owner(sourceSave),
            origen = new { nombre = console.Name, ip = console.Ip },
            fecha = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            tamano = size,
            sha256
        };
        File.WriteAllText(Path.ChangeExtension(imgPath, ".json"), JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string ExportZip(IEnumerable<BackupEntry> backups, string destinationZip)
    {
        var entries = backups.ToList();
        if (entries.Count == 0) throw new GarlicException("No hay copias seleccionadas para exportar.");

        var fullZip = Path.GetFullPath(destinationZip);
        var parent = Path.GetDirectoryName(fullZip);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        if (File.Exists(fullZip)) File.Delete(fullZip);

        using var archive = ZipFile.Open(fullZip, ZipArchiveMode.Create);
        foreach (var backup in entries)
        {
            AddFile(archive, backup.ImgPath);
            var sidecar = Path.ChangeExtension(backup.ImgPath, ".json");
            if (File.Exists(sidecar)) AddFile(archive, sidecar);
        }
        return fullZip;
    }

    private static void AddFile(ZipArchive archive, string path)
    {
        if (!File.Exists(path)) throw new GarlicException($"No existe el archivo de backup: {path}");
        archive.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Fastest);
    }

    public static void DeleteLocal(BackupEntry backup)
    {
        try
        {
            File.Delete(backup.ImgPath);
            File.Delete(Path.ChangeExtension(backup.ImgPath, ".json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GarlicException($"No se pudo eliminar la copia local: {ex.Message}");
        }
    }

    private static string Str(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) ? v.ToString() : "";
    private static long Long(JsonElement root, string name, long fallback) => long.TryParse(Str(root, name), out var x) ? x : fallback;
    private static Dictionary<string, object?> Dict(JsonElement root, string name)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object) return d;
        foreach (var p in v.EnumerateObject()) d[p.Name] = p.Value.ToString();
        return d;
    }
}
