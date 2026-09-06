using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr.Modules.Trash;

public sealed class TrashModule : IExecutableModule
{
    private readonly IModuleHostContext _host;
    private readonly TrashView _view;
    private List<TrashEntry> _trashEntries = [];
    private List<TrashGameGroup> _trashGroups = [];
    private ICollectionView? _trashGridView;
    private List<PcTrashEntry> _pcTrashEntries = [];
    private List<PcTrashGameGroup> _pcTrashGroups = [];
    private ICollectionView? _pcTrashGridView;
    private bool _refreshing;
    private bool _refreshingPc;
    private readonly System.Windows.Threading.DispatcherTimer _debounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private FileSystemWatcher? _watcher;

    public TrashModule(IModuleHostContext host)
    {
        _host = host;
        _view = new TrashView(this);
        _debounceTimer.Tick += async (_, _) => { _debounceTimer.Stop(); if (_view.IsLoaded && IsPcScope()) await RefreshPcTrashAsync(true); };
        ConfigureWatcher();
        RefreshSummary();
    }

    public string Id => "trash";
    public string Header => "PAPELERA";
    public UserControl View => _view;

    public void OnHostStateChanged(string state)
    {
        switch (state)
        {
            case ModuleState.Ps5TrashChanged:
                // Solo una operación que pueda alterar /data/backup_save_enc puede
                // refrescar la Papelera PS5. Guardar copias en PC nunca debe entrar
                // en este flujo ni provocar una consulta FTP a la consola.
                _ = RefreshPs5TrashAsync(true);
                break;
            case ModuleState.PcTrashChanged:
                // Mandato 6.8.6.12: tras mover copias a pc_trash la Papelera PC se
                // actualiza explícitamente, sea cual sea la subpestaña visible.
                _ = RefreshPcTrashDataAsync(true);
                break;
            case ModuleState.ThemeChanged:
            case ModuleState.ConnectionsChanged:
                RefreshSummary();
                break;
            case ModuleState.SelectionChanged:
                if (IsPcScope()) RefreshSummary();
                break;
        }
    }

    private bool IsPcScope() => _view.TrashScopeTabs.SelectedIndex == 1;

    /// <summary>
    /// Fuente única de visibilidad: la pestaña PS5 muestra siempre la grid PS5 y la
    /// pestaña PC la grid PC. Sin esto, tras visitar la pestaña PC la grid PC seguía
    /// visible dentro de la pestaña PS5 y las acciones (vaciar/restaurar/eliminar)
    /// se aplicaban a la papelera contraria a la que el usuario estaba viendo.
    /// </summary>
    internal void UpdateScopeVisibility()
    {
        var pc = IsPcScope();
        _view.TrashGrid.Visibility = pc ? Visibility.Collapsed : Visibility.Visible;
        _view.PcTrashGrid.Visibility = pc ? Visibility.Visible : Visibility.Collapsed;
    }

    internal async Task RefreshTrashAsync(bool silent = false)
    {
        // Entrada en PAPELERA: refrescar únicamente el ámbito visible.
        UpdateScopeVisibility();
        if (IsPcScope())
        {
            await RefreshPcTrashAsync(silent);
            return;
        }
        await RefreshPs5TrashAsync(silent);
    }

