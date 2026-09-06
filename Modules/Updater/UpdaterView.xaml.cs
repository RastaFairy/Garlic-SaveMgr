using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GarlicSaveMgr.UpdaterModule.Services;

namespace GarlicSaveMgr.UpdaterModule;

public partial class UpdaterView : UserControl
{
    private readonly Window _hostWindow;
    private readonly string _executablePath;
    private readonly string _runningVersion;
    private readonly Action<string, string> _log;
    private readonly GitHubReleaseService _github = new();
    private readonly UpdateInstaller _installer = new();
    private GitHubReleaseInfo? _latest;
    private bool _loaded;
    private bool _confirming;

    public UpdaterView(Window hostWindow, Action<string, string> log)
    {
        InitializeComponent();
        _hostWindow = hostWindow;
        _executablePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "Garlic_SaveMgr.exe");
        _runningVersion = ResolveRunningVersion(_executablePath);
        _log = log;
        Loaded += OnLoaded;
        VersionLabel.Text = $"Versión instalada: {_runningVersion}";
        StatusLabel.Text = "Listo.";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await CheckForUpdatesAsync(silent: true);
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(silent: false);

    private async Task CheckForUpdatesAsync(bool silent)
    {
        _confirming = false;
        ConfirmContinueButton.Visibility = Visibility.Visible;
        ConfirmCancelButton.Content = "Cancelar";
        ConfirmPanel.Visibility = Visibility.Collapsed;
        SetBusy(true);
        var telemetryId = GarlicSaveMgr.Infrastructure.ApplicationTelemetryService.StartOperation("UPDATE_CHECK", "Comprobación de actualización");
        var telemetryCompleted = false;
        try
        {
            StatusLabel.Text = "Consultando GitHub…";
            var local = ParseVersion(_runningVersion);
            if (local is null)
            {
                _latest = null;
                UpdateButton.IsEnabled = false;
                ReleaseLabel.Text = "—";
                ReleaseBody.Text = "No se pudo determinar de forma segura la versión del Garlic SaveMgr en ejecución. No se ofrecerá ninguna actualización.";
                StatusLabel.Text = "Versión instalada desconocida: actualización bloqueada.";
                _log("Updater: versión instalada desconocida; actualización bloqueada.", "WARN");
                GarlicSaveMgr.Infrastructure.ApplicationTelemetryService.ReportOperation(telemetryId, "RUNNING", 1, "version-unknown");
                GarlicSaveMgr.Infrastructure.ApplicationTelemetryService.CompleteOperation(telemetryId, true);
                telemetryCompleted = true;
                return;
            }

            var release = await _github.GetLatestReleaseAsync(CancellationToken.None, forceRefresh: !silent);
            _latest = release;
            ReleaseLabel.Text = release.Tag;
            ReleaseBody.Text = string.IsNullOrWhiteSpace(release.Body) ? "La release no tiene descripción." : release.Body.Trim();
            OpenGitHubButton.IsEnabled = !string.IsNullOrWhiteSpace(release.HtmlUrl);

            var remote = ParseVersion(release.Tag)
                ?? throw new InvalidDataException($"El tag de GitHub '{release.Tag}' no tiene un formato de versión válido.");

            var updateAvailable = remote > local;
            UpdateButton.IsEnabled = updateAvailable;
            StatusLabel.Text = updateAvailable
                ? $"Hay una actualización disponible: {remote}."
                : $"No hay actualización. {remote} ≤ {local}.";
            _log($"Updater: instalada {_runningVersion}; GitHub {release.Tag}; actualización={(updateAvailable ? "sí" : "no") }{(_github.UsedCachedResult ? " (dato cacheado)" : "")}.", "INFO");
            if (_github.UsedCachedResult)
                StatusLabel.Text += " · usando última información conocida";

            if (!silent && !updateAvailable)
                StatusLabel.Text = $"Ya tienes la versión más reciente. {remote}.";
        }
        catch (Exception ex)
        {
            _latest = null;
            UpdateButton.IsEnabled = false;
            StatusLabel.Text = "No se pudo comprobar GitHub.";
            ReleaseLabel.Text = "—";
            ReleaseBody.Text = ex.Message;
            GarlicSaveMgr.Infrastructure.ApplicationTelemetryService.RecordException("Updater.CheckForUpdates", ex, unhandled: false);
            _log($"Updater: {ex.Message}", "WARN");
            if (!silent)
                ShowInlineError(ex.Message);
            GarlicSaveMgr.Infrastructure.ApplicationTelemetryService.CompleteOperation(telemetryId, false, false, ex.Message);
            telemetryCompleted = true;
        }
        finally
        {
            if (!telemetryCompleted) GarlicSaveMgr.Infrastructure.ApplicationTelemetryService.CompleteOperation(telemetryId, true);
            SetBusy(false);
        }
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latest is null) return;
        var remote = ParseVersion(_latest.Tag);
        var local = ParseVersion(_runningVersion);
        if (remote is null || local is null || remote <= local)
        {
            UpdateButton.IsEnabled = false;
            StatusLabel.Text = "No hay una versión superior para instalar.";
            return;
        }

