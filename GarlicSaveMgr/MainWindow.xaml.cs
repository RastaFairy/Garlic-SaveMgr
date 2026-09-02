using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr;

public partial class MainWindow : Window
{
    private ConsoleConfig _config;
    private readonly OperationRunner _runner = new();
    private readonly GameMetadataService _metadata = new();
    private readonly ConsoleDiscoveryService _discovery = new();
    private readonly PayloadLauncherService _payloadLauncher = new();
    private readonly CoverCacheService _covers = new();
    private CancellationTokenSource? _discoveryCts;
    private readonly CancellationTokenSource _payloadCacheCts = new();
    private List<TitleRow> _titles = [];
    private List<BackupRow> _backups = [];
    private List<RestoreGameGroup> _restoreGroups = [];
    private ICollectionView? _titleView;
    private ICollectionView? _backupView;
    private bool _loadingProfile;
    private bool _simpleMode;

    public MainWindow()
    {
        InitializeComponent();
        AppVersionFooter.Text = AppInfo.Version;
        Title = $"Garlic SaveMgr v{AppInfo.Version}";
        _config = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(_config.Name)) _config.Name = "PS5";

        LogService.Message += OnLogMessage;
        ProfileService.Upsert(_config);
        LoadProfiles();
        UpdateConsoleLabel();
        LoadBackups();
        _simpleMode = SettingsService.LoadSimpleUi();
        ApplyViewMode();

