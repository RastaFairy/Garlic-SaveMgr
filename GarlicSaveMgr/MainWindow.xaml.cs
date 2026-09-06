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

public partial class MainWindow : Window, IModuleHostContext
{
    private ConsoleConfig _config;
    private readonly OperationRunner _runner = new();
    private readonly GameMetadataService _metadata = new();
    private readonly ConsoleDiscoveryService _discovery = new();
    private readonly PayloadLauncherService _payloadLauncher = new();
    private readonly CoverCacheService _covers = new();
    private readonly ModuleManager _modules = new();
    private CancellationTokenSource? _discoveryCts;
    private readonly CancellationTokenSource _payloadCacheCts = new();
    private readonly InitialConnectionGate _initialConnection = new();
    private CancellationTokenSource _coverLoadCts = new();
    private readonly SemaphoreSlim _coverWorkGate = new(4, 4);
    private List<TitleRow> _titles = [];
    private List<ConsoleConnection> _connections = [];
    private bool _loadingConnectionSelection;
    private List<BackupRow> _backups = [];
    private List<RestoreGameGroup> _restoreGroups = [];
    private List<SnapshotRecord> _snapshots = [];
    private List<BackupRow> _activeRestoreRows = [];
    private ICollectionView? _titleView;
    private ICollectionView? _restoreGroupView;
    private bool _simpleMode;

    public MainWindow()
    {
        InitializeComponent();
        AppVersionFooter.Text = AppInfo.Version;
        Title = $"Garlic SaveMgr v{AppInfo.Version}";
        _config = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(_config.Name)) _config.Name = "PS5";