    internal async Task RefreshPs5TrashAsync(bool silent = false)
    {
        if (!_view.IsLoaded) return;
        UpdateScopeVisibility();
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (IsPcScope()) return;
            if (!_host.EnsureIp()) return;
            _host.SetBusy(true);
            try
            {
                if (!silent) _host.Log("Actualizando Papelera desde la PS5...", "INFO");
                var previousSelection = _trashGroups.Where(x => x.Selected).Select(x => x.TitleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var loaded = await Task.Run(async () =>
                {
                    var entries = (await _host.Runner.LoadInternalTrashAsync(_host.Config, _host.Log, CancellationToken.None)).ToList();
                    if (entries.Count > 0)
                        await _host.Runner.VerifyInternalTrashAsync(entries, _host.Config, _host.Log, CancellationToken.None);
                    return entries;
                });
                _trashEntries = loaded;
                _trashGroups = BuildTrashGroups(_trashEntries);
                foreach (var group in _trashGroups)
                    if (previousSelection.Contains(group.TitleId)) group.Selected = true;
                _trashGridView = CollectionViewSource.GetDefaultView(_trashGroups);
                _view.TrashGrid.ItemsSource = _trashGridView;
                UpdateScopeVisibility();
                RefreshSummary();
                _ = LoadTrashCoversAsync(_trashGroups);
                if (!silent) _host.SetStatus(_trashEntries.Count == 0 ? "Papelera de PS5 vacía." : "Papelera de PS5 actualizada y verificada.");
            }
            catch (OperationCanceledException) { _host.Log("Actualización de Papelera PS5 cancelada.", "WARN"); }
            catch (Exception ex) { _host.Log($"ERR actualizando Papelera: {ex.Message}", "ERROR"); DialogService.ShowError(_host.HostWindow, ex.Message, "Papelera"); }
            finally { _host.SetBusy(false); }
        }
        finally { _refreshing = false; }
    }

    internal async Task RefreshPcTrashAsync(bool silent = false)
    {
        UpdateScopeVisibility();
        await RefreshPcTrashDataAsync(silent);
    }