        Loaded += async (_, _) =>
        {
            // El cacheado del payload nunca bloquea el arranque de la UI.
            LogService.Write($"Bienvenido a Garlic SaveMgr v{AppVersion} (C#).", "INFO");
            _ = CachePayloadInBackgroundAsync();
            await ConnectOrDiscoverAsync();
        };
    }

    private async Task ConnectOrDiscoverAsync()
    {
        try
        {
            // 1. Si existe una IP guardada, esa IP es nuestro perfil de consola.
            //    Comprobamos Garlic aunque actualmente no esté levantado para que
            //    el usuario vea el estado y pueda iniciarlo manualmente.
            if (IsValidConsoleAddress(_config.Ip, _config.Port))
            {
                await EnsureGarlicOrLaunchPayloadAsync();
                return;
            }

            // 2. En el primer arranque no conocemos la IP. La autodetección se basa
            //    en la presencia del servicio Garlic en el puerto configurado.
            await DiscoverConsoleAsync();
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Inicio cancelado.";
        }
        catch (Exception ex)
        {
            LogUi($"ERR conexión inicial: {ex.Message}", "error");
            MessageBox.Show(this, ex.Message, "Conexión inicial", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Comprueba el servicio Garlic y, si no está disponible, guía al usuario
    /// mediante la ruta de recuperación configurada para iniciar Garlic.
    /// </summary>
    private async Task EnsureGarlicOrLaunchPayloadAsync()
    {
        if (!IsValidConsoleAddress(_config.Ip, _config.Port)) return;

        using (var api = new GarlicApi(_config.Ip, _config.Port))
        {
            GarlicStatusLabel.Text = "Comprobando Garlic…";
            StatusLabel.Text = $"Comprobando Garlic en {_config.Ip}:{_config.Port}…";
            if (await api.PingAsync(timeout: TimeSpan.FromMilliseconds(450)))
            {
                GarlicStatusLabel.Text = "Garlic API ✓";
                StatusLabel.Text = "Garlic está ejecutándose. Continuando…";
                LogService.Write($"Garlic activo en {_config.Ip}:{_config.Port}.", "OK");
                _ = RefreshRunningPayloadVersionAsync();
                await ScanAsync();
                return;
            }
        }

        GarlicStatusLabel.Text = "Garlic no iniciado";
        StatusLabel.Text = "Garlic no está ejecutándose.";
        var ask = MessageBox.Show(
            this,
            $"No se detectó Garlic ejecutándose en {_config.Ip}.\n\n" +
            "La aplicación puede comprobar el catálogo de payloads, descargar y verificar el último Garlic SaveMgr y enviarlo al elfldr de la consola.\n\n" +
            "¿Quieres continuar?",
            "Garlic no detectado",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ask != MessageBoxResult.Yes)
        {
            StatusLabel.Text = "Garlic no iniciado.";
            return;
        }

        SetBusy(true, false);
        try
        {
            GarlicStatusLabel.Text = "Preparando Garlic…";
            StatusLabel.Text = "Comprobando última versión del payload…";
            var transfer = new Progress<(long Done, long Total)>(p =>
            {
                if (p.Total > 0)
                    StatusLabel.Text = $"Descargando payload: {FormatBytes(p.Done)} / {FormatBytes(p.Total)}";
            });

            var ok = await _payloadLauncher.EnsureGarlicRunningAsync(_config.Ip, LogUi, transfer, _discoveryCts?.Token ?? CancellationToken.None);
            if (!ok)
            {
                GarlicStatusLabel.Text = "Garlic no iniciado";
                StatusLabel.Text = "No se pudo iniciar Garlic.";
                return;
            }

            GarlicStatusLabel.Text = "Garlic API ✓";
            StatusLabel.Text = "Garlic iniciado correctamente. Continuando…";
            LogService.Write("Garlic iniciado correctamente tras cargar el payload.", "OK");
            _ = RefreshRunningPayloadVersionAsync();
            await ScanAsync();
        }
        finally
        {
            SetBusy(false, false);
        }
    }

    private const string AppVersion = AppInfo.Version;

    private void ViewModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _simpleMode = ViewModeToggle.IsChecked == true;
        SettingsService.SaveSimpleUi(_simpleMode);
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        if (!IsInitialized) return;

        ViewModeLabel.Text = _simpleMode ? "Simple" : "Detallada";
        ViewModeToggle.Content = _simpleMode ? "Simple" : "Detallada";
        ViewModeToggle.IsChecked = _simpleMode;
        foreach (var group in _restoreGroups) group.RefreshSelection();
        DetailedLogPanel.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        SimpleHintPanel.Visibility = Visibility.Collapsed;
        DetailedStatusCards.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        SimpleGamesScroll.Visibility = _simpleMode ? Visibility.Visible : Visibility.Collapsed;
        BackupGrid.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        SimpleRestoreScroll.Visibility = _simpleMode ? Visibility.Visible : Visibility.Collapsed;
        RestoreGrid.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        UidLabel.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        UidBox.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;

        // En Simple, la tabla ocupa todo el ancho disponible. En Detallada,
        // recuperamos el panel de actividad con la proporción original.
        if (MainContentGrid.ColumnDefinitions.Count >= 2)
        {
            MainContentGrid.ColumnDefinitions[0].Width = _simpleMode ? new GridLength(1, GridUnitType.Star) : new GridLength(2.2, GridUnitType.Star);
            MainContentGrid.ColumnDefinitions[1].Width = _simpleMode ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }

        // Simple hides technical columns; detailed restores every column.
        if (BackupGrid.Columns.Count >= 6)
        {
            BackupGrid.Columns[1].Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
            BackupGrid.Columns[4].Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        }
        if (RestoreGrid.Columns.Count >= 9)
        {
            RestoreGrid.Columns[1].Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
            RestoreGrid.Columns[4].Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
            RestoreGrid.Columns[7].Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async Task CachePayloadInBackgroundAsync()
    {
        try
        {
            await Dispatcher.InvokeAsync(() => PayloadCacheLabel.Text = "Payload cache: comprobando…");
            LogService.Write("Iniciando comprobación en segundo plano del último payload…", "INFO");
            var result = await _payloadLauncher.PrepareLatestPayloadCacheAsync(LogUi, _payloadCacheCts.Token);

            if (result.Cached)
            {
                var version = string.IsNullOrWhiteSpace(result.Version) ? "desconocida" : result.Version;
                LogService.Write($"Descarga/cache del payload finalizada: {version}.", "OK");
                if (!string.IsNullOrWhiteSpace(result.Sha256))
                    LogService.Write($"SHA-256 del payload cacheado: {result.Sha256}", "INFO");
                await Dispatcher.InvokeAsync(() => PayloadCacheLabel.Text = $"Payload cache: {version}");
                await ComparePayloadVersionsAsync(result.Version);
            }
            else
            {
                LogService.Write("La caché del payload no está disponible.", "WARN");
                await Dispatcher.InvokeAsync(() => PayloadCacheLabel.Text = "Payload cache: no disponible");
            }
        }
        catch (OperationCanceledException) when (_payloadCacheCts.IsCancellationRequested)
        {
            await Dispatcher.InvokeAsync(() => PayloadCacheLabel.Text = "Payload cache: cancelada");
        }
        catch (Exception ex)
        {
            LogService.Write($"ERR caché de payload: {ex.Message}", "ERROR");
            await Dispatcher.InvokeAsync(() => PayloadCacheLabel.Text = "Payload cache: error");
        }
    }

    private async Task RefreshRunningPayloadVersionAsync()
    {
        if (!IsValidConsoleAddress(_config.Ip, _config.Port)) return;
        try
        {
            var running = await _payloadLauncher.GetRunningVersionAsync(_config.Ip, _payloadCacheCts.Token);
            if (string.IsNullOrWhiteSpace(running))
            {
                await Dispatcher.InvokeAsync(() => GarlicVersionLabel.Text = "Payload en ejecución: versión no expuesta");
                LogService.Write("Garlic está activo, pero no se pudo identificar su versión en la interfaz HTML ni en /api/status.", "WARN");
                return;
            }

            await Dispatcher.InvokeAsync(() => GarlicVersionLabel.Text = $"Payload en ejecución: {running}");
            LogService.Write($"Payload en ejecución detectado: {running}.", "INFO");

            var cached = await _payloadLauncher.GetCachedPayloadVersionAsync();
            await ComparePayloadVersionsAsync(cached.Version, running);
        }
        catch (Exception ex)
        {
            LogService.Write($"ERR consultando versión del payload en ejecución: {ex.Message}", "WARN");
        }
    }

    private async Task ComparePayloadVersionsAsync(string? latestVersion)
    {
        await ComparePayloadVersionsAsync(latestVersion, null);
    }

    private async Task ComparePayloadVersionsAsync(string? latestVersion, string? runningVersion)
    {
        if (string.IsNullOrWhiteSpace(latestVersion)) return;
        if (string.IsNullOrWhiteSpace(runningVersion))
        {
            await Dispatcher.InvokeAsync(() => PayloadComparisonLabel.Text = $"Último payload: {latestVersion}");
            return;
        }

        var cmp = PayloadLauncherService.CompareVersions(runningVersion, latestVersion);
        string text;
        string level;
        if (cmp < 0)
        {
            text = $"Actualización disponible: {runningVersion} → {latestVersion}";
            level = "WARN";
        }
        else if (cmp == 0)
        {
            text = $"Payload actualizado: {latestVersion}";
            level = "OK";
        }
        else
        {
            text = $"Payload en ejecución ({runningVersion}) > catálogo ({latestVersion})";
            level = "INFO";
        }
        await Dispatcher.InvokeAsync(() => PayloadComparisonLabel.Text = text);
        LogService.Write(text, level);
    }

    private void LoadProfiles()
    {
        _loadingProfile = true;
        try
        {
            var profiles = ProfileService.Load();
            if (profiles.Count == 0)
            {
                ProfileService.Upsert(_config);
                profiles = [_config];
            }

            ProfileCombo.ItemsSource = profiles.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            ProfileCombo.SelectedItem = profiles.FirstOrDefault(p => string.Equals(p.Name, _config.Name, StringComparison.OrdinalIgnoreCase))?.Name
                                        ?? profiles[0].Name;
        }
        finally { _loadingProfile = false; }
    }

    private async void ProfileCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingProfile || ProfileCombo.SelectedItem is not string name) return;
        var profile = ProfileService.Find(name);
        if (profile is null) return;
        _config = profile;
        SettingsService.Save(_config);
        UpdateConsoleLabel();
        await ConnectOrDiscoverAsync();
    }

    private void UpdateConsoleLabel()
        => ConsoleLabel.Text = $"{_config.Name}  {(_config.Ip.Length == 0 ? "—" : _config.Ip)}";

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();
    private async void AutoDetect_Click(object sender, RoutedEventArgs e) => await DiscoverConsoleAsync();

    private async Task DiscoverConsoleAsync()
    {
        try
        {
            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
            _discoveryCts = new CancellationTokenSource();
            SetBusy(true, false);
            StatusLabel.Text = "Buscando consola en 192.168.x.x…";
            GarlicStatusLabel.Text = "Buscando…";
            var progress = new Progress<(string Ip, int Checked, int Total)>(p =>
                StatusLabel.Text = $"Buscando consola: {p.Ip} · {p.Checked}/{p.Total}");

            var result = await _discovery.DiscoverAsync(_config.Port, progress,
                message => LogService.Write(message, "INFO"), _discoveryCts.Token);

            if (result is null)
            {
                GarlicStatusLabel.Text = "No detectada";
                StatusLabel.Text = "No se encontró Garlic en 192.168.x.x.";
                NotifyCompletion("Detección finalizada", "No se encontró una consola con Garlic activo.");

                var manual = new SettingsWindow(_config, this);
                if (manual.ShowDialog() == true && IsValidConsoleAddress(_config.Ip, _config.Port))
                {
                    SettingsService.Save(_config);
                    ProfileService.Upsert(_config);
                    LoadProfiles();
                    UpdateConsoleLabel();
                    await EnsureGarlicOrLaunchPayloadAsync();
                }
                return;
            }

            _config.Ip = result.Ip;
            SettingsService.Save(_config);
            ProfileService.Upsert(_config);
            LoadProfiles();
            UpdateConsoleLabel();
            StatusLabel.Text = $"Consola detectada: {_config.Ip}";
            LogService.Write($"Consola detectada en {_config.Ip}:{_config.Port}.", "OK");

            // Comprobar Garlic; si no está activo, pedir al usuario que lo inicie
            // manualmente y reintentar hasta que esté disponible.
            await EnsureGarlicOrLaunchPayloadAsync();
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Detección cancelada.";
        }
        catch (Exception ex)
        {
            LogService.Write($"ERR detección: {ex.Message}", "ERROR");
            MessageBox.Show(this, ex.Message, "Detección de consola", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false, false); }
    }

    private async Task ScanAsync()
    {
        if (!EnsureIp()) return;
        try
        {
            SetBusy(true, false);
            StatusLabel.Text = $"Escaneando {_config.Ip}…";
            LogService.Write($"Escaneando {_config.Name} ({_config.Ip})…");
            using var api = new GarlicApi(_config.Ip, _config.Port);
            var raw = await api.ScanTitlesAsync(UidBox.Text.Trim());
            _titles = raw.Select(ToTitleRow).ToList();
            _titleView = CollectionViewSource.GetDefaultView(_titles);
            _titleView.Filter = TitleFilter;
            BackupGrid.ItemsSource = _titleView;
            SimpleGamesList.ItemsSource = _titleView;
            GarlicStatusLabel.Text = "Garlic API ✓";
            BackupCountLabel.Text = $"{_titles.Count} títulos";
            StatusLabel.Text = "Escaneo terminado.";
            LogService.Write($"Escaneo terminado: {_titles.Count} títulos.", "INFO");
            NotifyCompletion("Escaneo terminado", $"Se han detectado {_titles.Count} títulos en {_config.Name}.");
            _ = ResolveNamesAsync(_titles);
            _ = LoadCoversAsync(_titles);
        }
        catch (Exception ex)
        {
            LogService.Write($"ERR escaneo: {ex.Message}", "ERROR");
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false, false); }
    }

    private async Task ResolveNamesAsync(IList<TitleRow> rows)
    {
        try
        {
            var models = rows.Select(r => r.ToModel()).ToList();
            await _metadata.ResolveMissingAsync(models, updated => Dispatcher.Invoke(() =>
            {
                var row = rows.FirstOrDefault(r => string.Equals(r.TitleId, updated.TitleId, StringComparison.OrdinalIgnoreCase) &&
                                                   GarlicApi.Norm(r.Uid) == GarlicApi.Norm(updated.Uid));
                if (row != null)
                {
                    row.TitleName = updated.TitleName;
                    _titleView?.Refresh();
                }
            }));
        }
        catch (Exception ex) { LogUi($"ERR resolviendo nombres: {ex.Message}", "warn"); }
    }

    private void SimpleGameCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: TitleRow row })
            row.Selected = !row.Selected;
    }

    private void SimpleRestoreGameCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: RestoreGameGroup group }) return;
        group.ToggleSelection();
        _backupView?.Refresh();
    }

    private void SimpleBackupCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: BackupRow row })
            row.Selected = !row.Selected;
    }

    private async Task LoadCoversAsync(IList<TitleRow> rows)
    {
        try
        {
            var tuples = rows.Select(r => (r.TitleId, r.TitleName)).ToList();
            await _covers.WarmAsync(tuples, message => LogService.Write(message, "INFO"));
            foreach (var row in rows)
            {
                var path = await _covers.EnsureCoverAsync(row.TitleId, row.TitleName);
                if (path is null) continue;
                var image = CoverCacheService.LoadImage(path);
                if (image is null) continue;
                await Dispatcher.InvokeAsync(() =>
                {
                    row.CoverImage = image;
                    _metadata.SetCoverPath(row.TitleId, path);
                    foreach (var group in _restoreGroups.Where(g => string.Equals(g.TitleId, row.TitleId, StringComparison.OrdinalIgnoreCase)))
                        group.CoverImage ??= image;
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogUi($"ERR cargando carátulas: {ex.Message}", "warn"); }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureIp()) return;
        var selected = _titles.Where(x => x.Selected).Select(x => x.ToModel()).ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "Selecciona al menos un título.", "Garlic SaveMgr", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        try
        {
            SetBusy(true, false);
            BackupProgress.Value = 0; BackupProgress.Maximum = selected.Count;
            var progress = new Progress<(int Index, int Total, string TitleId, string Uid, string State)>(p =>
            {
                BackupProgress.Maximum = Math.Max(p.Total, 1);
                BackupProgress.Value = p.Index;
                MarkTitle(p.TitleId, p.Uid, p.State);
            });
            var transferProgress = new Progress<(long Done, long Total)>(p =>
            {
                if (p.Total > 0) StatusLabel.Text = $"Transferencia: {FormatBytes(p.Done)} / {FormatBytes(p.Total)}";
            });
            var outcome = await _runner.RunBackupAsync(selected, _config, progress, transferProgress, LogUi);
            BackupProgress.Value = BackupProgress.Maximum;
            LoadBackups();
            if (outcome.Canceled)
            {
                StatusLabel.Text = "Backup cancelado.";
                NotifyCompletion("Backup cancelado", $"La copia se canceló. Completados: {outcome.Succeeded}; errores: {outcome.Failed}.");
            }
            else if (outcome.Failed > 0)
            {
                StatusLabel.Text = $"Backup finalizado con {outcome.Failed} error(es).";
                NotifyCompletion("Backup con errores", $"Completados: {outcome.Succeeded}; errores: {outcome.Failed}.");
            }
            else
            {
                StatusLabel.Text = "Backup terminado.";
                NotifyCompletion("Backup terminado", $"Proceso de copia finalizado para {_config.Name}.");
            }
        }
        finally { SetBusy(false, false); }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureIp()) return;
        var selected = _backups.Where(x => x.Selected).Select(x => x.Model).ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "Selecciona al menos una copia.", "Garlic SaveMgr", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        try
        {
            SetBusy(true, true); RestoreProgress.Value = 0; RestoreProgress.Maximum = selected.Count;
            var progress = new Progress<(int Index, int Total, int Row, string State)>(p =>
            {
                RestoreProgress.Maximum = Math.Max(p.Total, 1);
                RestoreProgress.Value = p.Index;
                MarkBackup(p.Row, p.State);
            });
            var transferProgress = new Progress<(long Done, long Total)>(p =>
            {
                if (p.Total > 0) StatusLabel.Text = $"Transferencia: {FormatBytes(p.Done)} / {FormatBytes(p.Total)}";
            });
            var outcome = await _runner.RunRestoreAsync(selected, _config, progress, transferProgress, LogUi);
            RestoreProgress.Value = RestoreProgress.Maximum;
            if (outcome.Canceled)
            {
                StatusLabel.Text = "Restauración cancelada.";
                NotifyCompletion("Restauración cancelada", $"Completadas: {outcome.Succeeded}; errores: {outcome.Failed}.");
            }
            else if (outcome.Failed > 0)
            {
                StatusLabel.Text = $"Restauración finalizada con {outcome.Failed} error(es).";
                NotifyCompletion("Restauración con errores", $"Completadas: {outcome.Succeeded}; errores: {outcome.Failed}.");
            }
            else
            {
                StatusLabel.Text = "Restauración terminada.";
                NotifyCompletion("Restauración terminada", $"Proceso de restauración finalizado para {_config.Name}.");
            }
        }
        finally { SetBusy(false, true); }
    }

    private async void DeleteConsole_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureIp()) return;
        var selected = _titles.Where(x => x.Selected).Select(x => x.ToModel()).ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "Selecciona al menos un título.", "Garlic SaveMgr", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var items = selected.Select(t => new ConfirmationWindow.ConfirmationItem(t.TitleId, string.IsNullOrWhiteSpace(t.TitleName) ? "Nombre no disponible" : t.TitleName)).ToList();
        var summary = $"Se eliminarán {selected.Count} título(s) de la consola {_config.Name} ({_config.Ip}).\n\nEsta operación NO tiene deshacer.";
        if (!ConfirmationWindow.Show(this, "Confirmar eliminación", summary, items, "Eliminar")) return;
        SetBusy(true, false);
        try
        {
            var progress = new Progress<(int Index, int Total, string TitleId, string Uid, string State)>(p => { BackupProgress.Maximum = Math.Max(p.Total, 1); BackupProgress.Value = p.Index; MarkTitle(p.TitleId, p.Uid, p.State); });
            var outcome = await _runner.RunDeleteAsync(selected, _config, progress, LogUi);
            if (outcome.Canceled)
            {
                StatusLabel.Text = "Eliminación cancelada.";
                NotifyCompletion("Eliminación cancelada", $"Completadas: {outcome.Succeeded}; errores: {outcome.Failed}.");
            }
            else if (outcome.Failed > 0)
            {
                StatusLabel.Text = $"Eliminación finalizada con {outcome.Failed} error(es).";
                NotifyCompletion("Eliminación con errores", $"Completadas: {outcome.Succeeded}; errores: {outcome.Failed}.");
            }
            else
            {
                StatusLabel.Text = "Eliminación terminada.";
                NotifyCompletion("Eliminación terminada", $"Proceso de eliminación finalizado para {_config.Name}.");
            }
        }
        finally { SetBusy(false, false); }
    }

    private void DeleteLocal_Click(object sender, RoutedEventArgs e)
    {
        var selected = _backups.Where(x => x.Selected).ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "Selecciona al menos una copia.", "Garlic SaveMgr", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var items = selected.Select(b => new ConfirmationWindow.ConfirmationItem(b.TitleId, $"{b.SaveName} · {b.Date}")).ToList();
        var summary = $"Se eliminarán {selected.Count} copia(s) del PC.\n\nEsta operación NO tiene deshacer.";
        if (!ConfirmationWindow.Show(this, "Eliminar copias locales", summary, items, "Eliminar")) return;
        foreach (var b in selected)
        {
            try { BackupService.DeleteLocal(b.Model); }
            catch (Exception ex) { LogUi($"ERR eliminando copia: {ex.Message}", "error"); }
        }
        LoadBackups();
    }

    private void ExportZip_Click(object sender, RoutedEventArgs e)
    {
        var selected = _backups.Where(x => x.Selected).Select(x => x.Model).ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "Selecciona al menos una copia.", "Exportar ZIP", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dlg = new SaveFileDialog
        {
            Filter = "Archivo ZIP (*.zip)|*.zip",
            FileName = $"GarlicSaveMgr_{_config.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var path = BackupService.ExportZip(selected, dlg.FileName);
            StatusLabel.Text = $"ZIP exportado: {Path.GetFileName(path)}";
            LogUi($"Exportadas {selected.Count} copias a {path}", "ok");
            NotifyCompletion("Exportación terminada", $"{selected.Count} copias guardadas en {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            LogUi($"ERR exportando ZIP: {ex.Message}", "error");
            MessageBox.Show(this, ex.Message, "Exportar ZIP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadBackups()
    {
        _backups = BackupService.LoadLocalBackups().Select(b => new BackupRow(b)).ToList();
        _backupView = CollectionViewSource.GetDefaultView(_backups);
        _backupView.Filter = BackupFilter;
        RestoreGrid.ItemsSource = _backupView;

        _restoreGroups = _backups
            .GroupBy(x => x.TitleId ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(g => new RestoreGameGroup(
                g.Key,
                g.Select(x => x.TitleName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Nombre no disponible",
                g.ToList()))
            .OrderBy(x => x.TitleName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        SimpleRestoreList.ItemsSource = _restoreGroups;
        RestoreCountLabel.Text = $"{_restoreGroups.Count} juegos · {_backups.Count} copias";
        _ = LoadBackupCoversAsync(_backups);
    }

    private async Task LoadBackupCoversAsync(IList<BackupRow> rows)
    {
        try
        {
            foreach (var row in rows)
            {
                var path = await _covers.EnsureCoverAsync(row.TitleId, row.TitleName);
                if (path is null) continue;
                var image = CoverCacheService.LoadImage(path);
                if (image is null) continue;
                await Dispatcher.InvokeAsync(() =>
                {
                    row.CoverImage = image;
                    _metadata.SetCoverPath(row.TitleId, path);
                    foreach (var group in _restoreGroups.Where(g => string.Equals(g.TitleId, row.TitleId, StringComparison.OrdinalIgnoreCase)))
                        group.CoverImage ??= image;
                });
            }
        }
        catch { }
    }

    private bool TitleFilter(object obj)
    {
        if (obj is not TitleRow row) return false;
        var q = BackupSearchBox?.Text?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(q) || Contains(row.TitleId, q) || Contains(row.TitleName, q) || Contains(row.Uid, q);
    }

    private bool BackupFilter(object obj)
    {
        if (obj is not BackupRow row) return false;
        var q = RestoreSearchBox?.Text?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(q) || Contains(row.TitleId, q) || Contains(row.TitleName, q) || Contains(row.SaveName, q) || Contains(row.OwnerDisplay, q) || Contains(row.Date, q) || Contains(row.SizeDisplay, q);
    }

    private static bool Contains(string? value, string query) => (value ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase);
    private void BackupSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _titleView?.Refresh();
    private void RestoreSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _backupView?.Refresh();

    private void MarkTitle(string titleId, string uid, string state)
    {
        var row = _titles.FirstOrDefault(x => x.TitleId == titleId && GarlicApi.Norm(x.Uid) == GarlicApi.Norm(uid));
        if (row == null) return;
        row.State = state;
        row.Foreground = state switch { "ok" => Brushes.DarkGreen, "err" => Brushes.DarkRed, "proc" => Brushes.DarkOrange, _ => Brushes.Gray };
    }

    private void MarkBackup(int row, string state)
    {
        if (row < 0 || row >= _backups.Count) return;
        _backups[row].State = state;
        _backups[row].Foreground = state switch { "ok" => Brushes.DarkGreen, "err" => Brushes.DarkRed, "proc" => Brushes.DarkOrange, _ => Brushes.Gray };
    }

    private void SelectAllBackup_Click(object sender, RoutedEventArgs e) { foreach (var x in _titles) x.Selected = true; _titleView?.Refresh(); }
    private void SelectNoneBackup_Click(object sender, RoutedEventArgs e) { foreach (var x in _titles) x.Selected = false; _titleView?.Refresh(); }
    private void SelectAllRestore_Click(object sender, RoutedEventArgs e) { foreach (var x in _backups) x.Selected = true; foreach (var g in _restoreGroups) g.RefreshSelection(); _backupView?.Refresh(); }
    private void SelectNoneRestore_Click(object sender, RoutedEventArgs e) { foreach (var x in _backups) x.Selected = false; foreach (var g in _restoreGroups) g.RefreshSelection(); _backupView?.Refresh(); }
    private void ReloadRestore_Click(object sender, RoutedEventArgs e) => LoadBackups();
    private void Cancel_Click(object sender, RoutedEventArgs e) { _discoveryCts?.Cancel(); _runner.Cancel(); }
    private void OpenBackups_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.EncDirectory);
    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogsDirectory);

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_config, this);
        if (dlg.ShowDialog() == true)
        {
            SettingsService.Save(_config);
            ProfileService.Upsert(_config);
            LoadProfiles();
            UpdateConsoleLabel();
            await ScanAsync();
        }
    }

    private bool EnsureIp()
    {
        if (IsValidConsoleAddress(_config.Ip, _config.Port)) return true;
        _config.Ip = "";
        UpdateConsoleLabel();
        MessageBox.Show(this, "No hay una consola válida conectada. Pulsa 'Detectar consola'.", "Garlic SaveMgr", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static bool IsValidConsoleAddress(string? ip, int port)
        => port is >= 1 and <= 65535 && IPAddress.TryParse(ip, out var address) &&
           address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
           !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.None);

    private void SetBusy(bool busy, bool restore)
    {
        AutoDetectButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
        BackupButton.IsEnabled = !busy;
        DeleteConsoleButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        RestoreButton.IsEnabled = !busy;
        DeleteLocalButton.IsEnabled = !busy;
        CancelRestoreButton.IsEnabled = busy;
        ExportZipButton.IsEnabled = !busy;
        ProfileCombo.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        MainTabs.IsEnabled = !busy;
    }

    private void LogUi(string message, string level) => LogService.Write(message, level.ToUpperInvariant());

    private void OnLogMessage(string message, string level)
    {
        Dispatcher.Invoke(() =>
        {
            var p = new Paragraph(new Run($"{DateTime.Now:HH:mm:ss} {message}"));
            p.Foreground = level switch { "error" or "ERROR" => Brushes.DarkRed, "warn" or "WARN" => Brushes.DarkOrange, "ok" or "OK" => Brushes.DarkGreen, _ => Brushes.Black };
            LogBox.Document.Blocks.Add(p);
            LogBox.ScrollToEnd();
        });
    }

    private void NotifyCompletion(string title, string message)
    {
        System.Media.SystemSounds.Asterisk.Play();
        StatusLabel.Text = message;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        FlashWindow();
    }

    private void FlashWindow()
    {
        var oldTitle = Title;
        Title = $"✓ {oldTitle}";
        _ = Task.Delay(2500).ContinueWith(_ => Dispatcher.Invoke(() => Title = oldTitle));
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private static TitleRow ToTitleRow(JsonElement e)
    {
        var row = new TitleRow { TitleId = GarlicApi.GetString(e, "title_id"), Uid = GarlicApi.GetString(e, "uid"), TitleName = GarlicApi.GetString(e, "title_name") };
        row.SlotCount = int.TryParse(GarlicApi.GetString(e, "slot_count"), out var n) ? n : 0;
        var slots = new List<SlotInfo>();
        if (e.TryGetProperty("slots", out var sv) && sv.ValueKind == JsonValueKind.Array)
            foreach (var s in sv.EnumerateArray()) slots.Add(new SlotInfo { Name = GarlicApi.GetString(s, "name"), Backup = GarlicApi.GetBool(s, "backup") });
        row.Slots = slots;
        if (row.SlotCount == 0) row.SlotCount = slots.Count;
        return row;
    }

    private static string FormatBytes(long n)
    {
        double d = n;
        foreach (var u in new[] { "B", "KB", "MB", "GB" }) { if (d < 1024) return $"{d:0.0} {u}"; d /= 1024; }
        return $"{d:0.0} TB";
    }

    protected override void OnClosed(EventArgs e)
    {
        LogService.Message -= OnLogMessage;
        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _payloadCacheCts.Cancel();
        _payloadCacheCts.Dispose();
        _covers.Dispose();
        _runner.Cancel();
        base.OnClosed(e);
    }
}

public abstract class StatusRowBase : INotifyPropertyChanged
{
    private Brush _foreground = Brushes.Gray;
    private string _state = "";
    public Brush Foreground { get => _foreground; set { if (Equals(_foreground, value)) return; _foreground = value; OnPropertyChanged(); } }
    public string State { get => _state; set { if (_state == value) return; _state = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TitleRow : StatusRowBase
{
    private bool _selected = true;
    private string _titleName = "";
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; OnPropertyChanged(); } }
    public string TitleId { get; set; } = "";
    public string Uid { get; set; } = "";
    public string TitleName { get => _titleName; set { if (_titleName == value) return; _titleName = value; OnPropertyChanged(); } }
    private ImageSource? _coverImage;
    public ImageSource? CoverImage { get => _coverImage; set { if (Equals(_coverImage, value)) return; _coverImage = value; OnPropertyChanged(); } }
    public int SlotCount { get; set; }
    public List<SlotInfo> Slots { get; set; } = [];
    public TitleInfo ToModel() => new() { TitleId = TitleId, Uid = Uid, TitleName = TitleName, SlotCount = SlotCount, Slots = Slots };
}

public sealed record RestoreGroupKey(string TitleId, string TitleName);

public sealed class RestoreGameGroup : INotifyPropertyChanged
{
    private bool _selected;
    private ImageSource? _coverImage;
    public string TitleId { get; }
    public string TitleName { get; }
    public List<BackupRow> Backups { get; }
    public int BackupCount => Backups.Count;
    public string BackupCountText => BackupCount == 1 ? "1 copia" : $"{BackupCount} copias";
    public bool Selected { get => _selected; private set { if (_selected == value) return; _selected = value; OnPropertyChanged(); } }
    public ImageSource? CoverImage { get => _coverImage; set { if (Equals(_coverImage, value)) return; _coverImage = value; OnPropertyChanged(); } }

    public RestoreGameGroup(string titleId, string titleName, List<BackupRow> backups)
    {
        TitleId = titleId ?? "";
        TitleName = string.IsNullOrWhiteSpace(titleName) ? "Nombre no disponible" : titleName;
        Backups = backups;
        RefreshSelection();
    }

    public void ToggleSelection()
    {
        var target = !Selected;
        foreach (var backup in Backups) backup.Selected = target;
        RefreshSelection();
    }

    public void RefreshSelection()
    {
        Selected = Backups.Count > 0 && Backups.All(x => x.Selected);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BackupRow : StatusRowBase
{
    private bool _selected = true;
    public BackupEntry Model { get; }
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; OnPropertyChanged(); } }
    public string TitleId => Model.TitleId;
    public string TitleName => Model.TitleName;
    public string SaveName => Model.SaveName;
    public string OwnerDisplay => string.Join(", ", Model.Owner.Select(x => $"{x.Key}={x.Value}"));
    public string Date => Model.Date;
    public long Size => Model.Size;
    public string SizeDisplay => FormatBytes(Size);
    public string Sha256 => string.IsNullOrWhiteSpace(Model.Sha256) ? "No verificado" : Model.Sha256;
    private ImageSource? _coverImage;
    public ImageSource? CoverImage { get => _coverImage; set { if (Equals(_coverImage, value)) return; _coverImage = value; OnPropertyChanged(); } }
    public BackupRow(BackupEntry model) => Model = model;

    private static string FormatBytes(long n)
    {
        double d = n;
        foreach (var u in new[] { "B", "KB", "MB", "GB" }) { if (d < 1024) return $"{d:0.0} {u}"; d /= 1024; }
        return $"{d:0.0} TB";
    }
}
