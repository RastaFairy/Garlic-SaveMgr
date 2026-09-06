using System.Text.RegularExpressions;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

public sealed record OperationOutcome(int Succeeded, int Failed, bool Canceled);

public sealed record RestoreConflict(string TitleId, string TitleName, string SaveName);

public sealed class OperationRunner
{
    private CancellationTokenSource? _cts;
    public bool IsRunning => _cts is not null;

    public async Task<OperationOutcome> RunBackupAsync(
        IReadOnlyList<TitleInfo> titles,
        ConsoleConfig console,
        IProgress<(int Index, int Total, string TitleId, string Uid, string State)>? state,
        IProgress<(long Done, long Total)>? progress,
        Action<string,string>? log,
        bool smartBackup = true)
    {
        Cancel(); _cts = new CancellationTokenSource();
        var telemetryId = Infrastructure.ApplicationTelemetryService.StartOperation("BACKUP", $"Backup de {titles.Count} título(s)");
        var canceled = false;
        var ok = 0;
        var err = 0;
        try
        {
            using var api = new GarlicApi(console.Ip, console.Port);
            var total = titles.Count;
            for (var n = 0; n < total; n++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var t = titles[n];
                log?.Invoke($"\n{new string('─',58)}", "sep");
                log?.Invoke($"[{n+1}/{total}]  {t.TitleId}  {t.TitleName switch { "" => "—", _ => t.TitleName }}  ({t.SlotCount} slots)", "info");
                state?.Report((n,total,t.TitleId,t.Uid,"proc"));
                Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", total == 0 ? 1 : (double)n / total, t.TitleId);
                var slots = t.Slots.Where(s => !s.Backup).ToList(); if (slots.Count == 0) slots = t.Slots.ToList();
                var titleOk = true;
                foreach (var slot in slots)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    if (!await BackupSlotAsync(telemetryId, api, t, slot, console, progress, log, _cts.Token, smartBackup)) titleOk = false;
                }
                if (titleOk) { ok++; state?.Report((n,total,t.TitleId,t.Uid,"ok")); }
                else { err++; state?.Report((n,total,t.TitleId,t.Uid,"err")); }
            }
            progress?.Report((total, total));
            log?.Invoke($"\n{new string('═',58)}", "sep");
            log?.Invoke($"Fin:  {ok} OK  /  {err} errores  de {total} titulos.", "info");
        }
        catch (OperationCanceledException) { canceled = true; log?.Invoke("Cancelado.", "warn"); }
        catch (Exception ex) { err++; log?.Invoke($"ERR: {ex.Message}", "error"); }
        finally { Infrastructure.ApplicationTelemetryService.CompleteOperation(telemetryId, err == 0 && !canceled, canceled); _cts.Dispose(); _cts = null; }
        return new OperationOutcome(ok, err, canceled);
    }

    private static async Task<bool> BackupSlotAsync(Guid telemetryId, GarlicApi api, TitleInfo title, SlotInfo slot, ConsoleConfig console, IProgress<(long Done,long Total)>? progress, Action<string,string>? log, CancellationToken ct, bool smartBackup)
    {
        log?.Invoke($"  slot: {slot.Name}", "info");
        Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", null, $"{title.TitleId}/{slot.Name}");
        log?.Invoke("  buscando save en la consola...", "info");
        try
        {
            var saves = await api.SavesAsync(ct);
            Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", null, $"saves {title.TitleId}/{slot.Name}");
            JsonElement match = default; var found = false; var idx = -1;
            for (var i = 0; i < saves.Count; i++)
            {
                var s = saves[i];
                if (GarlicApi.GetString(s,"title_id") == title.TitleId && GarlicApi.GetString(s,"save_name") == slot.Name && GarlicApi.Norm(GarlicApi.GetString(s,"uid")) == GarlicApi.Norm(title.Uid))
                { idx = i; match = s; found = true; break; }
            }
            if (!found) { log?.Invoke("  ERR: save no encontrado en la consola", "error"); return false; }
            var latest = smartBackup ? SmartBackupService.FindLatestFor(title.TitleId, slot.Name, title.Uid) : null;
            var decision = smartBackup ? SmartBackupService.Decide(match, latest) : new SmartBackupDecision(false, "Smart Backup desactivado.");
            if (decision.Skip)
            {
                log?.Invoke($"  SMART BACKUP: omitido · {decision.Reason}", "ok");
                return true;
            }
            log?.Invoke($"  SMART BACKUP: {decision.Reason}", "info");
            log?.Invoke("  descargando copia de seguridad...", "info");
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var safeName = string.Join("_", slot.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var rawPath = Path.Combine(AppPaths.EncDirectory, $"{title.TitleId}_{safeName}_{ts}.img");
            var collision = 1;
            while (File.Exists(rawPath))
            {
                rawPath = Path.Combine(AppPaths.EncDirectory, $"{title.TitleId}_{safeName}_{ts}_{collision++}.img");
            }
            var size = await api.DownloadRawAsync(idx, rawPath, progress, ct);
            Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", null, $"downloaded {title.TitleId}/{slot.Name}");
            BackupService.SaveSidecar(rawPath, title, slot.Name, match, console, size);
            var created = BackupService.LoadSingleBackup(rawPath);
            SnapshotEngine.CreateForBackup(created, console);
            log?.Invoke($"  OK  {FormatBytes(size)}  guardado como {Path.GetFileName(rawPath)}", "ok");
            log?.Invoke("  SNAPSHOT: estado registrado.", "info");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log?.Invoke($"  ERR: {ex.Message}", "error"); return false; }
    }

    public async Task<List<RestoreConflict>> FindRestoreConflictsAsync(
        IReadOnlyList<BackupEntry> backups,
        ConsoleConfig console,
        CancellationToken ct = default)
    {
        using var api = new GarlicApi(console.Ip, console.Port);
        if (!await api.PingAsync(ct))
            throw new GarlicException("Sin conexion con la consola destino.");

        var profiles = await api.AccountIdsAsync(ct);
        if (profiles.Count == 0) profiles = await api.UsersAsync(ct);
        if (profiles.Count == 0)
            throw new GarlicException("La consola destino no reporto ningun perfil.");

        var assignments = new Dictionary<int, string>();
        for (var n = 0; n < backups.Count; n++)
        {
            ct.ThrowIfCancellationRequested();
            var profile = profiles.FirstOrDefault(p => GarlicApi.ProfileMatches(backups[n].Owner, p));
            if (profile.ValueKind != JsonValueKind.Undefined)
                assignments[n] = GarlicApi.ProfileImportValue(profile);
        }

        var saves = await api.SavesAsync(ct);
        var conflicts = new List<RestoreConflict>();

        for (var n = 0; n < backups.Count; n++)
        {
            ct.ThrowIfCancellationRequested();
            if (!assignments.TryGetValue(n, out var uid) || string.IsNullOrWhiteSpace(uid))
                continue;

            var b = backups[n];
            var exists = saves.Any(s =>
                string.Equals(GarlicApi.GetString(s, "title_id"), b.TitleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GarlicApi.GetString(s, "save_name"), b.SaveName, StringComparison.OrdinalIgnoreCase) &&
                SaveBelongsToUid(s, uid));

            if (exists)
            {
                conflicts.Add(new RestoreConflict(
                    b.TitleId,
                    string.IsNullOrWhiteSpace(b.TitleName) ? "Nombre no disponible" : b.TitleName,
                    b.SaveName));
            }
        }

        return conflicts;
    }

    private static bool SaveBelongsToUid(JsonElement save, string uid)
    {
        var expected = GarlicApi.Norm(uid);
        foreach (var key in new[] { "uid", "account_id", "id", "aid" })
        {
            var value = GarlicApi.GetString(save, key);
            if (!string.IsNullOrWhiteSpace(value) && GarlicApi.Norm(value) == expected)
                return true;
        }
        return false;
    }

    public async Task<OperationOutcome> RunRestoreAsync(IReadOnlyList<BackupEntry> backups, ConsoleConfig console, IProgress<(int Index,int Total,int Row,string State)>? state, IProgress<(long Done,long Total)>? progress, Action<string,string>? log)
    {
        Cancel(); _cts = new CancellationTokenSource();
        var telemetryId = Infrastructure.ApplicationTelemetryService.StartOperation("RESTORE", $"Restore de {backups.Count} backup(s)");
        var canceled = false;
        var ok = 0;
        var err = 0;
        try
        {
            using var api = new GarlicApi(console.Ip, console.Port);
            if (!await api.PingAsync(_cts.Token)) { log?.Invoke("Sin conexion con la consola destino.","error"); err++; return new OperationOutcome(ok, err, canceled); }
            var profiles = await api.AccountIdsAsync(_cts.Token); var source = "account_ids";
            if (profiles.Count == 0) { profiles = await api.UsersAsync(_cts.Token); source = "users"; }
            if (profiles.Count == 0) { log?.Invoke("La consola destino no reporto ningun perfil.","error"); err++; return new OperationOutcome(ok, err, canceled); }
            log?.Invoke("Verificando perfil de origen de cada copia...","info");
            var assignments = new Dictionary<int,string>(); var failures = new List<int>();
            for (var n=0; n<backups.Count; n++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var b=backups[n]; var profile=profiles.FirstOrDefault(p => GarlicApi.ProfileMatches(b.Owner,p));
                if (profile.ValueKind == JsonValueKind.Undefined)
                {
                    failures.Add(n);
                    state?.Report((n,backups.Count,n,"err"));
                }
                else
                {
                    assignments[n]=GarlicApi.ProfileImportValue(profile);
                }
            }

            var validCount = assignments.Count;
            if (failures.Count > 0)
                log?.Invoke($"Omitidas {failures.Count} copias sin un perfil compatible en la consola destino. Se restaurarán las {validCount} restantes.","warn");
            else
                log?.Invoke($"Perfil verificado en las {backups.Count} copias (via /{source}).","ok");

            for(var n=0;n<backups.Count;n++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", backups.Count == 0 ? 1 : (double)n / backups.Count, backups[n].TitleId);
                var b=backups[n];
                if (!assignments.TryGetValue(n, out var uid))
                    continue;

                state?.Report((n,backups.Count,n,"proc"));
                log?.Invoke($"\n{new string('─',58)}","sep");
                log?.Invoke($"[{n+1}/{backups.Count}]  {b.TitleId}  {b.TitleName}  ({b.SaveName})","info");
                try
                {
                    var extra = b.SaveName.StartsWith("sdimg_", StringComparison.OrdinalIgnoreCase) ? "ps4=1" : null;
                    Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", backups.Count == 0 ? 1 : (double)n / backups.Count, $"upload {b.TitleId}/{b.SaveName}");
                    var res=await api.PostFileAsync(b.ImgPath,uid,extra,progress,_cts.Token);
                    Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", backups.Count == 0 ? 1 : (double)n / backups.Count, $"finish {b.TitleId}/{b.SaveName}");
                    var fin=await api.ImportFinishAsync(uid,_cts.Token);
                    var finishOk=fin.ValueKind==System.Text.Json.JsonValueKind.Object && fin.TryGetProperty("ok",out var okEl) && okEl.ValueKind==System.Text.Json.JsonValueKind.True;
                    if(!finishOk){ log?.Invoke($"  ERR al finalizar: {(fin.TryGetProperty("error",out var e)?e.ToString():"?")}","error"); err++; state?.Report((n,backups.Count,n,"err")); continue; }
                    var match=res.ValueKind==System.Text.Json.JsonValueKind.Object && res.TryGetProperty("match",out var m)?m.ToString():"true";
                    var exists=res.ValueKind==System.Text.Json.JsonValueKind.Object && res.TryGetProperty("exists",out var ex)?ex.ToString():"false";
                    log?.Invoke($"  OK  coincidencia={(match=="True"||match=="true"?"si":"NO")}  existia={exists}","ok"); ok++; state?.Report((n,backups.Count,n,"ok"));
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){log?.Invoke($"  ERR: {ex.Message}","error"); err++; state?.Report((n,backups.Count,n,"err"));}
            }
            progress?.Report((backups.Count,backups.Count)); log?.Invoke($"\n{new string('═',58)}","sep"); log?.Invoke($"Fin restauracion:  {ok} OK  /  {err} errores  de {backups.Count}.","info");
        }
        catch(OperationCanceledException){canceled=true;log?.Invoke("Cancelado.","warn");}
        catch(Exception ex){err++;log?.Invoke($"ERR: {ex.Message}","error");}
        finally{Infrastructure.ApplicationTelemetryService.CompleteOperation(telemetryId, err == 0 && !canceled, canceled); _cts.Dispose(); _cts=null;}
        return new OperationOutcome(ok,err,canceled);
    }

    private static bool BackupBelongsToUid(BackupEntry backup, string uid)
    {
        if (backup.Owner.Count == 0) return false;

        foreach (var key in new[] { "uid", "id", "account_id", "aid" })
        {
            if (backup.Owner.TryGetValue(key, out var value)
                && !string.IsNullOrWhiteSpace(Convert.ToString(value))
                && GarlicApi.Norm(value) == GarlicApi.Norm(uid))
                return true;
        }

        return false;
    }

    private static bool SaveMatchesBackupOwner(JsonElement save, BackupEntry backup)
    {
        foreach (var key in new[] { "uid", "user_id", "account_id", "aid", "id" })
        {
            var saveUid = GarlicApi.GetString(save, key);
            if (string.IsNullOrWhiteSpace(saveUid)) continue;
            foreach (var ownerKey in new[] { "uid", "user_id", "account_id", "aid", "id" })
            {
                if (!backup.Owner.TryGetValue(ownerKey, out var ownerValue)) continue;
                var ownerUid = Convert.ToString(ownerValue);
                if (!string.IsNullOrWhiteSpace(ownerUid) && GarlicApi.Norm(saveUid) == GarlicApi.Norm(ownerUid))
                    return true;
            }
        }
        return false;
    }

    public async Task<IReadOnlyList<LocalBackupDeleteCheck>> CheckLocalBackupsBeforeDeleteAsync(
        IReadOnlyList<BackupEntry> backups,
        CancellationToken ct = default)
    {
        if (backups.Count == 0) return [];

        var result = new List<LocalBackupDeleteCheck>(backups.Count);

        foreach (var backup in backups)
        {
            ct.ThrowIfCancellationRequested();

            var originIp = GetOriginIp(backup);
            var consoleAddress = string.IsNullOrWhiteSpace(originIp) ? "origen desconocido" : $"{originIp}:8082";
            if (string.IsNullOrWhiteSpace(originIp))
            {
                result.Add(new LocalBackupDeleteCheck(
                    backup,
                    LocalBackupDeleteStatus.CannotVerify,
                    consoleAddress,
                    "La copia no contiene la IP de la consola de origen. No se puede comprobar si el savedata sigue en la consola."));
                continue;
            }

            if (backup.Owner.Count == 0)
            {
                result.Add(new LocalBackupDeleteCheck(
                    backup,
                    LocalBackupDeleteStatus.CannotVerify,
                    consoleAddress,
                    "La copia no contiene datos de propietario/user_id. No se puede confirmar que el savedata de la consola corresponda a esta copia."));
                continue;
            }

            try
            {
                using var api = new GarlicApi(originIp, 8082);
                var saves = await api.SavesAsync(ct);
                var matches = saves.Where(s =>
                        string.Equals(GarlicApi.GetString(s, "title_id"), backup.TitleId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(GarlicApi.GetString(s, "save_name"), backup.SaveName, StringComparison.OrdinalIgnoreCase)
                        && SaveMatchesBackupOwner(s, backup))
                    .ToList();

                if (matches.Count == 0)
                {
                    result.Add(new LocalBackupDeleteCheck(
                        backup,
                        LocalBackupDeleteStatus.MissingOnConsole,
                        consoleAddress,
                        "El savedata ya no existe en la consola de origen. Si se elimina esta copia local, no queda otra copia local asociada y la recuperación será imposible mediante Garlic SaveMgr."));
                }
                else
                {
                    result.Add(new LocalBackupDeleteCheck(
                        backup,
                        LocalBackupDeleteStatus.PresentOnConsole,
                        consoleAddress,
                        "El savedata sigue presente en la consola de origen."));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Add(new LocalBackupDeleteCheck(
                    backup,
                    LocalBackupDeleteStatus.CannotVerify,
                    consoleAddress,
                    $"No se pudo comprobar la consola de origen: {ex.Message}"));
            }
        }

        return result;
    }

    private static string GetOriginIp(BackupEntry backup)
    {
        foreach (var key in new[] { "ip", "IP", "direccion_ip", "address" })
        {
            if (backup.Origin.TryGetValue(key, out var value))
            {
                var text = Convert.ToString(value);
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
        }
        return "";
    }

    public async Task<IReadOnlyList<ConsoleDeletePreview>> PreviewDeleteAsync(IReadOnlyList<TitleInfo> titles, ConsoleConfig console, CancellationToken ct = default)
    {
        if (titles.Count == 0) return [];

        using var api = new GarlicApi(console.Ip, console.Port);
        var saves = await api.SavesAsync(ct);
        var localBackups = BackupService.LoadLocalBackups();
        var previews = new List<ConsoleDeletePreview>(titles.Count);

        foreach (var title in titles)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(title.Uid))
                throw new GarlicException($"No se pudo determinar el user_id de {title.TitleId}. La eliminación se ha bloqueado por seguridad.");

            var matching = saves
                .Where(s => string.Equals(GarlicApi.GetString(s, "title_id"), title.TitleId, StringComparison.OrdinalIgnoreCase)
                    && GarlicApi.Norm(GarlicApi.GetString(s, "uid")) == GarlicApi.Norm(title.Uid))
                .ToList();

            var slots = matching
                .Select(s => GarlicApi.GetString(s, "save_name"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var covered = localBackups
                .Where(b => string.Equals(b.TitleId, title.TitleId, StringComparison.OrdinalIgnoreCase))
                .Where(b => slots.Contains(b.SaveName, StringComparer.OrdinalIgnoreCase))
                .Where(b => BackupBelongsToUid(b, title.Uid))
                // A matching filename/owner is not enough to protect a destructive
                // console deletion. The local IMG must be physically present and
                // pass SHA-256 verification before the slot is considered covered.
                .Where(b =>
                {
                    var integrity = BackupService.VerifyIntegrity(b);
                    if (integrity.Status == BackupIntegrityStatus.Valid) return true;
                    LogService.Write($"Backup local no válido para protección de borrado: {b.TitleId} / {b.SaveName} ({integrity.Status}).", "WARN");
                    return false;
                })
                .Select(b => b.SaveName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            previews.Add(new ConsoleDeletePreview(
                title.TitleId, title.Uid, title.TitleName, slots, covered));
        }

        return previews;
    }

    public async Task BackupUncoveredSavesInternallyAsync(
        IReadOnlyList<ConsoleDeletePreview> previews,
        ConsoleConfig console,
        Action<string, string>? log,
        CancellationToken ct = default)
    {
        var targets = previews
            .Where(p => p.UncoveredSlotCount > 0)
            .SelectMany(p => p.SlotNames
                .Where(slot => !p.CoveredSlotNames.Contains(slot, StringComparer.OrdinalIgnoreCase))
                .Select(slot => (p.TitleId, p.Uid, p.TitleName, Slot: slot)))
            .ToList();

        if (targets.Count == 0) return;

        var ftpsrv = new FtpsrvService();
        if (!await ftpsrv.EnsureRunningAsync(console.Ip, log, ct))
            throw new GarlicException("No se pudo activar ftpsrv en la consola. No se ha eliminado ningún savedata.");

        using var api = new GarlicApi(console.Ip, console.Port);
        var saves = await api.SavesAsync(ct);
        var created = new List<string>();

        try
        {
            for (var n = 0; n < targets.Count; n++)
            {
                ct.ThrowIfCancellationRequested();
                var target = targets[n];
                var matches = saves
                    .Select((save, idx) => (save, idx))
                    .Where(x => string.Equals(GarlicApi.GetString(x.save, "title_id"), target.TitleId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(GarlicApi.GetString(x.save, "save_name"), target.Slot, StringComparison.OrdinalIgnoreCase)
                        && SaveBelongsToUid(x.save, target.Uid))
                    .ToList();

                if (matches.Count != 1)
                    throw new GarlicException($"No se pudo localizar de forma única {target.TitleId} / {target.Slot} en la consola.");

                var idx = matches[0].idx;
                var temp = Path.Combine(AppPaths.InternalBackupTempDirectory, $"{target.TitleId}_{SafeSegment(target.Slot)}_{Guid.NewGuid():N}.img");
                try
                {
                    log?.Invoke($"Respaldo interno [{n + 1}/{targets.Count}]: {target.TitleId} / {target.Slot}", "info");
                    await api.DownloadRawAsync(idx, temp, null, ct);
                    var size = new FileInfo(temp).Length;
                    var sha = BackupService.ComputeSha256(temp);
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var userDir = SafeSegment(target.Uid);
                    var titleDir = SafeSegment(target.TitleId);
                    var remoteBase = $"/data/backup_save_enc/{userDir}/{titleDir}";
                    var remoteName = $"{SafeSegment(target.Slot)}_{timestamp}_{Guid.NewGuid():N}.img";
                    var remotePath = $"{remoteBase}/{remoteName}";
                    await ftpsrv.UploadBackupAsync(console.Ip, remotePath, temp, ct);

                    var sidecarTemp = temp + ".json";
                    var metadata = JsonSerializer.Serialize(new
                    {
                        version = "6.8.7.23",
                        type = "internal-delete-backup",
                        title_id = target.TitleId,
                        save_name = target.Slot,
                        title_name = target.TitleName,
                        user_id = target.Uid,
                        fecha = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                        tamano = size,
                        sha256 = sha,
                        remote_path = remotePath
                    }, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(sidecarTemp, metadata, ct);
                    await ftpsrv.UploadBackupAsync(console.Ip, remotePath[..^4] + ".json", sidecarTemp, ct);
                    created.Add(remotePath);
                    log?.Invoke($"  respaldo interno OK  {FormatBytes(size)}  SHA-256={sha}", "ok");
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    try { if (File.Exists(temp + ".json")) File.Delete(temp + ".json"); } catch { }
                }
            }
        }
        catch
        {
            log?.Invoke("El respaldo interno previo a la eliminación no se completó. Se ha bloqueado el borrado por seguridad.", "error");
            // No se ejecuta DeleteAsync aquí. Los respaldos ya creados se conservan
            // deliberadamente para facilitar recuperación/diagnóstico manual.
            throw;
        }
    }

    private static string SafeSegment(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        var result = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    public async Task<IReadOnlyList<TrashEntry>> LoadInternalTrashAsync(ConsoleConfig console, Action<string, string>? log = null, CancellationToken ct = default)
    {
        var ftpsrv = new FtpsrvService();
        if (!await ftpsrv.EnsureRunningAsync(console.Ip, log, ct))
            throw new GarlicException("No se pudo activar ftpsrv para leer la Papelera.");

        var root = "/data/backup_save_enc";
        var result = new List<TrashEntry>();
        var userDirs = await ftpsrv.ListNamesAsync(console.Ip, root, ct);
        foreach (var userRaw in userDirs)
        {
            ct.ThrowIfCancellationRequested();
            var userDir = NormalizeFtpPath(userRaw);
            var userName = LeafFtpSegment(userDir);
            // Only directories directly below /data/backup_save_enc are valid
            // SaveMgr user folders. In particular, ftpsrv can expose parent
            // entries such as ../pldmgr; never descend into those paths.
            if (!IsSafeTrashSegment(userDir, userName)) continue;
            var userPath = $"{root}/{userName}";
            IReadOnlyList<string> titleDirs;
            try { titleDirs = await ftpsrv.ListNamesAsync(console.Ip, userPath, ct); }
            catch { continue; }
            foreach (var titleRaw in titleDirs)
            {
                ct.ThrowIfCancellationRequested();
                var titleDir = NormalizeFtpPath(titleRaw);
                var titleName = LeafFtpSegment(titleDir);
                // A title directory must also be a single safe path segment.
                if (!IsSafeTrashSegment(titleDir, titleName)) continue;
                var titlePath = $"{userPath}/{titleName}";
                IReadOnlyList<string> files;
                try { files = await ftpsrv.ListNamesAsync(console.Ip, titlePath, ct); }
                catch { continue; }
                foreach (var fileRaw in files)
                {
                    var normalizedFile = NormalizeFtpPath(fileRaw);
                    var fileName = LeafFtpSegment(normalizedFile);
                    // Only a filename directly in the current title directory is
                    // considered. Never follow parent/absolute traversal paths.
                    if (!IsSafeTrashSegment(normalizedFile, fileName)) continue;
                    // Only Garlic SaveMgr internal-trash sidecars belong here.
                    // Other JSON files can exist below the same FTP tree (for example
                    // pldmgr/repository_cache*.json) and are not trash metadata.
                    if (!IsInternalTrashSidecarFile(fileName)) continue;
                    var remoteJson = $"{titlePath}/{fileName}";
                    var remoteImg = remoteJson[..^5] + ".img";
                    var tempJson = Path.Combine(AppPaths.InternalBackupTempDirectory, $"trash_{Guid.NewGuid():N}.json");
                    try
                    {
                        Directory.CreateDirectory(AppPaths.InternalBackupTempDirectory);
                        await ftpsrv.DownloadAsync(console.Ip, remoteJson, tempJson, ct);
                        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(tempJson, ct));
                        var rootEl = doc.RootElement;
                        if (!string.Equals(GarlicApi.GetString(rootEl, "type"), "internal-delete-backup", StringComparison.OrdinalIgnoreCase)) continue;
                        long remoteSize = 0;
                        try { remoteSize = await ftpsrv.GetFileSizeAsync(console.Ip, remoteImg, ct); }
                        catch (Exception sizeEx) { log?.Invoke($"Papelera: no se pudo obtener tamaño real de {remoteImg}: {sizeEx.Message}", "warn"); }
                        var entry = new TrashEntry
                        {
                            TitleId = GarlicApi.GetString(rootEl, "title_id"),
                            SaveName = GarlicApi.GetString(rootEl, "save_name"),
                            TitleName = GarlicApi.GetString(rootEl, "title_name"),
                            UserId = FirstString(rootEl, "user_id", "uid"),
                            Date = FirstString(rootEl, "fecha", "date"),
                            Size = remoteSize,
                            Sha256 = GarlicApi.GetString(rootEl, "sha256"),
                            RemoteImgPath = remoteImg,
                            RemoteJsonPath = remoteJson
                        };
                        if (!string.IsNullOrWhiteSpace(entry.TitleId) && !string.IsNullOrWhiteSpace(entry.SaveName))
                            result.Add(entry);
                    }
                    catch (Exception ex) { log?.Invoke($"Papelera: no se pudo leer {remoteJson}: {ex.Message}", "warn"); }
                    finally { try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { } }
                }
            }
        }
        return result.OrderByDescending(x => x.Date, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.TitleName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task VerifyInternalTrashAsync(IReadOnlyList<TrashEntry> entries, ConsoleConfig console, Action<string, string>? log = null, CancellationToken ct = default)
    {
        if (entries.Count == 0) return;
        var ftpsrv = new FtpsrvService();
        if (!await ftpsrv.EnsureRunningAsync(console.Ip, log, ct)) throw new GarlicException("No se pudo activar ftpsrv para verificar la Papelera.");
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var temp = Path.Combine(AppPaths.InternalBackupTempDirectory, $"trash_verify_{Guid.NewGuid():N}.img");
            try
            {
                await ftpsrv.DownloadAsync(console.Ip, entry.RemoteImgPath, temp, ct);
                var actual = BackupService.ComputeSha256(temp);
                entry.IntegrityStatus = string.IsNullOrWhiteSpace(entry.Sha256)
                    ? BackupIntegrityStatus.MissingHash
                    : actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) ? BackupIntegrityStatus.Valid : BackupIntegrityStatus.Mismatch;
                log?.Invoke($"Papelera SHA-256 {(entry.IntegrityStatus == BackupIntegrityStatus.Valid ? "OK" : "ERROR")}: {entry.TitleId} / {entry.SaveName}", entry.IntegrityStatus == BackupIntegrityStatus.Valid ? "ok" : "error");
            }
            catch (Exception ex)
            {
                entry.IntegrityStatus = BackupIntegrityStatus.Error;
                log?.Invoke($"Papelera SHA-256 ERROR: {entry.TitleId} / {entry.SaveName}: {ex.Message}", "error");
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }
    }

    public async Task<IReadOnlyList<TrashEntry>> FindInternalTrashConflictsAsync(IReadOnlyList<TrashEntry> entries, ConsoleConfig console, CancellationToken ct = default)
    {
        if (entries.Count == 0) return [];
        using var api = new GarlicApi(console.Ip, console.Port);
        if (!await api.PingAsync(ct)) throw new GarlicException("Sin conexión con la consola destino.");
        var profiles = await api.AccountIdsAsync(ct);
        if (profiles.Count == 0) profiles = await api.UsersAsync(ct);
        if (profiles.Count == 0) throw new GarlicException("La consola destino no reportó ningún perfil.");
        var saves = await api.SavesAsync(ct);
        return entries.Where(entry => saves.Any(save =>
                string.Equals(GarlicApi.GetString(save, "title_id"), entry.TitleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GarlicApi.GetString(save, "save_name"), entry.SaveName, StringComparison.OrdinalIgnoreCase) &&
                SaveBelongsToUid(save, entry.UserId)))
            .ToList();
    }

    public async Task<OperationOutcome> RestoreInternalTrashAsync(IReadOnlyList<TrashEntry> entries, ConsoleConfig console, Action<string, string>? log = null)
    {
        var ok = 0; var err = 0;
        if (entries.Count == 0) return new OperationOutcome(0, 0, false);
        var ftpsrv = new FtpsrvService();
        if (!await ftpsrv.EnsureRunningAsync(console.Ip, log)) return new OperationOutcome(0, entries.Count, false);
        foreach (var entry in entries)
        {
            var tempImg = Path.Combine(AppPaths.InternalBackupTempDirectory, $"trash_restore_{Guid.NewGuid():N}.img");
            try
            {
                log?.Invoke($"Recuperando desde Papelera: {entry.TitleId} / {entry.SaveName}", "info");
                await ftpsrv.DownloadAsync(console.Ip, entry.RemoteImgPath, tempImg);
                var actual = BackupService.ComputeSha256(tempImg);
                if (string.IsNullOrWhiteSpace(entry.Sha256) || !actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new GarlicException("El respaldo interno no supera la verificación SHA-256.");
                var owner = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["uid"] = entry.UserId, ["user_id"] = entry.UserId, ["account_id"] = entry.UserId
                };
                var backup = new BackupEntry
                {
                    ImgPath = tempImg, TitleId = entry.TitleId, SaveName = entry.SaveName, TitleName = entry.TitleName,
                    Date = entry.Date, Size = entry.Size, Sha256 = entry.Sha256, Owner = owner
                };
                var outcome = await RunRestoreAsync([backup], console, null, null, log);
                if (outcome.Succeeded == 1)
                {
                    // Only remove the PS5-trash entry after the restored savedata
                    // has been accepted successfully by Garlic. If cleanup fails,
                    // keep the trash entry and report the cleanup problem.
                    try
                    {
                        var cleanupFtp = new FtpsrvService();
                        if (!await cleanupFtp.EnsureRunningAsync(console.Ip, log))
                            throw new GarlicException("No se pudo activar ftpsrv para limpiar la Papelera PS5 tras restaurar.");
                        await cleanupFtp.DeleteRemoteAsync(console.Ip, entry.RemoteImgPath, CancellationToken.None);
                        try { await cleanupFtp.DeleteRemoteAsync(console.Ip, entry.RemoteJsonPath, CancellationToken.None); } catch { }
                        ok++;
                        log?.Invoke($"Papelera restaurada y retirada correctamente: {entry.TitleId} / {entry.SaveName}", "ok");
                    }
                    catch (Exception cleanupEx)
                    {
                        err++;
                        log?.Invoke($"Restauración OK, pero no se pudo retirar la entrada de Papelera {entry.TitleId} / {entry.SaveName}: {cleanupEx.Message}", "warn");
                    }
                }
                else err++;
            }
            catch (Exception ex) { err++; log?.Invoke($"ERR restaurando desde Papelera {entry.TitleId} / {entry.SaveName}: {ex.Message}", "error"); }
            finally { try { if (File.Exists(tempImg)) File.Delete(tempImg); } catch { } }
        }
        return new OperationOutcome(ok, err, false);
    }

    public async Task DeleteInternalTrashAsync(IReadOnlyList<TrashEntry> entries, ConsoleConfig console, Action<string, string>? log = null, CancellationToken ct = default)
    {
        if (entries.Count == 0) return;
        var ftpsrv = new FtpsrvService();
        if (!await ftpsrv.EnsureRunningAsync(console.Ip, log, ct)) throw new GarlicException("No se pudo activar ftpsrv para eliminar de la Papelera.");
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            await ftpsrv.DeleteRemoteAsync(console.Ip, entry.RemoteImgPath, ct);
            try { await ftpsrv.DeleteRemoteAsync(console.Ip, entry.RemoteJsonPath, ct); } catch { }
            log?.Invoke($"Eliminado definitivamente de Papelera: {entry.TitleId} / {entry.SaveName}", "ok");
        }
    }

    private static bool IsInternalTrashSidecarFile(string fileName)
    {
        // Sidecars are generated from:
        // <slot>_yyyyMMdd_HHmmss_fff_<32-hex-guid>.json
        return Regex.IsMatch(
            fileName,
            @"^(?i:[A-Za-z0-9._-]+)_\d{8}_\d{6}_\d{3}_[0-9a-f]{32}\.json$");
    }

    private static string LeafFtpSegment(string value)
    {
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? "" : segments[^1];
    }

    private static bool IsSafeTrashSegment(string normalizedPath, string leaf)
    {
        if (string.IsNullOrWhiteSpace(leaf)) return false;
        if (!string.Equals(normalizedPath, leaf, StringComparison.Ordinal)) return false;
        if (leaf is "." or "..") return false;
        if (leaf.Contains('/') || leaf.Contains('\\')) return false;
        if (leaf.Contains("..", StringComparison.Ordinal)) return false;
        return Regex.IsMatch(leaf, @"^[A-Za-z0-9._-]{1,128}$");
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

    private static string NormalizeFtpPath(string value)
        => value.Trim().Replace('\\', '/').TrimEnd('/');

    public async Task<OperationOutcome> RunDeleteAsync(IReadOnlyList<TitleInfo> titles, ConsoleConfig console, IProgress<(int Index,int Total,string TitleId,string Uid,string State)>? state, Action<string,string>? log)
    {
        Cancel(); _cts=new CancellationTokenSource();
        var telemetryId=Infrastructure.ApplicationTelemetryService.StartOperation("DELETE", $"Eliminación de {titles.Count} título(s)");
        var canceled = false;
        var ok = 0;
        var err = 0;
        try
        {
            using var api=new GarlicApi(console.Ip,console.Port); var total=titles.Count;
            for(var n=0;n<total;n++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var t=titles[n];
                Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", total == 0 ? 1 : (double)n / total, t.TitleId); state?.Report((n,total,t.TitleId,t.Uid,"proc")); log?.Invoke($"[{n+1}/{total}] {t.TitleId} {t.TitleName}","warn");
                try
                {
                    var saves=await api.SavesAsync(_cts.Token); var idxs=new List<int>();
                    for(var i=0;i<saves.Count;i++)
                    {
                        var s=saves[i];
                        if(string.Equals(GarlicApi.GetString(s,"title_id"),t.TitleId,StringComparison.OrdinalIgnoreCase)
                            && SaveBelongsToUid(s, t.Uid))
                            idxs.Add(i);
                    }
                    if (idxs.Count == 0)
                        throw new GarlicException($"No se encontraron savedata actuales para {t.TitleId} / {t.Uid}. No se ha borrado nada.");

                    foreach(var idx in idxs.OrderByDescending(x=>x))
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        await api.DeleteAsync(idx,_cts.Token);
                        log?.Invoke($"  eliminado slot idx={idx}","ok");
                    }
                    ok++; state?.Report((n,total,t.TitleId,t.Uid,"ok"));
                }catch(OperationCanceledException){throw;}catch(Exception ex){log?.Invoke($"  ERR: {ex.Message}","error");err++;state?.Report((n,total,t.TitleId,t.Uid,"err"));}
            }
            log?.Invoke($"Fin eliminacion:  {ok} OK  /  {err} errores  de {total} titulos.","info");
        }
        catch(OperationCanceledException){canceled=true;log?.Invoke("Cancelado.","warn");}
        catch(Exception ex){err++;log?.Invoke($"ERR: {ex.Message}","error");}
        finally{Infrastructure.ApplicationTelemetryService.CompleteOperation(telemetryId, err == 0 && !canceled, canceled); _cts.Dispose();_cts=null;}
        return new OperationOutcome(ok,err,canceled);
    }

    public void Cancel() => _cts?.Cancel();
    private static string FormatBytes(long n){double d=n; foreach(var u in new[]{"B","KB","MB","GB"}){if(d<1024)return $"{d:0} {u}";d/=1024;}return $"{d:0.0} TB";}
}
