using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr.Services;

public sealed record OperationOutcome(int Succeeded, int Failed, bool Canceled);

public sealed class OperationRunner
{
    private CancellationTokenSource? _cts;
    public bool IsRunning => _cts is not null;

    public async Task<OperationOutcome> RunBackupAsync(
        IReadOnlyList<TitleInfo> titles,
        ConsoleConfig console,
        IProgress<(int Index, int Total, string TitleId, string Uid, string State)>? state,
        IProgress<(long Done, long Total)>? progress,
        Action<string,string>? log)
    {
        Cancel(); _cts = new CancellationTokenSource();
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
                var slots = t.Slots.Where(s => !s.Backup).ToList(); if (slots.Count == 0) slots = t.Slots.ToList();
                var titleOk = true;
                foreach (var slot in slots)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    if (!await BackupSlotAsync(api, t, slot, console, progress, log, _cts.Token)) titleOk = false;
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
        finally { _cts.Dispose(); _cts = null; }
        return new OperationOutcome(ok, err, canceled);
    }

    private static async Task<bool> BackupSlotAsync(GarlicApi api, TitleInfo title, SlotInfo slot, ConsoleConfig console, IProgress<(long Done,long Total)>? progress, Action<string,string>? log, CancellationToken ct)
    {
        log?.Invoke($"  slot: {slot.Name}", "info");
        log?.Invoke("  buscando save en la consola...", "info");
        try
        {
            var saves = await api.SavesAsync(ct);
            JsonElement match = default; var found = false; var idx = -1;
            for (var i = 0; i < saves.Count; i++)
            {
                var s = saves[i];
                if (GarlicApi.GetString(s,"title_id") == title.TitleId && GarlicApi.GetString(s,"save_name") == slot.Name && GarlicApi.Norm(GarlicApi.GetString(s,"uid")) == GarlicApi.Norm(title.Uid))
                { idx = i; match = s; found = true; break; }
            }
            if (!found) { log?.Invoke("  ERR: save no encontrado en la consola", "error"); return false; }
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
            BackupService.SaveSidecar(rawPath, title, slot.Name, match, console, size);
            log?.Invoke($"  OK  {FormatBytes(size)}  guardado como {Path.GetFileName(rawPath)}", "ok");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log?.Invoke($"  ERR: {ex.Message}", "error"); return false; }
    }

    public async Task<OperationOutcome> RunRestoreAsync(IReadOnlyList<BackupEntry> backups, ConsoleConfig console, IProgress<(int Index,int Total,int Row,string State)>? state, IProgress<(long Done,long Total)>? progress, Action<string,string>? log)
    {
        Cancel(); _cts = new CancellationTokenSource();
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
                if (profile.ValueKind == JsonValueKind.Undefined) failures.Add(n); else assignments[n]=GarlicApi.ProfileImportValue(profile);
            }
            if (failures.Count > 0)
            {
                log?.Invoke($"Abortado: {failures.Count} de {backups.Count} copias no coinciden con un perfil de la consola destino. No se ha restaurado ninguna partida.","error");
                foreach (var n in failures) state?.Report((n,backups.Count,n,"err"));
                err += failures.Count;
                return new OperationOutcome(ok, err, canceled);
            }
            log?.Invoke($"Perfil verificado en las {backups.Count} copias (via /{source}).","ok");
            for(var n=0;n<backups.Count;n++)
            {
                _cts.Token.ThrowIfCancellationRequested(); var b=backups[n]; state?.Report((n,backups.Count,n,"proc"));
                log?.Invoke($"\n{new string('─',58)}","sep");
                log?.Invoke($"[{n+1}/{backups.Count}]  {b.TitleId}  {b.TitleName}  ({b.SaveName})","info");
                try
                {
                    var uid=assignments[n];
                    var extra = b.SaveName.StartsWith("sdimg_", StringComparison.OrdinalIgnoreCase) ? "ps4=1" : null;
                    var res=await api.PostFileAsync(b.ImgPath,uid,extra,progress,_cts.Token); var fin=await api.ImportFinishAsync(uid,_cts.Token);
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
        finally{_cts.Dispose(); _cts=null;}
        return new OperationOutcome(ok,err,canceled);
    }

    public async Task<OperationOutcome> RunDeleteAsync(IReadOnlyList<TitleInfo> titles, ConsoleConfig console, IProgress<(int Index,int Total,string TitleId,string Uid,string State)>? state, Action<string,string>? log)
    {
        Cancel(); _cts=new CancellationTokenSource();
        var canceled = false;
        var ok = 0;
        var err = 0;
        try
        {
            using var api=new GarlicApi(console.Ip,console.Port); var total=titles.Count;
            for(var n=0;n<total;n++)
            {
                _cts.Token.ThrowIfCancellationRequested(); var t=titles[n]; state?.Report((n,total,t.TitleId,t.Uid,"proc")); log?.Invoke($"[{n+1}/{total}] {t.TitleId} {t.TitleName}","warn");
                try
                {
                    var saves=await api.SavesAsync(_cts.Token); var idxs=new List<int>();
                    for(var i=0;i<saves.Count;i++){var s=saves[i]; if(GarlicApi.GetString(s,"title_id")==t.TitleId && GarlicApi.Norm(GarlicApi.GetString(s,"uid"))==GarlicApi.Norm(t.Uid)) idxs.Add(i);}
                    foreach(var idx in idxs.OrderByDescending(x=>x)){_cts.Token.ThrowIfCancellationRequested(); await api.DeleteAsync(idx,_cts.Token); log?.Invoke($"  eliminado slot idx={idx}","ok");}
                    ok++; state?.Report((n,total,t.TitleId,t.Uid,"ok"));
                }catch(OperationCanceledException){throw;}catch(Exception ex){log?.Invoke($"  ERR: {ex.Message}","error");err++;state?.Report((n,total,t.TitleId,t.Uid,"err"));}
            }
            log?.Invoke($"Fin eliminacion:  {ok} OK  /  {err} errores  de {total} titulos.","info");
        }
        catch(OperationCanceledException){canceled=true;log?.Invoke("Cancelado.","warn");}
        catch(Exception ex){err++;log?.Invoke($"ERR: {ex.Message}","error");}
        finally{_cts.Dispose();_cts=null;}
        return new OperationOutcome(ok,err,canceled);
    }

    public void Cancel() => _cts?.Cancel();
    private static string FormatBytes(long n){double d=n; foreach(var u in new[]{"B","KB","MB","GB"}){if(d<1024)return $"{d:0} {u}";d/=1024;}return $"{d:0.0} TB";}
}
