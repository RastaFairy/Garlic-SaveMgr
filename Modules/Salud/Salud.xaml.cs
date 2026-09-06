using System.Windows;
using System.Windows.Controls;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Modules.Salud.Core;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr.Modules.Salud;

public sealed class SaludModule : IExecutableModule
{
    private readonly IModuleHostContext _host;
    private readonly SaludView _view;
    private readonly SaludEngine _engine;
    private readonly System.Windows.Threading.DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _refreshGeneration;
    private FileSystemWatcher? _snapshotWatcher;
    private SaludReport? _lastReport;

    public SaludModule(IModuleHostContext host)
    {
        _host = host;
        _view = new SaludView(this);
        _engine = new SaludEngine(new SaludHistoryStore());
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RefreshAsync(); };
        ConfigureSnapshotWatcher();
        _ = RefreshAsync();
    }

    public string Id => "salud";
    public string Header => "SALUD 2.2";
    public UserControl View => _view;

    public void OnHostStateChanged(string state)
    {
        if (state is ModuleState.BackupsChanged or ModuleState.ConnectionsChanged or ModuleState.SelectionChanged or ModuleState.ThemeChanged or ModuleState.ConfigChanged)
            _ = RefreshAsync();
    }

    internal SaludAiContext? BuildAiContext() => _lastReport is null ? null : SaludAiContextBuilder.Build(_lastReport);

    internal async Task RefreshAsync()
    {
        try
        {
            var generation = ++_refreshGeneration;
            var config = _host.Config;
            var titles = _host.Titles.Select(x => x.ToModel()).ToList();
            var backups = _host.Backups.Select(x => x.Model).ToList();
            var snapshots = _host.Snapshots.ToList();
            var connections = _host.Connections.ToList();
            var active = connections.FirstOrDefault(c => string.Equals(c.Ip, config.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == config.Port);
            var online = active?.GarlicApiAvailable == true;
            var configured = _host.IsConsoleConfigured;
            var applicationTelemetry = ApplicationTelemetryService.GetSnapshot();
            var coverTelemetry = _host.Covers.GetTelemetrySnapshot();

            var report = await Task.Run(() => _engine.Build(config, titles, backups, snapshots, connections, online, configured, applicationTelemetry, coverTelemetry));
            if (generation != _refreshGeneration) return;
            _lastReport = report;
            ApplyReport(report, snapshots, backups, connections);
        }
        catch (Exception ex)
        {
            ApplicationTelemetryService.RecordException("SaludModule.Refresh", ex, unhandled: false);
            _host.Log($"WARN Salud 2.2: {ex.Message}", "WARN");
        }
    }

    private void ApplyReport(SaludReport report, IReadOnlyList<SnapshotRecord> snapshots, IReadOnlyList<BackupEntry> backups, IReadOnlyList<ConsoleConnection> connections)
    {
        _view.SaludHealthScore.Text = $"{report.Health.Score}/100";
        _view.SaludHealthLabel.Text = report.Health.Label;
        _view.SaludHealthFactors.Text = string.Join(Environment.NewLine, report.Health.Factors.Take(3));
        _view.SaludSessionSummary.Text = report.SessionSummary;
        _view.SaludStorageTrend.Text = report.Storage.FreePercent is { } free
            ? $"Almacenamiento: {FormatBytes(report.Storage.BackupBytes)} en backups · {free:0.#}% libre"
            : "Almacenamiento: datos de espacio no disponibles";

        _view.SaludPs5Summary.Text = report.Console.Online ? "Conectada" : "Sin conexión";
        _view.SaludPs5Detail.Text = $"{report.Console.Titles} títulos · {report.Console.Slots} saves · {report.Console.Address}";
        _view.SaludPcSummary.Text = $"{report.Backups.Count} ficheros";
        _view.SaludPcDetail.Text = $"{report.Backups.Games} juegos · {FormatBytes(report.Storage.BackupBytes)} · libres {FormatBytes(report.Storage.FreeBytes)}";
        _view.SaludSnapshotSummary.Text = $"{report.Snapshots.Count} snapshots";
        _view.SaludSnapshotDetail.Text = report.Snapshots.LatestLocal is { } latest
            ? $"Último: {latest:yyyy-MM-dd HH:mm:ss} · {report.Snapshots.Summary}"
            : "Sin snapshots registrados";
        _view.SaludIntegritySummary.Text = $"{report.Integrity.ValidReferences} con SHA · {report.Integrity.MissingHash} sin SHA" + (report.Integrity.MissingFiles > 0 ? $" · {report.Integrity.MissingFiles} faltantes" : "");
        _view.SaludSmartSummary.Text = $"{report.SmartBackup.State} · {report.Integrity.Summary}";
        _view.SaludApplicationSummary.Text = report.Application.Summary;
        _view.SaludCoverSummary.Text = report.Covers.Summary;
        _view.SaludAnomaliesGrid.ItemsSource = report.Anomalies;
        _view.SaludRecommendationsList.ItemsSource = report.Recommendations;
        _view.SaludSnapshotsGrid.ItemsSource = snapshots
            .OrderByDescending(x => x.CreatedLocal)
            .Select(x => new SaludSnapshotRow(x, _host.Backups))
            .ToList();
        _view.SaludConnectionsList.ItemsSource = connections
            .Select(c => new SaludConnectionRow(c, string.Equals(c.Ip, _host.Config.Ip, StringComparison.OrdinalIgnoreCase) && c.Port == _host.Config.Port))
            .ToList();
        _view.SaludActiveLabel.Text = report.Console.Configured && report.Console.Online ? $"Conexión activa: {report.Console.Address}" : "Sin conexión activa";
        _view.SaludUserCountLabel.Text = report.SessionSummary;
    }

    private void ConfigureSnapshotWatcher()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SnapshotsDirectory);
            _snapshotWatcher = new FileSystemWatcher(AppPaths.SnapshotsDirectory, "index.json")
            { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            _snapshotWatcher.Changed += (_, _) => ScheduleSnapshotRefresh();
            _snapshotWatcher.Created += (_, _) => ScheduleSnapshotRefresh();
            _snapshotWatcher.Renamed += (_, _) => ScheduleSnapshotRefresh();
            _snapshotWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { _host.Log($"WARN Salud 2.2: no se pudo vigilar snapshots: {ex.Message}", "WARN"); }
    }

    private void ScheduleSnapshotRefresh()
    {
        try { _host.Dispatcher.BeginInvoke(new Action(() => { if (_view.IsLoaded) { _debounce.Stop(); _debounce.Start(); } })); }
        catch (InvalidOperationException) { }
    }

    private static string FormatBytes(long n)
    {
        double d = n;
        foreach (var u in new[] { "B", "KB", "MB", "GB" }) { if (d < 1024) return $"{d:0.0} {u}"; d /= 1024; }
        return $"{d:0.0} TB";
    }

    public void Dispose() { _snapshotWatcher?.Dispose(); _debounce.Stop(); }
}

