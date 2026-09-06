using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Modules.Salud;

public static class ModuleEntry
{
    public static IExecutableModule CreateModule(IModuleHostContext host) => new SaludModule(host);
}
