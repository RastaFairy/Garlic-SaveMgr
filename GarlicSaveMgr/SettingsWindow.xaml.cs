using System.Windows;
using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr;

public partial class SettingsWindow : Window
{
    private readonly ConsoleConfig _cfg;
    private IReadOnlyList<ThemeCatalogItem> _themes = [];
    private ThemeMode _selectedMode;

    public SettingsWindow(ConsoleConfig cfg, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        _cfg = cfg;
        _selectedMode = ThemeManager.CurrentMode;
        NameBox.Text = string.IsNullOrWhiteSpace(cfg.Name) ? "PS5" : cfg.Name;
        IpBox.Text = cfg.Ip;
        PortBox.Text = cfg.Port.ToString();

        _themes = ThemeManager.GetAvailableThemes();
        ThemeBox.ItemsSource = _themes;
        ThemeBox.SelectedItem = _themes.FirstOrDefault(t => string.Equals(t.Id, ThemeManager.CurrentTheme.Id, StringComparison.OrdinalIgnoreCase)) ?? _themes.FirstOrDefault();
        ThemeModeSwitch.IsChecked = _selectedMode == ThemeMode.Dark;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        MaxHeight = Math.Max(MinHeight, workArea.Height - 40);
        UpdateCenter(workArea);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        UpdateCenter(SystemParameters.WorkArea);
    }

    private void UpdateCenter(Rect workArea)
    {
        Left = Math.Max(workArea.Left, workArea.Left + (workArea.Width - ActualWidth) / 2);
        Top = Math.Max(workArea.Top, workArea.Top + (workArea.Height - ActualHeight) / 2);
    }

    private void ThemeModeSwitch_Checked(object sender, RoutedEventArgs e) => _selectedMode = ThemeMode.Dark;
    private void ThemeModeSwitch_Unchecked(object sender, RoutedEventArgs e) => _selectedMode = ThemeMode.Light;

    private async void Ping_Click(object sender, RoutedEventArgs e)
    {
        var ip = IpBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip)) { DialogService.ShowWarning(this, "IP no configurada.", "Verificar conexión"); return; }
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535) { DialogService.ShowWarning(this, "Puerto inválido.", "Verificar conexión"); return; }
        using var api = new GarlicApi(ip, port);
        var ok = await api.PingAsync();
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "PS5" : NameBox.Text.Trim();
        DialogService.ShowInfo(this, $"{name} ({ip}:{port}):  {(ok ? "OK" : "sin respuesta")}", "Verificar conexión");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535) { DialogService.ShowWarning(this, "Puerto inválido.", "Ajustes"); return; }
        _cfg.Name = NameBox.Text.Trim(); _cfg.Ip = IpBox.Text.Trim(); _cfg.Port = port;
        var selected = ThemeBox.SelectedItem as ThemeCatalogItem;
        var themeId = selected?.Id ?? "Default";
        ThemeManager.Apply(themeId, _selectedMode);
        SettingsService.Save(_cfg);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