public partial class SaludView : UserControl
{
    private readonly SaludModule _module;
    public SaludView(SaludModule module) { InitializeComponent(); _module = module; }
    private void RefreshSalud_Click(object sender, RoutedEventArgs e) => _ = _module.RefreshAsync();
}

public sealed class SaludConnectionRow
{
    public string Ip { get; }
    public string Garlic { get; }
    public string ElfLdr { get; }
    public string State { get; }
    public string UserIds { get; }

    public SaludConnectionRow(ConsoleConnection connection, bool active)
    {
        Ip = connection.Ip;
        State = active ? "ACTIVA" : "DETECTADA";
        Garlic = connection.GarlicApiAvailable switch { true => "✓ 8082", false => "—", null => "?" };
        ElfLdr = connection.ElfLdrAvailable switch { true => "✓ 9021", false => "—", null => "?" };
        UserIds = connection.UserIds.Count == 0 ? "—" : string.Join(", ", connection.UserIds);
    }
}

public sealed class SaludSnapshotRow
{
    private readonly SnapshotRecord _snapshot;
    private readonly IReadOnlyList<BackupRow> _backups;
    public SaludSnapshotRow(SnapshotRecord snapshot, IReadOnlyList<BackupRow> backups) { _snapshot = snapshot; _backups = backups; }
    public DateTime CreatedLocal => _snapshot.CreatedLocal;
    public string TitleName => string.IsNullOrWhiteSpace(_snapshot.TitleName) ? "Nombre no disponible" : _snapshot.TitleName;
    public string SaveName => _snapshot.SaveName;
    public string SizeDisplay => SaludEngine.ResolveBackup(_snapshot, _backups.Select(x => x.Model).ToList()) is { } backup ? new BackupRow(backup).SizeDisplay : "—";
    public string State => SaludEngine.ResolveBackup(_snapshot, _backups.Select(x => x.Model).ToList()) is not null ? "Backup disponible" : "Solo historial";
}
