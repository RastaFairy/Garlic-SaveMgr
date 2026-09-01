using System.Windows;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr;

public partial class SettingsWindow : Window
{
    private readonly ConsoleConfig _cfg;
    public SettingsWindow(ConsoleConfig cfg, Window owner)
    {
        InitializeComponent(); Owner=owner; _cfg=cfg; NameBox.Text=string.IsNullOrWhiteSpace(cfg.Name)?"PS5":cfg.Name; IpBox.Text=cfg.Ip; PortBox.Text=cfg.Port.ToString();
    }
    private async void Ping_Click(object sender,RoutedEventArgs e)
    {
        var ip=IpBox.Text.Trim(); if(string.IsNullOrWhiteSpace(ip)){MessageBox.Show(this,"IP no configurada.","Verificar conexión");return;}
        if(!int.TryParse(PortBox.Text,out var port)||port<1||port>65535){MessageBox.Show(this,"Puerto inválido.","Verificar conexión");return;}
        using var api = new GarlicApi(ip, port);
        var ok=await api.PingAsync();
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "PS5" : NameBox.Text.Trim();
        MessageBox.Show(this,$"{name} ({ip}:{port}):  {(ok?"OK":"sin respuesta")}","Verificar conexión");
    }
    private void Ok_Click(object sender,RoutedEventArgs e){if(!int.TryParse(PortBox.Text,out var port)||port<1||port>65535){MessageBox.Show(this,"Puerto inválido.","Ajustes");return;}_cfg.Name=NameBox.Text.Trim();_cfg.Ip=IpBox.Text.Trim();_cfg.Port=port;DialogResult=true;Close();}
    private void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;Close();}
}
