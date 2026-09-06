using GarlicSaveMgr.Infrastructure;
using System.Windows.Controls;

namespace GarlicSaveMgr.Modules.Trash;

public static class ModuleEntry
{
    public static IExecutableModule CreateModule(IModuleHostContext host) => new TrashModule(host);
}