    /// <summary>Recarga los datos de la Papelera PC sin tocar la visibilidad de las grids.</summary>
    private async Task RefreshPcTrashDataAsync(bool silent = false)
    {
        if (!_view.IsLoaded || _refreshingPc) return;
        _refreshingPc = true;
        try
        {
            var previousSelection = _pcTrashGroups.Where(x => x.Selected).Select(x => x.TitleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // PcTrashService.Verify hashea cada IMG del disco: fuera del hilo de interfaz.
            var (entries, groups) = await Task.Run(() =>
            {
                var loaded = PcTrashService.Load();
                PcTrashService.Verify(loaded);
                return (loaded, BuildPcTrashGroups(loaded));
            });
            _pcTrashEntries = entries;
            _pcTrashGroups = groups;
            foreach (var group in _pcTrashGroups)
                if (previousSelection.Contains(group.TitleId)) group.Selected = true;
            _pcTrashGridView = CollectionViewSource.GetDefaultView(_pcTrashGroups);
            _view.PcTrashGrid.ItemsSource = _pcTrashGridView;
            RefreshSummary();
            _ = LoadPcTrashCoversAsync(_pcTrashGroups);
            if (!silent) _host.SetStatus("Papelera del PC actualizada y verificada.");
        }
        catch (Exception ex)
        {
            _host.Log($"ERR actualizando Papelera del PC: {ex.Message}", "ERROR");
            if (!silent) DialogService.ShowError(_host.HostWindow, ex.Message, "Papelera del PC");
        }
        finally { _refreshingPc = false; }
    }

    internal async Task RestoreAsync()
    {
        if (IsPcScope())
        {
            var selected = _pcTrashGroups.Where(x => x.Selected).SelectMany(x => x.Entries).ToList();
            if (selected.Count == 0) { DialogService.ShowInfo(_host.HostWindow, "Selecciona al menos un juego de la Papelera del PC.", "Papelera PC"); return; }
            _host.SetBusy(true);
            try
            {
                PcTrashService.Verify(selected);
                var invalid = selected.Where(x => x.IntegrityStatus != BackupIntegrityStatus.Valid).ToList();
                if (invalid.Count > 0) { DialogService.ShowWarning(_host.HostWindow, $"No se puede restaurar ninguna copia no válida. Hay {invalid.Count} elemento(s) sin SHA-256 válido.", "Restauración bloqueada"); return; }
                var conflicts = PcTrashService.FindRestoreConflicts(selected);
                if (conflicts.Count > 0)
                {
                    var lines = string.Join("\n", conflicts.Select(x => $"• {x.TitleId} / {x.SaveName}"));
                    DialogService.ShowWarning(_host.HostWindow, "La restauración está bloqueada porque ya existen copias activas con esos nombres:\n\n" + lines + "\n\nRetira primero la copia activa si quieres recuperar la de la Papelera.", "Conflicto de restauración");
                    return;
                }
                if (!ConfirmationWindow.Show(_host.HostWindow, "Restaurar Papelera del PC", $"Se restaurarán {selected.Count} copia(s) al almacenamiento activo del PC.\n\nLa integridad ya ha sido verificada.", selected.Select(x => new ConfirmationWindow.ConfirmationItem(x.SaveName, $"{x.TitleId} · {x.TitleName}")).ToList(), "Restaurar")) return;
                var restored = PcTrashService.Restore(selected);
                _host.ReloadBackups();
                await RefreshPcTrashAsync(true);
                _host.SetStatus($"{restored} copia(s) restaurada(s) desde la Papelera PC.");
                _host.Log($"Papelera PC restaurada: {restored}/{selected.Count} copia(s).", "OK");
            }
            catch (Exception ex) { _host.Log($"ERR restaurando Papelera PC: {ex.Message}", "ERROR"); DialogService.ShowError(_host.HostWindow, ex.Message, "Papelera PC"); }
            finally { _host.SetBusy(false); }
            return;
        }

        var ps5Selected = _trashGroups.Where(x => x.Selected).SelectMany(x => x.Entries).ToList();
        if (ps5Selected.Count == 0) { DialogService.ShowInfo(_host.HostWindow, "Selecciona al menos un elemento de la Papelera.", "Papelera"); return; }
        try
        {
            _host.SetBusy(true);
            if (!DialogService.Confirm(_host.HostWindow, $"Se restaurarán {ps5Selected.Count} savedata(s) desde la Papelera.\n\nTras una restauración correcta, cada copia se retira de la Papelera.", "Restaurar desde Papelera")) return;
            await _host.Runner.VerifyInternalTrashAsync(ps5Selected, _host.Config, _host.Log, CancellationToken.None);
            if (ps5Selected.Any(x => x.IntegrityStatus != BackupIntegrityStatus.Valid)) { DialogService.ShowError(_host.HostWindow, "Una o más copias de la Papelera no superaron SHA-256.", "Papelera"); return; }
            var conflicts = await _host.Runner.FindInternalTrashConflictsAsync(ps5Selected, _host.Config);
            if (conflicts.Count > 0)
            {
                var lines = string.Join("\n", conflicts.Select(x => $"• {x.TitleId} / {x.SaveName}"));
                if (!DialogService.Confirm(_host.HostWindow, $"Se han encontrado {conflicts.Count} savedata(s) ya presentes en la consola destino:\n\n{lines}\n\n¿Quieres sobrescribirlos con el contenido de la Papelera?", "Sobrescritura desde Papelera", DialogSeverity.Warning)) return;
            }
            var outcome = await _host.Runner.RestoreInternalTrashAsync(ps5Selected, _host.Config, _host.Log);
            await _host.ScanAsync();
            await RefreshTrashAsync();
            _host.NotifyCompletion("Restauración desde Papelera", $"Completadas: {outcome.Succeeded}; errores: {outcome.Failed}.");
        }
        catch (Exception ex) { _host.Log($"ERR restaurando Papelera: {ex.Message}", "ERROR"); DialogService.ShowError(_host.HostWindow, ex.Message, "Papelera"); }
        finally { _host.SetBusy(false); }
    }

    internal async Task DeleteAsync(bool empty)
    {
        if (IsPcScope())
        {
            var selected = empty ? _pcTrashEntries.ToList() : _pcTrashGroups.Where(x => x.Selected).SelectMany(x => x.Entries).ToList();
            if (selected.Count == 0) { _host.SetStatus("La Papelera del PC ya está vacía."); return; }
            if (!ConfirmationWindow.Show(_host.HostWindow, empty ? "Vaciar Papelera PC" : "Eliminar definitivamente", $"Se eliminarán DEFINITIVAMENTE {selected.Count} elemento(s) de la Papelera del PC.\n\nEsta operación no tiene deshacer.", selected.Select(x => new ConfirmationWindow.ConfirmationItem(x.SaveName, $"{x.TitleId} · {x.TitleName}")).ToList(), "Eliminar definitivamente")) return;
            _host.SetBusy(true);
            try { var deleted = await Task.Run(() => PcTrashService.DeletePermanently(selected)); await RefreshPcTrashAsync(true); _host.SetStatus($"Papelera PC: eliminados {deleted} elemento(s)."); _host.Log($"Papelera PC: eliminados definitivamente {deleted} elemento(s).", "OK"); }
            catch (Exception ex) { _host.Log($"ERR eliminando Papelera PC: {ex.Message}", "ERROR"); DialogService.ShowError(_host.HostWindow, ex.Message, "Papelera PC"); }
            finally { _host.SetBusy(false); }
            return;
        }

        var selectedPs5 = empty ? _trashEntries.ToList() : _trashGroups.Where(x => x.Selected).SelectMany(x => x.Entries).ToList();
        if (selectedPs5.Count == 0) { _host.SetStatus("La Papelera ya está vacía."); return; }
        if (!DialogService.Confirm(_host.HostWindow, $"Se eliminarán DEFINITIVAMENTE {selectedPs5.Count} respaldo(s) de la Papelera PS5.\n\nEsta operación no tiene deshacer.", "Eliminar definitivamente", DialogSeverity.Warning)) return;
        try { _host.SetBusy(true); await _host.Runner.DeleteInternalTrashAsync(selectedPs5, _host.Config, _host.Log); await RefreshTrashAsync(); }
        catch (Exception ex) { _host.Log($"ERR eliminando Papelera: {ex.Message}", "ERROR"); DialogService.ShowError(_host.HostWindow, ex.Message, "Papelera"); }
        finally { _host.SetBusy(false); }
    }

    private void RefreshSummary()
    {
        if (!_view.IsLoaded) return;
        if (IsPcScope())
        {
            var bytes = _pcTrashEntries.Sum(x => x.Size); var valid = _pcTrashEntries.Count(x => x.IntegrityStatus == BackupIntegrityStatus.Valid);
            _view.TrashSummaryLabel.Text = $"PC · {_pcTrashGroups.Count} juegos · {_pcTrashEntries.Count} slots · {FormatBytes(bytes)} · integridad automática: {valid}/{_pcTrashEntries.Count}";
        }
        else
        {
            var bytes = _trashEntries.Sum(x => x.Size); var valid = _trashEntries.Count(x => x.IntegrityStatus == BackupIntegrityStatus.Valid);
            _view.TrashSummaryLabel.Text = $"PS5 · {_trashGroups.Count} juegos · {_trashEntries.Count} slots · {FormatBytes(bytes)} · integridad automática: {valid}/{_trashEntries.Count}";
        }
    }

    private List<TrashGameGroup> BuildTrashGroups(IEnumerable<TrashEntry> entries) => entries.GroupBy(x => x.TitleId ?? "", StringComparer.OrdinalIgnoreCase).Select(g => new TrashGameGroup(g.Key, g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TitleName))?.TitleName ?? "Nombre no disponible", g.ToList())).OrderBy(x => x.TitleName, StringComparer.CurrentCultureIgnoreCase).ToList();
    private List<PcTrashGameGroup> BuildPcTrashGroups(IEnumerable<PcTrashEntry> entries) => entries.GroupBy(x => x.TitleId ?? "", StringComparer.OrdinalIgnoreCase).Select(g => new PcTrashGameGroup(g.Key, g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TitleName))?.TitleName ?? "Nombre no disponible", g.ToList())).OrderBy(x => x.TitleName, StringComparer.CurrentCultureIgnoreCase).ToList();

    private async Task LoadTrashCoversAsync(IEnumerable<TrashGameGroup> groups)
    {
        var tasks = groups.Select(async group =>
        {
            try
            {
                var path = await _host.Covers.EnsureCoverAsync(group.TitleId, group.TitleName);
                var image = await _host.Covers.LoadImageAsync(path);
                if (image is not null) await _host.Dispatcher.InvokeAsync(() => group.CoverImage = image);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _host.Log($"Carátula Papelera PS5 {group.TitleId}: {ex.Message}", "WARN"); }
        });

        await Task.WhenAll(tasks);
    }

    private async Task LoadPcTrashCoversAsync(IEnumerable<PcTrashGameGroup> groups)
    {
        var tasks = groups.Select(async group =>
        {
            try
            {
                var path = await _host.Covers.EnsureCoverAsync(group.TitleId, group.TitleName);
                var image = await _host.Covers.LoadImageAsync(path);
                if (image is not null) await _host.Dispatcher.InvokeAsync(() => group.CoverImage = image);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _host.Log($"Carátula Papelera PC {group.TitleId}: {ex.Message}", "WARN"); }
        });

        await Task.WhenAll(tasks);
    }

    private void ConfigureWatcher()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.PcTrashDirectory);
            _watcher = new FileSystemWatcher(AppPaths.PcTrashDirectory) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            _watcher.Changed += OnWatcher; _watcher.Created += OnWatcher; _watcher.Deleted += OnWatcher; _watcher.Renamed += (_, e) => { if (Relevant(e.FullPath) || Relevant(e.OldFullPath)) ScheduleWatcher(); }; _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { _host.Log($"WARN no se pudo vigilar automáticamente la Papelera PC: {ex.Message}", "WARN"); }
    }
    private void OnWatcher(object sender, FileSystemEventArgs e) { if (Relevant(e.FullPath)) ScheduleWatcher(); }
    private static bool Relevant(string path) { var name = Path.GetFileName(path); if (string.IsNullOrWhiteSpace(name)) return true; if (name.EndsWith("~", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return false; return name.EndsWith(".img", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase); }
    private void ScheduleWatcher() { try { _host.Dispatcher.BeginInvoke(new Action(() => { if (_view.IsLoaded && IsPcScope()) { _debounceTimer.Stop(); _debounceTimer.Start(); } })); } catch (InvalidOperationException) { } }

    private static string FormatBytes(long n) { double d=n; foreach (var u in new[]{"B","KB","MB","GB"}) { if(d<1024)return $"{d:0.0} {u}"; d/=1024; } return $"{d:0.0} TB"; }

    public void Dispose() { _watcher?.Dispose(); _debounceTimer.Stop(); }
}

public partial class TrashView : UserControl
{
    private readonly TrashModule _module;
    public TrashView(TrashModule module) { InitializeComponent(); _module = module; Loaded += async (_, _) => await _module.RefreshTrashAsync(true); }
    private void TrashScopeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (e.Source != TrashScopeTabs) return; _module.UpdateScopeVisibility(); _ = _module.RefreshTrashAsync(true); }
    private async void RestoreTrash_Click(object sender, RoutedEventArgs e) => await _module.RestoreAsync();
    private async void DeleteTrash_Click(object sender, RoutedEventArgs e) => await _module.DeleteAsync(false);
    private async void EmptyTrash_Click(object sender, RoutedEventArgs e) => await _module.DeleteAsync(true);
}