        LogService.Message += OnLogMessage;
        ThemeManager.ThemeChanged += OnThemeChanged;
        if (IsValidConsoleAddress(_config.Ip, _config.Port))
            UpsertConnection(_config.Ip, _config.Port, select: true);
        LoadProfiles();
        UpdateConsoleLabel();
        LoadBackups();
        _simpleMode = SettingsService.LoadSimpleUi();
        _modules.Load(MainTabs, this);
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
        // Mientras este intento de conexión/detección esté vivo, el aviso de consola
        // no válida no puede aparecer: la búsqueda (hasta 1.275 direcciones) aún no
        // ha entregado su resultado y el arranque no puede advertir antes de tiempo.
        using (_initialConnection.Begin())
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
                DialogService.ShowError(this, ex.Message, "Conexión inicial");
            }
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
                MarkActiveConnectionRuntimeState(garlic: true, elfldr: null);
                GarlicStatusLabel.Text = "Garlic API ✓";
                StatusLabel.Text = "Garlic está ejecutándose. Continuando…";
                LogService.Write($"Garlic activo en {_config.Ip}:{_config.Port}.", "OK");
                _ = RefreshRunningPayloadVersionAsync();
                await RefreshActiveConnectionUsersAsync();
                await ScanAsync();
                return;
            }
        }

        GarlicStatusLabel.Text = "Garlic no iniciado";
        StatusLabel.Text = "Garlic no está ejecutándose.";
        var ask = DialogService.Confirm(
            this,
            $"No se detectó Garlic ejecutándose en {_config.Ip}.\n\n" +
            "La aplicación puede comprobar el catálogo de payloads, descargar y verificar el último Garlic SaveMgr y enviarlo al elfldr de la consola.\n\n" +
            "¿Quieres continuar?",
            "Garlic no detectado");

        if (!ask)
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

            MarkActiveConnectionRuntimeState(garlic: true, elfldr: true);
            GarlicStatusLabel.Text = "Garlic API ✓";
            StatusLabel.Text = "Garlic iniciado correctamente. Continuando…";
            LogService.Write("Garlic iniciado correctamente tras cargar el payload.", "OK");
            _ = RefreshRunningPayloadVersionAsync();
            await RefreshActiveConnectionUsersAsync();
            await ScanAsync();
        }
        finally
        {
            SetBusy(false, false);
        }
    }

    private static string AppVersion => AppInfo.Version;

    private void ViewModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _simpleMode = ViewModeToggle.IsChecked == true;
        SettingsService.SaveSimpleUi(_simpleMode);
        ApplyViewMode();
        if (!_simpleMode && _modules.MissingRecommendedModules.Count > 0)
        {
            var missing = string.Join("\n", _modules.MissingRecommendedModules.Select(ModuleManager.GetRecommendedHeader).Select(x => $"• {x}"));
            DialogService.ShowInfo(this,
                $"Algunas funciones detalladas no están cargadas porque faltan sus módulos en la carpeta Modules:\n\n{missing}\n\nColoca los ficheros recuperables en Modules/ y reinicia Garlic SaveMgr para cargarlos.",
                "Módulos detallados no disponibles");
        }
    }

    private void ShowMissingModuleWarning(string capability, string moduleId, string details)
    {
        var header = ModuleManager.GetRecommendedHeader(moduleId);
        var message = $"La función '{capability}' está limitada porque no está cargado el módulo {header}.\n\n" +
                      details + "\n\n" +
                      $"Coloca el módulo '{moduleId}' en la carpeta Modules y reinicia Garlic SaveMgr para recuperar esta capacidad.";
        DialogService.ShowWarning(this, message, $"Módulo {header} no disponible");
        LogUi($"Función limitada: {capability}; falta el módulo {moduleId}.", "warn");
    }

    private bool IsModuleAvailable(string id) => _modules.IsAvailable(id);
    private bool IsModuleSelected(string id) => _modules.IsSelected(MainTabs, id);
    private void SetModuleVisibility(string id, Visibility visibility) => _modules.SetVisibility(id, visibility);

    private void ApplyViewMode()
    {
        if (!IsInitialized) return;

        ViewModeLabel.Text = _simpleMode ? "Simple" : "Detallada";
        ViewModeToggle.Content = _simpleMode ? "Simple" : "Detallada";
        ViewModeToggle.IsChecked = _simpleMode;
        foreach (var group in _restoreGroups) group.RefreshSelection();

        // Nunca dejamos seleccionado un tab que la nueva vista va a ocultar.
        // WPF puede conservar el contenido anterior visualmente hasta que el usuario
        // vuelve a pulsar COPIAR/RESTAURAR; seleccionamos un tab visible antes de
        // cambiar Visibility para que el layout se recalcule en una sola pasada.
        if (_simpleMode && (IsModuleSelected("security") ||
                            IsModuleSelected("trash") ||
                            IsModuleSelected("salud") ||
                            IsModuleSelected("updater")))
        {
            var backupTab = MainTabs.Items.OfType<TabItem>().FirstOrDefault(x => string.Equals(x.Header?.ToString(), "COPIAR", StringComparison.OrdinalIgnoreCase));
            if (backupTab != null) MainTabs.SelectedItem = backupTab;
        }

        DetailedLogPanel.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        SetModuleVisibility("salud", _simpleMode ? Visibility.Collapsed : Visibility.Visible);
        SimpleHintPanel.Visibility = Visibility.Collapsed;
        DetailedStatusCards.Visibility = _simpleMode ? Visibility.Collapsed : Visibility.Visible;
        SetModuleVisibility("security", _simpleMode ? Visibility.Collapsed : Visibility.Visible);
        SetModuleVisibility("trash", _simpleMode ? Visibility.Collapsed : Visibility.Visible);
        SetModuleVisibility("updater", _simpleMode ? Visibility.Collapsed : Visibility.Visible);
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

        UpdateModuleAvailabilityStatus();
    }

    private void UpdateModuleAvailabilityStatus()
    {
        var missing = _modules.MissingRecommendedModules;
        if (missing.Count == 0) return;
        var names = string.Join(", ", missing.Select(ModuleManager.GetRecommendedHeader));
        StatusLabel.Text = _simpleMode
            ? $"Modo operativo básico. Funciones opcionales no disponibles: {names}."
            : $"Módulos no cargados: {names}. Las operaciones básicas siguen disponibles.";
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
        _loadingConnectionSelection = true;
        try
        {
            var profiles = ProfileService.Load();
            foreach (var profile in profiles.Where(p => IsValidConsoleAddress(p.Ip, p.Port)))
                UpsertConnection(profile.Ip, profile.Port, select: false);

            if (_connections.Count == 0 && IsValidConsoleAddress(_config.Ip, _config.Port))
                UpsertConnection(_config.Ip, _config.Port, select: true);

            ProfileCombo.ItemsSource = null;
            ProfileCombo.ItemsSource = _connections;
            if (_connections.Count > 0)
            {
                var active = _connections.FirstOrDefault(c => string.Equals(c.Ip, _config.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == _config.Port) ?? _connections[0];
                ProfileCombo.SelectedItem = active;
            }
            _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
        }
        finally { _loadingConnectionSelection = false; }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var draft = new ConsoleConfig
        {
            Name = _config.Name,
            Ip = _config.Ip,
            Port = _config.Port
        };

        var dialog = new SettingsWindow(draft, this);
        if (dialog.ShowDialog() != true) return;

        _config = draft;
        SettingsService.Save(_config);
        if (IsValidConsoleAddress(_config.Ip, _config.Port))
            UpsertConnection(_config.Ip, _config.Port, select: true);
        else
        {
            ProfileCombo.SelectedItem = null;
            UpdateConsoleLabel();
            _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
        }

        LogUi($"Ajustes guardados: {_config.Name} {_config.Ip}:{_config.Port}.", "ok");
        _ = ConnectOrDiscoverAsync();
    }

    private async void ProfileCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingConnectionSelection || ProfileCombo.SelectedItem is not ConsoleConnection connection) return;
        if (!IsValidConsoleAddress(connection.Ip, connection.Port)) return;
        _config = connection.ToConfig();
        SettingsService.Save(_config);
        UpdateConsoleLabel();
        _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
        await ConnectOrDiscoverAsync();
    }

    private void UpsertConnection(string ip, int port, bool select)
    {
        if (!IsValidConsoleAddress(ip, port)) return;
        var existing = _connections.FirstOrDefault(c => string.Equals(c.Ip, ip, StringComparison.OrdinalIgnoreCase) && c.Port == port);
        if (existing is null)
        {
            existing = new ConsoleConnection { Ip = ip.Trim(), Port = port };
            _connections.Add(existing);
            _connections = _connections.OrderBy(c => c.Ip, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Port).ToList();
        }
        if (select)
        {
            _config = existing.ToConfig();
            if (IsInitialized && ProfileCombo != null)
            {
                _loadingConnectionSelection = true;
                ProfileCombo.ItemsSource = _connections;
                ProfileCombo.SelectedItem = existing;
                _loadingConnectionSelection = false;
            }
        }
        _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
    }

    private async Task RefreshActiveConnectionUsersAsync()
    {
        var connection = _connections.FirstOrDefault(c => string.Equals(c.Ip, _config.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == _config.Port);
        if (connection is null) return;
        try
        {
            using var api = new GarlicApi(connection.Ip, connection.Port);
            var userIds = await QueryUserIdsWithRetryAsync(api, connection.Ip, _payloadCacheCts.Token);
            connection.UserIds.Clear();
            connection.UserIds.AddRange(userIds);
            connection.UserIds.Sort(StringComparer.OrdinalIgnoreCase);
            connection.GarlicApiAvailable = true;
            connection.LastSeenLocal = DateTime.Now;
            _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
            LoadBackups();
        }
        catch (Exception ex) { LogUi($"ERR consultando user_id de {_config.Ip}: {ex.Message}", "warn"); }
    }

    private async Task<List<string>> QueryUserIdsWithRetryAsync(GarlicApi api, string ip, CancellationToken ct)
    {
        Exception? last = null;
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1.5) };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            if (delays[attempt] > TimeSpan.Zero) await Task.Delay(delays[attempt], ct).ConfigureAwait(false);
            try
            {
                var ids = await api.UserIdsAsync(ct).ConfigureAwait(false);
                if (ids.Count > 0 || attempt == delays.Length - 1) return ids;
            }
            catch (Exception ex) { last = ex; LogService.Write($"Intento {attempt + 1}/{delays.Length} consultando user_id en {ip}: {ex.Message}", "WARN"); }
        }
        if (last is not null) throw last;
        return [];
    }

    private void MarkActiveConnectionRuntimeState(bool? garlic, bool? elfldr)
    {
        var connection = _connections.FirstOrDefault(c => string.Equals(c.Ip, _config.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == _config.Port);
        if (connection is null) return;
        if (garlic.HasValue) connection.GarlicApiAvailable = garlic;
        if (elfldr.HasValue) connection.ElfLdrAvailable = elfldr;
        connection.LastSeenLocal = DateTime.Now;
        _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
    }

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || e.Source != MainTabs) return;
        _modules.NotifyStateChanged(ModuleState.SelectionChanged);
        ApplyModuleLayout();
    }

    private void ApplyModuleLayout()
    {
        if (!IsInitialized) return;
        var saludSelected = _modules.IsSelected(MainTabs, "salud");
        MainTabsHost.SetValue(Grid.ColumnSpanProperty, saludSelected ? 2 : 1);
        DetailedLogPanel.Visibility = saludSelected ? Visibility.Collapsed : (_simpleMode ? Visibility.Collapsed : Visibility.Visible);
    }

    private async Task OnConsoleDiscoveredAsync(ConsoleDiscoveryResult result)
    {
        ConsoleConnection? connection = null;
        await Dispatcher.InvokeAsync(() =>
        {
            var existing = _connections.FirstOrDefault(c => string.Equals(c.Ip, result.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == result.Port);
            if (existing is null)
            {
                existing = new ConsoleConnection { Ip = result.Ip, Port = result.Port };
                _connections.Add(existing);
                _connections = _connections.OrderBy(c => c.Ip, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Port).ToList();
            }
            connection = existing;
            connection.GarlicApiAvailable = result.GarlicApiAvailable;
            connection.ElfLdrAvailable = result.ElfLdrAvailable;
            connection.LastSeenLocal = DateTime.Now;
            ProfileCombo.ItemsSource = null;
            ProfileCombo.ItemsSource = _connections;
            _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
        });
        if (connection is null || !result.GarlicApiAvailable) return;
        try
        {
            using var api = new GarlicApi(connection.Ip, connection.Port);
            var userIds = await QueryUserIdsWithRetryAsync(api, connection.Ip, _discoveryCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                connection.UserIds.Clear();
                connection.UserIds.AddRange(userIds);
                connection.UserIds.Sort(StringComparer.OrdinalIgnoreCase);
                _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
            });
            LogService.Write($"{connection.Ip}: {userIds.Count} user_id detectados durante la detección.", "INFO");
        }
        catch (Exception ex) { LogService.Write($"No se pudieron consultar user_id en {connection.Ip}: {ex.Message}", "WARN"); }
    }

    private void UpdateConsoleLabel()
        => ConsoleLabel.Text = $"{_config.Name}  {(_config.Ip.Length == 0 ? "—" : _config.Ip)}";

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();
    private async void AutoDetect_Click(object sender, RoutedEventArgs e) => await DiscoverConsoleAsync();

    private async Task DiscoverConsoleAsync()
    {
        try
        {
            _discoveryCts?.Cancel(); _discoveryCts?.Dispose(); _discoveryCts = new CancellationTokenSource();
            SetBusy(true, false);
            StatusLabel.Text = "Buscando consolas en 192.168.x.x…";
            GarlicStatusLabel.Text = "Buscando…";
            var progress = new Progress<(string Ip, int Checked, int Total)>(p => StatusLabel.Text = $"Buscando consolas: {p.Ip} · {p.Checked}/{p.Total}");
            var results = await _discovery.DiscoverAllAsync(_config.Port, progress, message => LogService.Write(message, "INFO"), OnConsoleDiscoveredAsync, _discoveryCts.Token);
            if (results.Count == 0)
            {
                GarlicStatusLabel.Text = "No detectada";
                StatusLabel.Text = "No se encontraron consolas con Garlic/elfldr.";
                NotifyCompletion("Detección finalizada", "No se encontraron consolas compatibles.");
                return;
            }
            _loadingConnectionSelection = true;
            _connections = results.Select(result => _connections.FirstOrDefault(c => string.Equals(c.Ip, result.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == result.Port) ?? new ConsoleConnection { Ip = result.Ip, Port = result.Port }).OrderBy(c => c.Ip, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Port).ToList();
            ProfileCombo.ItemsSource = _connections;
            var active = _connections.FirstOrDefault(c => string.Equals(c.Ip, _config.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == _config.Port) ?? _connections[0];
            ProfileCombo.SelectedItem = active;
            _config = active.ToConfig();
            _loadingConnectionSelection = false;
            ProfileService.Save(_connections.Select(c => c.ToConfig()));
            SettingsService.Save(_config);
            UpdateConsoleLabel();
            StatusLabel.Text = results.Count == 1 ? $"1 consola detectada: {_config.Ip}" : $"{results.Count} consolas detectadas.";
            var activeResult = results.FirstOrDefault(r => string.Equals(r.Ip, _config.Ip, StringComparison.OrdinalIgnoreCase) && r.Port == _config.Port);
            GarlicStatusLabel.Text = activeResult?.GarlicApiAvailable == true ? "Garlic API ✓" : "elfldr disponible";
            foreach (var result in results) LogService.Write($"Consola detectada: {result.Ip}:{result.Port}.", "OK");
            _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
            await EnsureGarlicOrLaunchPayloadAsync();
        }
        catch (OperationCanceledException) { StatusLabel.Text = "Detección cancelada."; }
        catch (Exception ex)
        {
            LogService.Write($"ERR detección: {ex.Message}", "ERROR");
            DialogService.ShowError(this, ex.Message, "Detección de consola");
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
            var activeConnection = _connections.FirstOrDefault(c => string.Equals(c.Ip, _config.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == _config.Port);
            if (activeConnection is not null)
            {
                activeConnection.TitleCount = _titles.Count;
                activeConnection.GarlicApiAvailable = true;
                activeConnection.LastSeenLocal = DateTime.Now;
            }
            _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
            _modules.NotifyStateChanged(ModuleState.BackupsChanged);
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
            StartTitleCoverLoading(_titles);
        }
        catch (Exception ex)
        {
            LogService.Write($"ERR escaneo: {ex.Message}", "ERROR");
            DialogService.ShowError(this, ex.Message, "Error");
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
                    // TitleName implements INotifyPropertyChanged, so the bound
                    // cells/cards update immediately. Do not refresh the live
                    // CollectionView from inside the metadata callback: a DataGrid
                    // may be in AddNew/EditItem transaction at that exact moment,
                    // which makes ICollectionView.Refresh() illegal (WPF throws:
                    // "Refresh is not allowed during an AddNew or EditItem transaction").
                    row.TitleName = updated.TitleName;
                }
            }));

            // Do not force ICollectionView.Refresh() here. TitleName raises
            // PropertyChanged itself, which is enough to update the visible cell.
            // A refresh at this point can race with WPF's internal AddNew/EditItem
            // transaction and produce the exact exception seen in the test logs.
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
        _restoreGroupView?.Refresh();
    }

    private void SimpleBackupCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: BackupRow row })
            row.Selected = !row.Selected;
    }

    private void StartTitleCoverLoading(IList<TitleRow> rows)
    {
        _coverLoadCts.Cancel();
        _coverLoadCts.Dispose();
        _coverLoadCts = new CancellationTokenSource();
        var token = _coverLoadCts.Token;
        foreach (var row in rows)
            _ = LoadCoverIntoTitleAsync(row, token);
    }

    private async Task LoadCoverIntoTitleAsync(TitleRow row, CancellationToken ct)
    {
        await _coverWorkGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = _metadata.GetCachedCoverPath(row.TitleId)
                ?? await _covers.EnsureCoverAsync(row.TitleId, row.TitleName, message => LogService.Write(message, "INFO"), ct);
            if (path is null || ct.IsCancellationRequested) return;

            var image = await _covers.LoadImageAsync(path, ct);
            if (image is null || ct.IsCancellationRequested) return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                row.CoverImage = image;
                _metadata.SetCoverPath(row.TitleId, path);
                foreach (var group in _restoreGroups.Where(g => string.Equals(g.TitleId, row.TitleId, StringComparison.OrdinalIgnoreCase)))
                    group.CoverImage ??= image;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogService.Write($"Carátula {row.TitleId}: {ex.Message}", "WARN"); }
        finally { _coverWorkGate.Release(); }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureIp()) return;
        var selected = _titles.Where(x => x.Selected).Select(x => x.ToModel()).ToList();
        if (selected.Count == 0) { DialogService.ShowInfo(this, "Selecciona al menos un título.", "Garlic SaveMgr"); return; }

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
            var outcome = await _runner.RunBackupAsync(selected, _config, progress, transferProgress, LogUi, SmartBackupCheckBox?.IsChecked != false);
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

    private async Task<List<(BackupEntry Backup, string Message)>> VerifyBackupsForRestoreAsync(IReadOnlyList<BackupRow> rows)
    {
        var failures = new List<(BackupEntry Backup, string Message)>();
        foreach (var row in rows)
        {
            var result = await Task.Run(() => BackupService.VerifyIntegrity(row.Model));
            row.SetIntegrity(result);
            switch (result.Status)
            {
                case BackupIntegrityStatus.Valid:
                    LogUi($"SHA-256 OK: {row.TitleId} / {row.SaveName}", "ok");
                    break;
                case BackupIntegrityStatus.MissingHash:
                    failures.Add((row.Model, "Sin SHA-256 de referencia"));
                    LogUi($"SHA-256 AUSENTE: {row.TitleId} / {row.SaveName}", "error");
                    break;
                case BackupIntegrityStatus.Mismatch:
                    failures.Add((row.Model, "SHA-256 no coincide"));
                    LogUi($"SHA-256 NO COINCIDE: {row.TitleId} / {row.SaveName}", "error");
                    break;
                default:
                    failures.Add((row.Model, string.IsNullOrWhiteSpace(result.ErrorMessage) ? "No se pudo verificar" : result.ErrorMessage));
                    LogUi($"SHA-256 ERROR: {row.TitleId} / {row.SaveName}: {result.ErrorMessage}", "error");
                    break;
            }
        }
        return failures;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureIp()) return;
        var selectedGroups = _restoreGroups.Where(x => x.Selected).ToList();
        var selectedRows = selectedGroups.SelectMany(g => g.Backups).Where(x => x.Selected).ToList();
        var selected = selectedRows.Select(x => x.Model).ToList();
        if (selected.Count == 0) { DialogService.ShowInfo(this, "Selecciona al menos un juego.", "Garlic SaveMgr"); return; }

        _activeRestoreRows = selectedRows;
        try
        {
            SetBusy(true, true);

            // 6.8.3: la integridad de la copia local se verifica antes de cualquier
            // comprobacion/transferencia de restauracion. Una copia sin SHA-256
            // de referencia o con hash distinto no puede restaurarse.
            if (!IsModuleAvailable("security"))
            {
                ShowMissingModuleWarning("verificación detallada de integridad", "security",
                    "La restauración básica sigue utilizando la verificación SHA-256 del núcleo, pero no estará disponible la pestaña SEGURIDAD ni sus herramientas de auditoría.");
            }
            LogUi("Verificando integridad SHA-256 de las copias seleccionadas...", "info");
            IReadOnlyList<ModuleIntegrityFailure> integrityFailures;
            if (_modules.TryGetCapability<ISecurityModuleCapability>("security", out var securityModule))
            {
                integrityFailures = await securityModule!.VerifyForRestoreAsync(selectedRows);
            }
            else
            {
                integrityFailures = (await VerifyBackupsForRestoreAsync(selectedRows))
                    .Select(x => new ModuleIntegrityFailure(x.Backup, x.Message))
                    .ToList();
            }
            if (integrityFailures.Count > 0)
            {
                var items = integrityFailures
                    .Select(x => new ConfirmationWindow.ConfirmationItem(
                        $"{x.Backup.TitleId} · {x.Backup.SaveName}", x.Message))
                    .ToList();
                var summary = "La restauración se ha bloqueado porque una o más copias no pueden verificarse como íntegras.\n\n" +
                              "Revisa la pestaña SEGURIDAD antes de volver a intentarlo.";
                ConfirmationWindow.Show(this, "Integridad de copias", summary, items, "Cerrar");
                LogUi($"Restauración bloqueada: {integrityFailures.Count} copia(s) no superaron la verificación SHA-256.", "error");
                StatusLabel.Text = "Restauración bloqueada por integridad.";
                return;
            }

            LogUi("Integridad SHA-256 verificada correctamente en todas las copias seleccionadas.", "ok");

            // Preflight REAL en consola: comprobamos antes de subir nada si alguno
            // de los savedata ya existe en el perfil destino. El endpoint
            // /api/import_encrypted devuelve "exists" despues de aceptar la
            // importacion, por lo que consultarlo tras el POST no evita la
            // sobrescritura.
            LogUi("Comprobando si ya existe alguna copia en la consola destino...", "info");
            var conflicts = await _runner.FindRestoreConflictsAsync(selected, _config);
            if (conflicts.Count > 0)
            {
                var items = conflicts
                    .Select(c => new ConfirmationWindow.ConfirmationItem(
                        $"{c.TitleId} · {c.SaveName}", c.TitleName))
                    .ToList();
                var summary = conflicts.Count == 1
                    ? "Ya existe una copia de este savedata en la consola destino. Si continúas, la copia actual será sobrescrita."
                    : $"Se han encontrado {conflicts.Count} copias que ya existen en la consola destino. Si continúas, esas copias serán sobrescritas.";

                if (!ConfirmationWindow.Show(this, "Copias ya existentes", summary, items, "Sobrescribir y continuar"))
                {
                    LogUi("Restauración cancelada: se detectaron copias ya existentes y no se autorizó la sobrescritura.", "warn");
                    return;
                }

                LogUi($"Sobrescritura autorizada para {conflicts.Count} copia(s) existente(s).", "warn");
            }
            else
            {
                LogUi("No se detectaron copias existentes para los savedata seleccionados.", "ok");
            }

            RestoreProgress.Value = 0; RestoreProgress.Maximum = selected.Count;
            var progress = new Progress<(int Index, int Total, int Row, string State)>(p =>
            {
                RestoreProgress.Maximum = Math.Max(p.Total, 1);
                RestoreProgress.Value = Math.Min(p.Index + 1, p.Total);
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

            // 6.8.3: después de cualquier operación de restauración volvemos a
            // consultar los savedata reales de la PS5. Esto evita que COPIAR se
            // quede mostrando el estado anterior (por ejemplo, 24 títulos
            // después de restaurar un título que vuelve a ser visible).
            // También cubre restauraciones parciales o canceladas, porque el
            // estado final de la consola debe venir de un escaneo real.
            LogUi("Actualizando automaticamente los savedata despues de la restauracion...", "info");
            await ScanAsync();
        }
        finally
        {
            _activeRestoreRows = [];
            SetBusy(false, true);
        }
    }

    private async void DeleteConsole_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureIp()) return;
        var selected = _titles.Where(x => x.Selected).Select(x => x.ToModel()).ToList();
        if (selected.Count == 0)
        {
            DialogService.ShowInfo(this, "Selecciona al menos un título.", "Garlic SaveMgr");
            return;
        }

        SetBusy(true, false);
        try
        {
            LogUi("Comprobando qué savedata se eliminarán y qué slots tienen copia local...", "info");
            IReadOnlyList<ConsoleDeletePreview> previews;
            try
            {
                previews = await _runner.PreviewDeleteAsync(selected, _config);
            }
            catch (Exception ex)
            {
                LogUi($"Eliminación bloqueada: no se pudo verificar el contenido actual de la consola: {ex.Message}", "error");
                    DialogService.ShowWarning(
                    this,
                    $"No se ha podido verificar qué savedata se van a eliminar.\n\nPor seguridad, no se ha borrado nada.\n\nDetalle: {ex.Message}",
                    "Eliminación bloqueada");
                return;
            }

            var totalSlots = previews.Sum(x => x.SlotCount);
            var coveredSlots = previews.Sum(x => x.CoveredSlotCount);
            var uncoveredSlots = previews.Sum(x => x.UncoveredSlotCount);

            if (totalSlots == 0)
            {
                LogUi("Eliminación cancelada: no se encontraron savedata coincidentes con la selección.", "warn");
                DialogService.ShowInfo(
                    this,
                    "No se encontraron savedata coincidentes para los títulos seleccionados.\n\nNo se ha borrado nada.",
                    "Eliminación bloqueada");
                return;
            }

            var items = previews.Select(p =>
            {
                var coverage = p.UncoveredSlotCount == 0
                    ? $"{p.SlotCount} slot(s) · copia local para todos"
                    : $"{p.SlotCount} slot(s) · {p.UncoveredSlotCount} sin copia local";
                var slots = p.SlotNames.Count == 0 ? "Sin slots" : string.Join(", ", p.SlotNames);
                return new ConfirmationWindow.ConfirmationItem(
                    p.TitleId,
                    $"{(string.IsNullOrWhiteSpace(p.TitleName) ? "Nombre no disponible" : p.TitleName)} · {coverage}\n    {slots}");
            }).ToList();

            var summary = $"Se eliminarán {totalSlots} savedata de {previews.Count} título(s) de la consola {_config.Name} ({_config.Ip}).\n\n" +
                          $"Copia local disponible: {coveredSlots}/{totalSlots} slot(s).";

            if (uncoveredSlots > 0)
            {
                summary += $"\n\nATENCIÓN: {uncoveredSlots} slot(s) NO tienen una copia local asociada. " +
                           "La eliminación es permanente y Garlic SaveMgr no podrá recuperarlos mediante una copia local.";
                LogUi($"ATENCIÓN: {uncoveredSlots} savedata(s) seleccionados no tienen copia local asociada.", "warn");
            }

            summary += "\n\nEsta operación NO tiene deshacer.";

            var deleteMode = ConsoleDeleteMode.Permanent;
            if (uncoveredSlots > 0)
            {
                var choice = DeleteSafetyWindow.Show(
                    this,
                    "Savedata sin copia de seguridad",
                    summary + "\n\nPuedes crear primero un respaldo interno en la PS5 mediante ftpsrv. Si el respaldo no termina correctamente, no se realizará el borrado.",
                    items);

                if (choice is null)
                {
                    LogUi("Eliminación cancelada por el usuario.", "warn");
                    return;
                }

                deleteMode = choice.Value;
            }
            else if (!ConfirmationWindow.Show(
                    this,
                    "Confirmar eliminación",
                    summary,
                    items,
                    "Eliminar definitivamente"))
            {
                LogUi("Eliminación cancelada por el usuario.", "warn");
                return;
            }

            using var consoleDeleteGuard = deleteMode == ConsoleDeleteMode.InternalBackup
                ? BackupService.SuppressPcTrashMoves()
                : null;

            if (deleteMode == ConsoleDeleteMode.InternalBackup)
            {
                try
                {
                    LogUi($"Preparando respaldo interno para {uncoveredSlots} savedata(s) sin copia local...", "warn");
                    await _runner.BackupUncoveredSavesInternallyAsync(previews, _config, LogUi);
                    LogUi("Todos los respaldos internos se han creado y verificado antes del borrado.", "ok");
                }
                catch (Exception ex)
                {
                    LogUi($"Eliminación bloqueada: no se pudo completar el respaldo interno: {ex.Message}", "error");
                    DialogService.ShowError(this,
                        $"No se ha eliminado ningún savedata.\n\nNo se pudo completar el respaldo interno previo al borrado.\n\nDetalle: {ex.Message}",
                        "Eliminación bloqueada");
                    return;
                }
            }

            LogUi(deleteMode == ConsoleDeleteMode.InternalBackup
                ? $"Eliminación confirmada con respaldo interno: {totalSlots} savedata(s) en {previews.Count} título(s)."
                : $"Eliminación confirmada: {totalSlots} savedata(s) en {previews.Count} título(s).", "warn");

            var progress = new Progress<(int Index, int Total, string TitleId, string Uid, string State)>(p =>
            {
                BackupProgress.Maximum = Math.Max(p.Total, 1);
                BackupProgress.Value = p.Index;
                MarkTitle(p.TitleId, p.Uid, p.State);
            });

            var outcome = await _runner.RunDeleteAsync(selected, _config, progress, LogUi);

            // 6.8.3: refresco automatico despues de borrar en consola.
            // La pestaña COPIAR debe reflejar inmediatamente los titulos que
            // siguen existiendo en la PS5, sin obligar al usuario a pulsar
            // ESCANEAR manualmente. La vista RESTAURAR se reconstruye tambien
            // para mantener coherencia entre ambas pestañas.
            if (!outcome.Canceled && outcome.Succeeded > 0)
            {
                LogUi("Actualizando automaticamente las listas despues de la eliminacion...", "info");
                LoadBackups();
                await ScanAsync();
                _modules.NotifyStateChanged(ModuleState.Ps5TrashChanged);
            }

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
        finally
        {
            SetBusy(false, false);
        }
    }

    private async void DeleteLocal_Click(object sender, RoutedEventArgs e)
    {
        var selectedGroups = _restoreGroups.Where(x => x.Selected).ToList();
        var selected = selectedGroups.SelectMany(g => g.Backups).ToList();
        if (selected.Count == 0)
        {
            DialogService.ShowInfo(this, "Selecciona al menos un juego.", "Garlic SaveMgr");
            return;
        }

        var items = selectedGroups
            .Select(g => new ConfirmationWindow.ConfirmationItem(g.TitleId, $"{g.TitleName} · {g.BackupCountText}"))
            .ToList();

        var allowPcTrash = IsModuleAvailable("trash");
        if (!allowPcTrash)
        {
            LogUi("Módulo Papelera no disponible: se limita la eliminación local a la eliminación definitiva.", "warn");
            ShowMissingModuleWarning("recuperación de copias mediante Papelera PC", "trash",
                "No será posible mover la copia a pc_trash ni recuperarla posteriormente desde la aplicación mientras falte este módulo. La eliminación definitiva no tiene recuperación en PC.");
        }

        // 6.8.6.15: antes de cualquier retirada del backup activo, la operación
        // exige seguridad local + integridad SHA-256. El usuario decide después
        // si elimina definitivamente o mueve el mismo backup a pc_trash.
        if (!IsModuleAvailable("security"))
        {
            ShowMissingModuleWarning("auditoría detallada de integridad", "security",
                "La eliminación local seguirá usando la verificación SHA-256 del núcleo, pero no habrá pestaña SEGURIDAD para revisar manualmente el estado de las copias.");
        }

        LogUi($"Verificando seguridad e integridad de {selected.Count} copia(s) antes de la eliminación local...", "info");
        var integrityFailures = await VerifyBackupsForRestoreAsync(selected);
        if (integrityFailures.Count > 0)
        {
            var details = string.Join("\n", integrityFailures.Select(x =>
                $"• {x.Backup.TitleId} / {x.Backup.SaveName} — {x.Message}"));
            LogUi($"Eliminación local bloqueada: {integrityFailures.Count} copia(s) no superaron SHA-256.", "error");
            DialogService.ShowError(this,
                "NO SE HA ELIMINADO NI MOVIDO NINGUNA COPIA.\n\n" +
                "Una o más copias no superan la verificación SHA-256. Revisa SEGURIDAD antes de continuar.\n\n" + details,
                "Integridad de copia no válida");
            return;
        }
        LogUi("Seguridad e integridad: OK en todas las copias seleccionadas.", "ok");

        var summary = $"Se han validado {selected.Count} copia(s) locales de {selectedGroups.Count} juego(s).\n\n" +
                      "Elige cómo retirar estas copias del almacenamiento activo.";
        var choice = DeleteSafetyWindow.ShowPc(this, "Eliminar copias locales",
            summary + (allowPcTrash
                ? "\n\nLa opción Papelera mueve el mismo IMG + JSON al directorio interno pc_trash. La eliminación definitiva no utiliza la Papelera de Windows."
                : "\n\nLa Papelera PC no está cargada. Solo se permitirá la eliminación definitiva mientras falte el módulo Papelera."),
            items, allowMoveToTrash: allowPcTrash);
        if (choice is null)
        {
            LogUi("Eliminación local cancelada por el usuario.", "warn");
            return;
        }

        SetBusy(true, false);
        try
        {
            var processed = 0;
            if (choice.Value == PcBackupDeleteMode.MoveToTrash)
            {
                foreach (var b in selected)
                {
                    try
                    {
                        var trashPath = BackupService.MoveLocalToPcTrash(b.Model);
                        processed++;
                        LogUi($"Copia local movida a la Papelera del PC: {b.Model.TitleId} / {b.Model.SaveName} → {trashPath}", "ok");
                    }
                    catch (Exception ex)
                    {
                        LogUi($"ERR moviendo copia a la Papelera del PC: {ex.Message}", "error");
                    }
                }

                LogUi($"Copias locales retiradas de la lista activa: {processed}/{selected.Count}. Papelera del PC: {AppPaths.PcTrashDirectory}", "info");
                LoadBackups();
                if (processed > 0)
                {
                    LogUi("Actualizando automáticamente la Papelera del PC después de mover las copias...", "info");
                    _modules.NotifyStateChanged(ModuleState.PcTrashChanged);
                }
                StatusLabel.Text = $"{processed} copia(s) movida(s) a la Papelera del PC.";
            }
            else
            {
                foreach (var b in selected)
                {
                    try
                    {
                        BackupService.DeleteLocalPermanently(b.Model);
                        processed++;
                        LogUi($"Copia local eliminada definitivamente: {b.Model.TitleId} / {b.Model.SaveName}", "ok");
                    }
                    catch (Exception ex)
                    {
                        LogUi($"ERR eliminando definitivamente la copia local: {ex.Message}", "error");
                    }
                }

                LoadBackups();
                StatusLabel.Text = $"{processed} copia(s) eliminada(s) definitivamente.";
                NotifyCompletion("Eliminación definitiva", $"Completadas: {processed}; errores: {selected.Count - processed}.");
            }
        }
        catch (Exception ex)
        {
            LogUi($"ERR en eliminación local: {ex.Message}", "error");
            DialogService.ShowError(this, ex.Message, "Eliminación local");
        }
        finally
        {
            SetBusy(false, false);
        }
    }

    private void ExportZip_Click(object sender, RoutedEventArgs e)
    {
        var selectedGroups = _restoreGroups.Where(x => x.Selected).ToList();
        var selected = selectedGroups.SelectMany(g => g.Backups).Select(x => x.Model).ToList();
        if (selected.Count == 0) { DialogService.ShowInfo(this, "Selecciona al menos un juego.", "Exportar ZIP"); return; }
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
            LogUi($"Exportadas {selected.Count} copias ({selectedGroups.Count} juegos) a {path}", "ok");
            NotifyCompletion("Exportación terminada", $"{selected.Count} copias de {selectedGroups.Count} juegos guardadas en {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            LogUi($"ERR exportando ZIP: {ex.Message}", "error");
            DialogService.ShowError(this, ex.Message, "Exportar ZIP");
        }
    }

    private void LoadBackups()
    {
        _backups = BackupService.LoadLocalBackups().Select(b => new BackupRow(b)).ToList();

        _restoreGroups = _backups
            .GroupBy(x => x.TitleId ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(g => new RestoreGameGroup(
                g.Key,
                g.Select(x => x.TitleName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Nombre no disponible",
                g.ToList()))
            .OrderBy(x => x.TitleName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _restoreGroupView = CollectionViewSource.GetDefaultView(_restoreGroups);
        _restoreGroupView.Filter = RestoreGroupFilter;
        RestoreGrid.ItemsSource = _restoreGroupView;
        SimpleRestoreList.ItemsSource = _restoreGroups;
        RestoreCountLabel.Text = $"{_restoreGroups.Count} juegos · {_backups.Count} slots";
        _snapshots = SnapshotEngine.Load();
        _modules.NotifyStateChanged(ModuleState.BackupsChanged);
        StartBackupCoverLoading(_backups);
    }

    private void StartBackupCoverLoading(IList<BackupRow> rows)
    {
        var token = _coverLoadCts.Token;
        foreach (var group in rows
                     .Where(r => !string.IsNullOrWhiteSpace(r.TitleId))
                     .GroupBy(r => r.TitleId, StringComparer.OrdinalIgnoreCase))
        {
            _ = LoadCoverIntoBackupGroupAsync(group.ToList(), token);
        }
    }

    private async Task LoadCoverIntoBackupGroupAsync(IList<BackupRow> rows, CancellationToken ct)
    {
        var first = rows.FirstOrDefault();
        if (first is null || string.IsNullOrWhiteSpace(first.TitleId)) return;
        await _coverWorkGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = _metadata.GetCachedCoverPath(first.TitleId)
                ?? await _covers.EnsureCoverAsync(first.TitleId, first.TitleName, message => LogService.Write(message, "INFO"), ct);
            if (path is null || ct.IsCancellationRequested) return;
            var image = await _covers.LoadImageAsync(path, ct);
            if (image is null || ct.IsCancellationRequested) return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                foreach (var row in rows) row.CoverImage = image;
                _metadata.SetCoverPath(first.TitleId, path);
                foreach (var group in _restoreGroups.Where(g => string.Equals(g.TitleId, first.TitleId, StringComparison.OrdinalIgnoreCase)))
                    group.CoverImage ??= image;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogService.Write($"Carátula restore {first.TitleId}: {ex.Message}", "WARN"); }
        finally { _coverWorkGate.Release(); }
    }

    private bool TitleFilter(object obj)
    {
        if (obj is not TitleRow row) return false;
        var q = BackupSearchBox?.Text?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(q) || Contains(row.TitleId, q) || Contains(row.TitleName, q) || Contains(row.Uid, q);
    }

    private bool RestoreGroupFilter(object obj)
    {
        if (obj is not RestoreGameGroup group) return false;
        var q = RestoreSearchBox?.Text?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(q) || Contains(group.TitleId, q) || Contains(group.TitleName, q) || Contains(group.OwnerDisplay, q) || Contains(group.LatestDate, q) || Contains(group.BackupCountText, q);
    }

    private static bool Contains(string? value, string query) => (value ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase);
    private void BackupSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _titleView?.Refresh();
    private void RestoreSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _restoreGroupView?.Refresh();

    private void MarkTitle(string titleId, string uid, string state)
    {
        var row = _titles.FirstOrDefault(x => x.TitleId == titleId && GarlicApi.Norm(x.Uid) == GarlicApi.Norm(uid));
        if (row == null) return;
        row.State = state;
        row.Foreground = ThemeManager.GetStatusBrush(state);
    }

    private void MarkBackup(int row, string state)
    {
        if (row < 0 || row >= _activeRestoreRows.Count) return;
        var backup = _activeRestoreRows[row];
        backup.State = state;
        backup.Foreground = ThemeManager.GetStatusBrush(state);
        var group = _restoreGroups.FirstOrDefault(g => g.Backups.Contains(backup));
        group?.RefreshSummary();
        _restoreGroupView?.Refresh();
    }

    private void SelectAllBackup_Click(object sender, RoutedEventArgs e) { foreach (var x in _titles) x.Selected = true; _titleView?.Refresh(); }
    private void SelectNoneBackup_Click(object sender, RoutedEventArgs e) { foreach (var x in _titles) x.Selected = false; _titleView?.Refresh(); }
    private void SelectAllRestore_Click(object sender, RoutedEventArgs e) { foreach (var g in _restoreGroups) g.SetSelection(true); _restoreGroupView?.Refresh(); }
    private void SelectNoneRestore_Click(object sender, RoutedEventArgs e) { foreach (var g in _restoreGroups) g.SetSelection(false); _restoreGroupView?.Refresh(); }
    private void ReloadRestore_Click(object sender, RoutedEventArgs e) => LoadBackups();
    private void Cancel_Click(object sender, RoutedEventArgs e) { _discoveryCts?.Cancel(); _runner.Cancel(); }
    private void OpenBackups_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.EncDirectory);
    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogsDirectory);

    private bool EnsureIp()
    {
        if (IsValidConsoleAddress(_config.Ip, _config.Port)) return true;

        // El encabezado puede seguir mostrando una consola detectada mientras la
        // configuración seleccionada ha quedado incompleta. Recuperar la conexión
        // runtime que Garlic marcó como activa evita un falso negativo en Guardar
        // copia/Papelera/restauración.
        // Con varios perfiles históricos, preferir la consola vista más recientemente
        // en lugar de la primera por orden de IP.
        var active = _connections
            .Where(c => c.GarlicApiAvailable == true && IsValidConsoleAddress(c.Ip, c.Port))
            .OrderByDescending(c => c.LastSeenLocal ?? DateTime.MinValue)
            .FirstOrDefault();

        if (active is not null)
        {
            _config = active.ToConfig();
            SettingsService.Save(_config);
            UpdateConsoleLabel();
            _modules.NotifyStateChanged(ModuleState.ConnectionsChanged);
            LogService.Write($"Consola activa recuperada para la operación: {_config.Ip}:{_config.Port}.", "INFO");
            return true;
        }

        if (_initialConnection.SuppressConsoleWarning)
        {
            // La conexión/detección inicial (o una reconexión) sigue en curso: no se
            // advierte todavía. La operación simplemente no procede hasta conocer el resultado.
            return false;
        }

        // No se borra la dirección configurada: el encabezado sigue mostrando el
        // destino seleccionado y las siguientes validaciones la comprueban tal cual.
        DialogService.ShowWarning(this, "No hay una consola válida conectada. Pulsa 'Detectar consola'.", "Garlic SaveMgr");
        return false;
    }

    private static bool IsValidConsoleAddress(string? ip, int port)
        => port is >= 1 and <= 65535 && IPAddress.TryParse(ip, out var address) &&
           address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
           !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.None);


    private void SetBusy(bool busy, bool restore)
    {
        if (busy)
        {
            // Las operaciones del usuario tienen prioridad absoluta sobre las carátulas.
            // Cancelamos las peticiones/decodificaciones en curso; al quedar libre la UI
            // se relanza únicamente contra el estado actual y la caché evita trabajo duplicado.
            _coverLoadCts.Cancel();
        }

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

        if (!busy && IsLoaded)
        {
            // Reanudar en cuanto termina una operación crítica. Los archivos ya cacheados
            // salen por la ruta rápida y los faltantes vuelven a entrar en la cola acotada.
            StartTitleCoverLoading(_titles);
            StartBackupCoverLoading(_backups);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var row in _titles) row.RefreshThemeBrush();
            foreach (var row in _activeRestoreRows) row.RefreshThemeBrush();
            foreach (var group in _restoreGroups) group.RefreshThemeBrush();
            _modules.NotifyStateChanged(ModuleState.ThemeChanged);
            foreach (var block in LogBox.Document.Blocks.OfType<Paragraph>())
                block.Foreground = ThemeManager.GetLogBrush(block.Tag?.ToString());
            InvalidateVisual();
        });
    }

    private void LogUi(string message, string level) => LogService.Write(message, level.ToUpperInvariant());

    private void OnLogMessage(string message, string level)
    {
        Dispatcher.Invoke(() =>
        {
            var p = new Paragraph(new Run($"{DateTime.Now:HH:mm:ss} {message}")) { Tag = level };
            p.Foreground = ThemeManager.GetLogBrush(level);
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

    public Window HostWindow => this;
    System.Windows.Threading.Dispatcher IModuleHostContext.Dispatcher => base.Dispatcher;
    public ConsoleConfig Config => _config;
    public OperationRunner Runner => _runner;
    public GameMetadataService Metadata => _metadata;
    public CoverCacheService Covers => _covers;
    public IReadOnlyList<TitleRow> Titles => _titles;
    public IReadOnlyList<BackupRow> Backups => _backups;
    public IReadOnlyList<ConsoleConnection> Connections => _connections;
    public IReadOnlyList<SnapshotRecord> Snapshots => _snapshots;
    public bool IsConsoleConfigured => IsValidConsoleAddress(_config.Ip, _config.Port);
    Task IModuleHostContext.ScanAsync() => ScanAsync();
    bool IModuleHostContext.EnsureIp() => EnsureIp();
    void IModuleHostContext.SetBusy(bool busy, bool restore) => SetBusy(busy, restore);
    void IModuleHostContext.NotifyCompletion(string title, string message) => NotifyCompletion(title, message);
    public void ReloadBackups() => LoadBackups();
    public void SetStatus(string message) => StatusLabel.Text = message;
    public void Log(string message,string level)=>LogUi(message,level);

    protected override void OnClosed(EventArgs e)
    {
        LogService.Message -= OnLogMessage;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _coverLoadCts.Cancel();
        _coverLoadCts.Dispose();
        _payloadCacheCts.Cancel();
        _payloadCacheCts.Dispose();
        _covers.Dispose();
        _coverWorkGate.Dispose();
        _runner.Cancel();
        _modules.Dispose();
        base.OnClosed(e);
    }
}

public abstract class StatusRowBase : INotifyPropertyChanged
{
    private Brush _foreground = ThemeManager.GetBrush("TextMuted");
    private string _state = "";
    public Brush Foreground { get => _foreground; set { if (Equals(_foreground, value)) return; _foreground = value; OnPropertyChanged(); } }
    public string State { get => _state; set { if (_state == value) return; _state = value; OnPropertyChanged(); } }
    public void RefreshThemeBrush() => Foreground = ThemeManager.GetStatusBrush(State);
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
    private bool _syncingSelection;
    private ImageSource? _coverImage;
    private string _state = "";
    private Brush _foreground = ThemeManager.GetBrush("TextMuted");

    public string TitleId { get; }
    public string TitleName { get; }
    public List<BackupRow> Backups { get; }
    public int BackupCount => Backups.Count;
    public string BackupCountText => BackupCount == 1 ? "1 slot" : $"{BackupCount} slots";

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            if (!_syncingSelection)
            {
                _syncingSelection = true;
                try { foreach (var backup in Backups) backup.Selected = value; }
                finally { _syncingSelection = false; }
            }
            OnPropertyChanged();
        }
    }

    public ImageSource? CoverImage
    {
        get => _coverImage;
        set { if (Equals(_coverImage, value)) return; _coverImage = value; OnPropertyChanged(); }
    }

    public string OwnerDisplay => string.Join(", ", Backups
        .SelectMany(b => b.Model.Owner.Select(x => $"{x.Key}={x.Value}"))
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Distinct(StringComparer.OrdinalIgnoreCase));

    public string LatestDate => Backups
        .Select(b => b.Date)
        .Where(d => !string.IsNullOrWhiteSpace(d))
        .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault() ?? "—";

    public string State
    {
        get => _state;
        private set { if (_state == value) return; _state = value; OnPropertyChanged(); }
    }

    public Brush Foreground
    {
        get => _foreground;
        private set { if (Equals(_foreground, value)) return; _foreground = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Detailed status for every hidden slot. The main grid stays grouped by game;
    /// this string is shown as the tooltip of the group Status cell.
    /// </summary>
    public string SlotStatesDisplay => string.Join(Environment.NewLine,
        Backups.Select(b => $"{b.SaveName}: {FormatSlotState(b.State)}"));

    public void RefreshThemeBrush() => Foreground = ThemeManager.GetStatusBrush(_state);

    public RestoreGameGroup(string titleId, string titleName, List<BackupRow> backups)
    {
        TitleId = titleId ?? "";
        TitleName = string.IsNullOrWhiteSpace(titleName) ? "Nombre no disponible" : titleName;
        Backups = backups;
        SetSelection(true);
        RefreshSummary();
    }

    public void ToggleSelection() => SetSelection(!Selected);
    public void SetSelection(bool selected) => Selected = selected;

    public void RefreshSelection()
    {
        var allSelected = Backups.Count > 0 && Backups.All(x => x.Selected);
        if (_selected == allSelected) return;
        _selected = allSelected;
        OnPropertyChanged(nameof(Selected));
    }

    public void RefreshSummary()
    {
        RefreshSelection();

        var total = Backups.Count;
        var ok = Backups.Count(x => string.Equals(x.State, "ok", StringComparison.OrdinalIgnoreCase));
        var err = Backups.Count(x => string.Equals(x.State, "err", StringComparison.OrdinalIgnoreCase));
        var proc = ok + err;
        var pending = Math.Max(0, total - proc);

        if (proc == 0)
        {
            State = total == 0 ? "—" : "PENDIENTE";
            Foreground = ThemeManager.GetBrush("TextMuted");
        }
        else if (pending > 0)
        {
            State = err > 0
                ? $"ERROR · {ok} OK / {err} ERR / {pending} pend."
                : $"EN CURSO · {proc}/{total}";
            Foreground = err > 0 ? ThemeManager.GetBrush("Danger") : ThemeManager.GetBrush("Warning");
        }
        else if (err > 0)
        {
            State = $"ERROR · {ok} OK / {err} ERR";
            Foreground = ThemeManager.GetBrush("Danger");
        }
        else
        {
            State = $"OK · {ok}/{total}";
            Foreground = ThemeManager.GetBrush("Success");
        }

        OnPropertyChanged(nameof(BackupCount));
        OnPropertyChanged(nameof(BackupCountText));
        OnPropertyChanged(nameof(OwnerDisplay));
        OnPropertyChanged(nameof(LatestDate));
        OnPropertyChanged(nameof(SlotStatesDisplay));
    }

    private static string FormatSlotState(string? state) => state switch
    {
        "ok" => "OK",
        "err" => "ERROR",
        "proc" => "EN CURSO",
        _ => "PENDIENTE"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BackupRow : StatusRowBase
{
    private bool _selected = true;
    private string _integrityStatus = "SIN COMPROBAR";
    private string _integrityDetail = "Aún no verificada";
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
    public string IntegrityStatus => _integrityStatus;
    public string IntegrityDetail => _integrityDetail;
    private ImageSource? _coverImage;
    public ImageSource? CoverImage { get => _coverImage; set { if (Equals(_coverImage, value)) return; _coverImage = value; OnPropertyChanged(); } }
    public BackupRow(BackupEntry model) => Model = model;

    public void SetIntegrity(BackupIntegrityResult result)
    {
        _integrityStatus = result.Status switch
        {
            BackupIntegrityStatus.Valid => "OK",
            BackupIntegrityStatus.MissingHash => "SIN HASH",
            BackupIntegrityStatus.Mismatch => "CORRUPTA",
            BackupIntegrityStatus.Error => "ERROR",
            _ => "SIN COMPROBAR"
        };
        _integrityDetail = result.Status switch
        {
            BackupIntegrityStatus.Valid => $"SHA-256 {result.ActualSha256}",
            BackupIntegrityStatus.MissingHash => "La copia no contiene SHA-256 de referencia.",
            BackupIntegrityStatus.Mismatch => $"Esperado: {result.ExpectedSha256}\nActual:   {result.ActualSha256}",
            BackupIntegrityStatus.Error => result.ErrorMessage,
            _ => "Aún no verificada"
        };
        OnPropertyChanged(nameof(IntegrityStatus));
        OnPropertyChanged(nameof(IntegrityDetail));
    }

    private static string FormatBytes(long n)
    {
        double d = n;
        foreach (var u in new[] { "B", "KB", "MB", "GB" }) { if (d < 1024) return $"{d:0.0} {u}"; d /= 1024; }
        return $"{d:0.0} TB";
    }
}
