using System.Windows.Controls;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.UpdaterModule;

public static class ModuleEntry
{
    public static IExecutableModule CreateModule(IModuleHostContext host) => new UpdaterRuntime(host);
}

public sealed class UpdaterRuntime : IExecutableModule
{
    private readonly UpdaterView _view;
    public UpdaterRuntime(IModuleHostContext host) => _view = new UpdaterView(host.HostWindow, host.Log);
    public string Id => "updater";
    public string Header => "ACTUALIZADOR";
    public UserControl View => _view;
    public void OnHostStateChanged(string state) { }
    public void Dispose() { }
}
