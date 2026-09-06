using System.IO.Compression;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text.Json;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

public static class BackupService
{
    private sealed record IntegrityCacheEntry(long Length, DateTime LastWriteUtc, string Sha256, BackupIntegrityResult Result);
    private static readonly ConcurrentDictionary<string, IntegrityCacheEntry> IntegrityCache = new(StringComparer.OrdinalIgnoreCase);

    private static int _consoleInternalDeleteGuard;

    public static IDisposable SuppressPcTrashMoves()
    {
        Interlocked.Increment(ref _consoleInternalDeleteGuard);
        return new PcTrashMoveGuard();
    }

    private sealed class PcTrashMoveGuard : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref _consoleInternalDeleteGuard);
    }

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
                    Size = new FileInfo(img).Length,
                    Sha256 = Str(root, "sha256"),
                    RemoteFingerprint = Str(root, "remote_fingerprint")
                });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                LogService.Write($"Backup ilegible {jf}: {ex.Message}", "WARN");
            }
        }
        return result;
    }

    public static BackupEntry LoadSingleBackup(string imgPath)
    {
        var jf = Path.ChangeExtension(imgPath, ".json");
        if (!File.Exists(imgPath) || !File.Exists(jf))
            throw new GarlicException($"No existe el backup o su sidecar: {imgPath}");
        using var doc = JsonDocument.Parse(File.ReadAllText(jf));
        var root = doc.RootElement;
        return new BackupEntry
        {
            ImgPath = imgPath,
            TitleId = Str(root, "title_id"),
            SaveName = Str(root, "save_name"),
            TitleName = Str(root, "title_name"),
            Owner = Dict(root, "propietario"),
            Origin = Dict(root, "origen"),
            Date = Str(root, "fecha"),
            Size = new FileInfo(imgPath).Length,
            Sha256 = Str(root, "sha256"),
            RemoteFingerprint = Str(root, "remote_fingerprint")
        };
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static BackupIntegrityResult VerifyIntegrity(BackupEntry backup)
    {
        if (string.IsNullOrWhiteSpace(backup.Sha256))
        {
            return new BackupIntegrityResult(BackupIntegrityStatus.MissingHash, "", "", "La copia no contiene un SHA-256 de referencia.");
        }

        try
        {
            var info = new FileInfo(backup.ImgPath);
            if (!info.Exists)
                return new BackupIntegrityResult(BackupIntegrityStatus.Error, backup.Sha256, "", "El fichero de backup no existe.");

            var key = Path.GetFullPath(backup.ImgPath);
            if (IntegrityCache.TryGetValue(key, out var cached) &&
                cached.Length == info.Length &&
                cached.LastWriteUtc == info.LastWriteTimeUtc &&
                string.Equals(cached.Sha256, backup.Sha256, StringComparison.OrdinalIgnoreCase))
                return cached.Result;

            var actual = ComputeSha256(backup.ImgPath);
            var valid = string.Equals(actual, backup.Sha256, StringComparison.OrdinalIgnoreCase);
            var result = new BackupIntegrityResult(
                valid ? BackupIntegrityStatus.Valid : BackupIntegrityStatus.Mismatch,
                backup.Sha256, actual, valid ? "" : "El SHA-256 calculado no coincide con el registrado.");
            IntegrityCache[key] = new IntegrityCacheEntry(info.Length, info.LastWriteTimeUtc, backup.Sha256, result);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            LogService.Write($"No se pudo verificar SHA-256 de {backup.ImgPath}: {ex.Message}", "ERROR");
            return new BackupIntegrityResult(BackupIntegrityStatus.Error, backup.Sha256, "", ex.Message);
        }
    }

    public static bool VerifySha256(BackupEntry backup, out string actual)
    {
        var result = VerifyIntegrity(backup);
        actual = result.ActualSha256;
        return result.Status == BackupIntegrityStatus.Valid;
    }

    public static void SaveSidecar(string imgPath, TitleInfo title, string saveName, JsonElement sourceSave, ConsoleConfig console, long size)
    {
        var actualSize = new FileInfo(imgPath).Length;
        var sha256 = ComputeSha256(imgPath);
        var meta = new
        {
            title_id = title.TitleId,
            save_name = saveName,
            title_name = title.TitleName,
            propietario = GarlicApi.Owner(sourceSave),
            origen = new { nombre = console.Name, ip = console.Ip },
            fecha = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            tamano = actualSize,
            sha256,
            remote_fingerprint = SmartBackupService.RemoteFingerprint(sourceSave)
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

    /// <summary>
    /// Retira la copia de la lista activa y la mueve a la Papelera local del PC.
    /// No elimina físicamente la copia.
    /// </summary>
    public static string MoveLocalToPcTrash(BackupEntry backup)
    {
        if (Volatile.Read(ref _consoleInternalDeleteGuard) > 0)
            throw new GarlicException("Movimiento a Papelera PC bloqueado durante una eliminación de consola con respaldo interno.");

        AppPaths.EnsureDirectories();

        var sourceImg = Path.GetFullPath(backup.ImgPath);
        var sourceJson = Path.ChangeExtension(sourceImg, ".json");
        if (!File.Exists(sourceImg))
            throw new GarlicException($"No existe la copia local: {sourceImg}");
        if (!File.Exists(sourceJson))
            throw new GarlicException($"No existe el sidecar de la copia local: {sourceJson}");

        // La Papelera PC sigue el mismo principio de seguridad que la Papelera PS5:
        // antes de retirar un elemento del almacenamiento activo, la copia debe
        // estar físicamente presente y superar su integridad SHA-256. Esta garantía
        // se comprueba aquí, en el punto real de movimiento, para que ninguna llamada
        // futura pueda saltarse accidentalmente la protección de la UI.
        var integrity = VerifyIntegrity(backup);
        if (integrity.Status != BackupIntegrityStatus.Valid)
            throw new GarlicException($"No se puede mover a la Papelera del PC {backup.TitleId} / {backup.SaveName}: la integridad no está validada ({integrity.Status}).");

        // Una carpeta exclusiva por operación evita colisiones y mantiene IMG+JSON juntos.
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var safeTitle = SanitizePathPart(backup.TitleId);
        var safeSave = SanitizePathPart(backup.SaveName);
        var folderName = $"{stamp}_{safeTitle}_{safeSave}_{Guid.NewGuid():N}";
        var destinationDir = Path.Combine(AppPaths.PcTrashDirectory, folderName);
        Directory.CreateDirectory(destinationDir);

        var destinationImg = Path.Combine(destinationDir, Path.GetFileName(sourceImg));
        var destinationJson = Path.Combine(destinationDir, Path.GetFileName(sourceJson));
        var movedImg = false;
        try
        {
            File.Move(sourceImg, destinationImg);
            movedImg = true;
            if (File.Exists(sourceJson))
                File.Move(sourceJson, destinationJson);

            return destinationDir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Intento de rollback para no dejar la copia activa a medias.
            try
            {
                if (File.Exists(destinationJson) && !File.Exists(sourceJson)) File.Move(destinationJson, sourceJson);
                if (movedImg && File.Exists(destinationImg) && !File.Exists(sourceImg)) File.Move(destinationImg, sourceImg);
                if (Directory.Exists(destinationDir) && !Directory.EnumerateFileSystemEntries(destinationDir).Any()) Directory.Delete(destinationDir);
            }
            catch { }
            throw new GarlicException($"No se pudo mover la copia local a la Papelera del PC: {ex.Message}");
        }
    }

    /// <summary>
    /// Elimina definitivamente un backup local después de verificar su integridad.
    /// No utiliza la Papelera de Windows.
    /// </summary>
    public static void DeleteLocalPermanently(BackupEntry backup)
    {
        AppPaths.EnsureDirectories();
        var sourceImg = Path.GetFullPath(backup.ImgPath);
        var sourceJson = Path.ChangeExtension(sourceImg, ".json");
        if (!File.Exists(sourceImg))
            throw new GarlicException($"No existe la copia local: {sourceImg}");

        var integrity = VerifyIntegrity(backup);
        if (integrity.Status != BackupIntegrityStatus.Valid)
            throw new GarlicException($"No se puede eliminar definitivamente {backup.TitleId} / {backup.SaveName}: la integridad no está validada ({integrity.Status}).");

        var stagingRoot = Path.Combine(AppPaths.PcTrashDirectory, ".delete_staging");
        Directory.CreateDirectory(stagingRoot);
        var stagingDir = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        var stagedImg = Path.Combine(stagingDir, Path.GetFileName(sourceImg));
        var stagedJson = Path.Combine(stagingDir, Path.GetFileName(sourceJson));
        var movedImg = false;
        var movedJson = false;
        try
        {
            File.Move(sourceImg, stagedImg);
            movedImg = true;
            if (File.Exists(sourceJson))
            {
                File.Move(sourceJson, stagedJson);
                movedJson = true;
            }

            File.Delete(stagedImg);
            if (movedJson && File.Exists(stagedJson)) File.Delete(stagedJson);
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (movedJson && File.Exists(stagedJson) && !File.Exists(sourceJson)) File.Move(stagedJson, sourceJson);
                if (movedImg && File.Exists(stagedImg) && !File.Exists(sourceImg)) File.Move(stagedImg, sourceImg);
                if (Directory.Exists(stagingDir) && !Directory.EnumerateFileSystemEntries(stagingDir).Any()) Directory.Delete(stagingDir);
            }
            catch { }
            throw new GarlicException($"No se pudo eliminar definitivamente la copia local: {ex.Message}");
        }
    }

    // Compatibilidad para llamadas antiguas: ya no borra físicamente.
    public static void DeleteLocal(BackupEntry backup) => MoveLocalToPcTrash(backup);

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var text = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(text) ? "backup" : text;
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