        ConfirmText.Text =
            $"Se descargará {_latest.AssetName} directamente desde GitHub y se sustituirá el ejecutable actual.\n\n" +
            $"Instalada: {_runningVersion}\nNueva: {_latest.Tag}\n\n" +
            "Garlic SaveMgr se cerrará y se volverá a iniciar automáticamente.";
        ConfirmPanel.Visibility = Visibility.Visible;
        _confirming = true;
        StatusLabel.Text = "Confirma la actualización.";
        SetBusy(false);
        UpdateButton.IsEnabled = false;
    }

    private void ConfirmCancelButton_Click(object sender, RoutedEventArgs e)
    {
        _confirming = false;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        SetBusy(false);
        StatusLabel.Text = "Actualización cancelada por el usuario.";
    }

    private async void ConfirmContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_confirming || _latest is null) return;
        _confirming = false;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        SetBusy(true);

        var remote = ParseVersion(_latest.Tag);
        var local = ParseVersion(_runningVersion);
        if (remote is null || local is null || remote <= local)
        {
            SetBusy(false);
            StatusLabel.Text = "No hay una versión superior para instalar.";
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"GarlicSaveMgr-{Guid.NewGuid():N}.exe");
        try
        {
            StatusLabel.Text = "Descargando ejecutable…";
            var progress = new Progress<long>(done => StatusLabel.Text = $"Descargando: {done / 1024d / 1024d:0.0} MB…");
            await _github.DownloadAsync(_latest, temp, progress, CancellationToken.None);

            StatusLabel.Text = "Verificando ejecutable…";
            if (_latest.AssetSize > 0 && new FileInfo(temp).Length != _latest.AssetSize)
                throw new InvalidDataException("El tamaño del ejecutable descargado no coincide con el asset publicado.");
            if (!await UpdateInstaller.IsValidPeAsync(temp, CancellationToken.None))
                throw new InvalidDataException("El asset descargado no es un ejecutable Windows válido.");
            if (!string.IsNullOrWhiteSpace(_latest.AssetDigest) && _latest.AssetDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                var actual = await UpdateInstaller.Sha256Async(temp, CancellationToken.None);
                var expected = _latest.AssetDigest[7..].Trim().ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("El SHA-256 del ejecutable no coincide con el digest publicado por GitHub.");
            }

            var current = _executablePath;
            if (!File.Exists(current))
                throw new FileNotFoundException("No se encontró el Garlic_SaveMgr.exe actual.", current);

            StatusLabel.Text = "Preparando reinicio…";
            await _installer.ScheduleExecutableReplacementAsync(current, temp, _latest.Tag, CancellationToken.None);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            SetBusy(false);
            StatusLabel.Text = "Actualización cancelada.";
            GarlicSaveMgr.Infrastructure.ApplicationTelemetryService.RecordException("Updater.Install", ex, unhandled: false);
            _log($"Updater: no se pudo instalar {_latest.Tag}: {ex.Message}", "ERROR");
            ShowInlineError(ex.Message);
        }
    }

    private void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latest is null || string.IsNullOrWhiteSpace(_latest.HtmlUrl)) return;
        Process.Start(new ProcessStartInfo(_latest.HtmlUrl) { UseShellExecute = true });
    }

    private void ShowInlineError(string message)
    {
        ConfirmText.Text = message;
        ConfirmPanel.Visibility = Visibility.Visible;
        ConfirmContinueButton.Visibility = Visibility.Collapsed;
        ConfirmCancelButton.Content = "Cerrar";
        _confirming = false;
    }

    private static string ResolveRunningVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var candidate = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                var plus = candidate.IndexOf('+');
                if (plus >= 0) candidate = candidate[..plus];
                var parsed = ParseVersion(candidate);
                if (parsed is not null) return parsed.ToString();
            }
        }
        catch { }

        return "desconocida";
    }

    private void SetBusy(bool busy)
    {
        CheckButton.IsEnabled = !busy && !_confirming;
        UpdateButton.IsEnabled = !busy && !_confirming && _latest is not null && ParseVersion(_latest.Tag) is Version remote && ParseVersion(_runningVersion) is Version local && remote > local;
        ConfirmContinueButton.IsEnabled = !busy && _confirming;
        ConfirmCancelButton.IsEnabled = !busy || _confirming;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private static Version? ParseVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        var start = 0;
        while (start < text.Length && !char.IsDigit(text[start])) start++;
        if (start >= text.Length) return null;
        text = text[start..];
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 4) return null;
        if (parts.Length == 3) text += ".0";
        return Version.TryParse(text, out var version) ? version : null;
    }
}
