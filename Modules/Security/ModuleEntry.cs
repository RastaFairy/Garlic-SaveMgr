using GarlicSaveMgr.Infrastructure;
using System.Windows.Controls;

namespace GarlicSaveMgr.Modules.Security;

public static class ModuleEntry
{
    public static IExecutableModule CreateModule(IModuleHostContext host) => new SecurityModule(host);
}
