using System.Security.Cryptography;
using System.Text.Json;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

/// <summary>
/// Gestor de la Papelera interna del PC de Garlic SaveMgr.
/// NO utiliza ni interactúa con la Papelera de reciclaje de Windows.
/// Mantiene cada operación como una carpeta aislada dentro de pc_trash, con su
/// IMG y sidecar JSON, siguiendo el mismo modelo lógico de la Papelera PS5:
/// validar -> trasladar -> restaurar o eliminar definitivamente.
/// </summary>
public static class PcTrashService
{
    public static List<PcTrashEntry> Load()
    {
        AppPaths.EnsureDirectories();
        var result = new List<PcTrashEntry>();

        foreach (var dir in Directory.EnumerateDirectories(AppPaths.PcTrashDirectory)
                     .OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            try
            {
                var imgs = Directory.EnumerateFiles(dir, "*.img", SearchOption.TopDirectoryOnly).ToList();
                if (imgs.Count == 0) continue;

                // Una carpeta de Papelera representa una única operación.
                var img = imgs[0];
                var json = Path.ChangeExtension(img, ".json");
                var info = new FileInfo(img);
                var titleId = "";
                var saveName = Path.GetFileNameWithoutExtension(img);
                var titleName = "";
                var userId = "";
                var sourceConsole = "";
                var date = Directory.GetLastWriteTime(dir).ToString("yyyy-MM-ddTHH:mm:ss");
                var size = info.Length;
                var sha = "";

                if (File.Exists(json))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(json));
                    var root = doc.RootElement;
                    titleId = GarlicApi.GetString(root, "title_id");
                    saveName = GarlicApi.GetString(root, "save_name") is { Length: > 0 } sn ? sn : saveName;
                    titleName = GarlicApi.GetString(root, "title_name");
                    date = FirstString(root, "fecha", "date") is { Length: > 0 } dt ? dt : date;
                    sha = GarlicApi.GetString(root, "sha256");
                    if (root.TryGetProperty("propietario", out var owner) && owner.ValueKind == JsonValueKind.Object)
                        userId = FirstString(owner, "uid", "user_id", "account_id", "id", "aid");
                    sourceConsole = ReadOrigin(root);
                }

                if (string.IsNullOrWhiteSpace(titleId))
                    titleId = "—";

                result.Add(new PcTrashEntry
                {
                    TrashDirectory = dir,
                    ImgPath = img,
                    JsonPath = File.Exists(json) ? json : "",
                    TitleId = titleId,
                    SaveName = saveName,
                    TitleName = string.IsNullOrWhiteSpace(titleName) ? "Nombre no disponible" : titleName,
                    UserId = string.IsNullOrWhiteSpace(userId) ? "—" : userId,
                    SourceConsole = string.IsNullOrWhiteSpace(sourceConsole) ? "—" : sourceConsole,
                    Date = date,
                    Size = size,
                    Sha256 = sha
                });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                LogService.Write($"Papelera PC ilegible {dir}: {ex.Message}", "WARN");
            }
        }

        return result;
    }

    public static void Verify(IEnumerable<PcTrashEntry> entries)
    {
        foreach (var entry in entries)
        {
            try
            {
                if (!File.Exists(entry.ImgPath))
                {
                    entry.IntegrityStatus = BackupIntegrityStatus.Error;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Sha256))
                {
                    entry.IntegrityStatus = BackupIntegrityStatus.MissingHash;
                    continue;
                }

                var actual = BackupService.ComputeSha256(entry.ImgPath);
                entry.IntegrityStatus = string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? BackupIntegrityStatus.Valid
                    : BackupIntegrityStatus.Mismatch;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                entry.IntegrityStatus = BackupIntegrityStatus.Error;
                LogService.Write($"No se pudo verificar Papelera PC {entry.ImgPath}: {ex.Message}", "ERROR");
            }
        }
    }

    public static IReadOnlyList<PcTrashEntry> FindRestoreConflicts(IEnumerable<PcTrashEntry> entries)
        => entries.Where(e =>
        {
            if (string.IsNullOrWhiteSpace(e.TitleId) || e.TitleId == "—") return false;
            var targetImg = Path.Combine(AppPaths.EncDirectory, Path.GetFileName(e.ImgPath));
            var targetJson = string.IsNullOrWhiteSpace(e.JsonPath) ? "" : Path.Combine(AppPaths.EncDirectory, Path.GetFileName(e.JsonPath));
            return File.Exists(targetImg) || (!string.IsNullOrWhiteSpace(targetJson) && File.Exists(targetJson));
        }).ToList();

    public static int Restore(IEnumerable<PcTrashEntry> entries)
    {
        AppPaths.EnsureDirectories();
        var restored = 0;
        foreach (var entry in entries)
        {
            if (!File.Exists(entry.ImgPath))
                throw new GarlicException($"No existe el archivo de la Papelera PC: {entry.ImgPath}");

            // La entrada de Papelera se vuelve a verificar justo antes de restaurar.
            // Así no dependemos únicamente del SHA calculado durante el último refresco
            // de la interfaz y evitamos restaurar un fichero que haya cambiado después.
            var expectedSha = entry.Sha256;
            if (string.IsNullOrWhiteSpace(expectedSha))
                throw new GarlicException($"No se puede restaurar {entry.TitleId} / {entry.SaveName}: la Papelera no contiene SHA-256.");
            var actualSha = BackupService.ComputeSha256(entry.ImgPath);
            if (!actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new GarlicException($"No se puede restaurar {entry.TitleId} / {entry.SaveName}: la integridad SHA-256 no coincide.");

            var destinationImg = Path.Combine(AppPaths.EncDirectory, Path.GetFileName(entry.ImgPath));
            var destinationJson = string.IsNullOrWhiteSpace(entry.JsonPath) ? "" : Path.Combine(AppPaths.EncDirectory, Path.GetFileName(entry.JsonPath));
            if (File.Exists(destinationImg) || (!string.IsNullOrWhiteSpace(destinationJson) && File.Exists(destinationJson)))
                throw new GarlicException($"Ya existe una copia activa con el mismo nombre: {Path.GetFileName(destinationImg)}");

            var movedImg = false;
            try
            {
                File.Move(entry.ImgPath, destinationImg);
                movedImg = true;
                if (!string.IsNullOrWhiteSpace(entry.JsonPath) && File.Exists(entry.JsonPath))
                    File.Move(entry.JsonPath, destinationJson);

                if (Directory.Exists(entry.TrashDirectory) && !Directory.EnumerateFileSystemEntries(entry.TrashDirectory).Any())
                    Directory.Delete(entry.TrashDirectory);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(destinationJson) && File.Exists(destinationJson) && !File.Exists(entry.JsonPath))
                        File.Move(destinationJson, entry.JsonPath);
                    if (movedImg && File.Exists(destinationImg) && !File.Exists(entry.ImgPath))
                        File.Move(destinationImg, entry.ImgPath);
                }
                catch { }
                throw new GarlicException($"No se pudo restaurar la Papelera PC: {ex.Message}");
            }
        }
        return restored;
    }

    public static int DeletePermanently(IEnumerable<PcTrashEntry> entries)
    {
        var deleted = 0;
        foreach (var entry in entries)
        {
            if (!Directory.Exists(entry.TrashDirectory)) continue;
            Directory.Delete(entry.TrashDirectory, recursive: true);
            deleted++;
        }
        return deleted;
    }

    private static string ReadOrigin(JsonElement root)
    {
        if (!root.TryGetProperty("origen", out var origin) || origin.ValueKind != JsonValueKind.Object) return "";
        var name = FirstString(origin, "nombre", "name");
        var ip = FirstString(origin, "ip", "host");
        return string.IsNullOrWhiteSpace(name) ? ip : string.IsNullOrWhiteSpace(ip) ? name : $"{name} ({ip})";
    }

    private static string FirstString(JsonElement e, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GarlicApi.GetString(e, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static long GetLong(JsonElement e, params string[] names)
    {
        foreach (var name in names)
        {
            if (!e.TryGetProperty(name, out var p)) continue;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
            if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out n)) return n;
        }
        return 0;
    }
}