public sealed class TrashGameGroup : INotifyPropertyChanged
{
    private bool _selected; private System.Windows.Media.ImageSource? _coverImage;
    public string TitleId { get; } public string TitleName { get; } public List<TrashEntry> Entries { get; }
    public bool Selected { get=>_selected; set { if(_selected==value)return; _selected=value; foreach(var x in Entries)x.Selected=value; OnPropertyChanged(); RefreshSummary(); } }
    public System.Windows.Media.ImageSource? CoverImage { get=>_coverImage; set{if(Equals(_coverImage,value))return;_coverImage=value;OnPropertyChanged();}}
    public int SlotCount => Entries.Count; public string SlotsDisplay=>string.Join("  •  ",Entries.Select(x=>x.SaveName).Distinct(StringComparer.OrdinalIgnoreCase)); public string SlotCountDisplay=>SlotCount==1?"1 slot":$"{SlotCount} slots"; public string OwnerDisplay=>string.Join(", ",Entries.Select(x=>x.UserId).Where(x=>!string.IsNullOrWhiteSpace(x)&&x!="—").Distinct(StringComparer.OrdinalIgnoreCase)); public string SizeDisplay=>FormatBytes(Entries.Sum(x=>x.Size)); public string Date=>Entries.Select(x=>x.Date).OrderByDescending(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault()??"—"; public string IntegrityDisplay=>BuildIntegrityDisplay();
    private string _state="SIN COMPROBAR"; private System.Windows.Media.Brush _foreground=ThemeManager.GetStatusBrush("SIN COMPROBAR"); public string State{get=>_state;private set{if(_state==value)return;_state=value;OnPropertyChanged();}} public System.Windows.Media.Brush Foreground{get=>_foreground;private set{if(Equals(_foreground,value))return;_foreground=value;OnPropertyChanged();}}
    public TrashGameGroup(string titleId,string titleName,List<TrashEntry> entries){TitleId=titleId;TitleName=titleName;Entries=entries;RefreshSummary();}
    public void RefreshSummary(){State=Entries.All(x=>x.IntegrityStatus==BackupIntegrityStatus.Valid)?"OK":Entries.Any(x=>x.IntegrityStatus is BackupIntegrityStatus.Mismatch or BackupIntegrityStatus.Error)?"CORRUPTA":Entries.Any(x=>x.IntegrityStatus==BackupIntegrityStatus.MissingHash)?"SIN HASH":"SIN COMPROBAR";Foreground=ThemeManager.GetStatusBrush(State);OnPropertyChanged(nameof(IntegrityDisplay));}
    private string BuildIntegrityDisplay()=>Entries.Count==0?"—":$"{Entries.Count(x=>x.IntegrityStatus==BackupIntegrityStatus.Valid)}/{Entries.Count} OK";
    private static string FormatBytes(long n){double d=n;foreach(var u in new[]{"B","KB","MB","GB"}){if(d<1024)return $"{d:0.#} {u}";d/=1024;}return $"{d:0.0} TB";}
    public event PropertyChangedEventHandler? PropertyChanged; private void OnPropertyChanged([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
}

public sealed class PcTrashGameGroup : INotifyPropertyChanged
{
    private bool _selected; private System.Windows.Media.ImageSource? _coverImage;
    public string TitleId { get; } public string TitleName { get; } public List<PcTrashEntry> Entries { get; }
    public bool Selected{get=>_selected;set{if(_selected==value)return;_selected=value;foreach(var x in Entries)x.Selected=value;OnPropertyChanged();}}
    public System.Windows.Media.ImageSource? CoverImage{get=>_coverImage;set{if(Equals(_coverImage,value))return;_coverImage=value;OnPropertyChanged();}}
    public int SlotCount=>Entries.Count; public string SlotsDisplay=>string.Join("  •  ",Entries.Select(x=>x.SaveName).Distinct(StringComparer.OrdinalIgnoreCase)); public string SlotCountDisplay=>SlotCount==1?"1 slot":$"{SlotCount} slots"; public string SourceConsole=>string.Join(", ",Entries.Select(x=>x.SourceConsole).Where(x=>!string.IsNullOrWhiteSpace(x)&&x!="—").Distinct(StringComparer.OrdinalIgnoreCase)); public string SizeDisplay=>FormatBytes(Entries.Sum(x=>x.Size)); public string Date=>Entries.Select(x=>x.Date).OrderByDescending(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault()??"—"; public string IntegrityDisplay=>Entries.Count==0?"—":$"{Entries.Count(x=>x.IntegrityStatus==BackupIntegrityStatus.Valid)}/{Entries.Count} OK"; public string State=>Entries.All(x=>x.IntegrityStatus==BackupIntegrityStatus.Valid)?"OK":Entries.Any(x=>x.IntegrityStatus is BackupIntegrityStatus.Mismatch or BackupIntegrityStatus.Error)?"CORRUPTA":Entries.Any(x=>x.IntegrityStatus==BackupIntegrityStatus.MissingHash)?"SIN HASH":"SIN COMPROBAR"; public System.Windows.Media.Brush Foreground=>ThemeManager.GetStatusBrush(State);
    public PcTrashGameGroup(string titleId,string titleName,List<PcTrashEntry> entries){TitleId=titleId;TitleName=titleName;Entries=entries;}
    private static string FormatBytes(long n){double d=n;foreach(var u in new[]{"B","KB","MB","GB"}){if(d<1024)return $"{d:0.#} {u}";d/=1024;}return $"{d:0.0} TB";}
    public event PropertyChangedEventHandler? PropertyChanged; private void OnPropertyChanged([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
}
