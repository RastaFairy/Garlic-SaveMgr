using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr.Modules.Security;

public sealed class SecurityModule : IExecutableModule, ISecurityModuleCapability
{
    private readonly IModuleHostContext _host;
    private readonly SecurityView _view;
    private readonly ObservableCollection<BackupRow> _backups = [];

    public SecurityModule(IModuleHostContext host)
    {
        _host = host;
        _view = new SecurityView(this);
        Refresh();
    }

    public string Id => "security";
    public string Header => "SEGURIDAD";
    public UserControl View => _view;

    public void OnHostStateChanged(string state)
    {
        switch (state)
        {
            case ModuleState.BackupsChanged:
            case ModuleState.ThemeChanged:
                Refresh();
                break;
        }
    }

    internal IReadOnlyList<BackupRow> Backups => _host.Backups;

    public async Task<IReadOnlyList<ModuleIntegrityFailure>> VerifyForRestoreAsync(IReadOnlyList<BackupRow> rows)
    {
        var failures = new List<ModuleIntegrityFailure>();
        foreach (var row in rows.Distinct())
        {
            var result = await Task.Run(() => BackupService.VerifyIntegrity(row.Model));
            row.SetIntegrity(result);
            switch (result.Status)
            {
                case BackupIntegrityStatus.Valid:
                    _host.Log($"SHA-256 OK: {row.TitleId} / {row.SaveName}", "OK");
                    break;
                case BackupIntegrityStatus.MissingHash:
                    failures.Add(new ModuleIntegrityFailure(row.Model, "Sin SHA-256 de referencia"));
                    _host.Log($"SHA-256 AUSENTE: {row.TitleId} / {row.SaveName}", "ERROR");
                    break;
                case BackupIntegrityStatus.Mismatch:
                    failures.Add(new ModuleIntegrityFailure(row.Model, "SHA-256 no coincide"));
                    _host.Log($"SHA-256 NO COINCIDE: {row.TitleId} / {row.SaveName}", "ERROR");
                    break;
                default:
                    failures.Add(new ModuleIntegrityFailure(row.Model, string.IsNullOrWhiteSpace(result.ErrorMessage) ? "No se pudo verificar" : result.ErrorMessage));
                    _host.Log($"SHA-256 ERROR: {row.TitleId} / {row.SaveName}: {result.ErrorMessage}", "ERROR");
                    break;
            }
        }
        Refresh();
        return failures;
    }

    internal async Task VerifyAsync(IEnumerable<BackupRow> rows)
    {
        var list = rows.Distinct().ToList();
        if (list.Count == 0)
        {
            DialogService.ShowInfo(_host.HostWindow, "No hay copias para verificar.", "SEGURIDAD");
            return;
        }

        try
        {
            _host.SetBusy(true);
            _host.SetStatus("Verificando integridad SHA-256…");
            _host.Log($"Iniciando verificación de integridad de {list.Count} copia(s)...", "INFO");
            foreach (var row in list)
            {
                var result = await Task.Run(() => BackupService.VerifyIntegrity(row.Model));
                row.SetIntegrity(result);
                switch (result.Status)
                {
                    case BackupIntegrityStatus.Valid:
                        _host.Log($"SHA-256 OK: {row.TitleId} / {row.SaveName}", "OK");
                        break;
                    case BackupIntegrityStatus.MissingHash:
                        _host.Log($"SHA-256 AUSENTE: {row.TitleId} / {row.SaveName}", "ERROR");
                        break;
                    case BackupIntegrityStatus.Mismatch:
                        _host.Log($"SHA-256 NO COINCIDE: {row.TitleId} / {row.SaveName}", "ERROR");
                        break;
                    default:
                        _host.Log($"SHA-256 ERROR: {row.TitleId} / {row.SaveName}: {result.ErrorMessage}", "ERROR");
                        break;
                }
            }
            Refresh();
            _host.SetStatus("Verificación de integridad terminada.");
        }
        finally
        {
            _host.SetBusy(false);
        }
    }

    private void Refresh()
    {
        _backups.Clear();
        foreach (var row in _host.Backups) _backups.Add(row);
        _view.SecurityGrid.ItemsSource = _backups;
        var total = _backups.Count;
        var valid = _backups.Count(x => x.IntegrityStatus == "OK");
        var invalid = _backups.Count(x => x.IntegrityStatus is "ERROR" or "CORRUPTA");
        var missing = _backups.Count(x => x.IntegrityStatus == "SIN HASH");
        var pending = Math.Max(0, total - valid - invalid - missing);
        _view.SecuritySummaryLabel.Text = $"{total} copias · {valid} OK · {invalid} con error · {missing} sin hash · {pending} sin comprobar";
    }

    public void Dispose() { }
}

public partial class SecurityView : UserControl
{
    private readonly SecurityModule _module;
    public SecurityView(SecurityModule module)
    {
        InitializeComponent();
        _module = module;
    }

    private async void VerifyAllSecurity_Click(object sender, RoutedEventArgs e) => await _module.VerifyAsync(_module.Backups);

    private async void VerifySelectedSecurity_Click(object sender, RoutedEventArgs e) => await _module.VerifyAsync(_module.Backups.Where(x => x.Selected));
}
