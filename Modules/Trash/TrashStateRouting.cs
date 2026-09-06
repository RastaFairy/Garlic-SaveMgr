using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Modules.Trash;

/// <summary>Contratos de refresco del módulo PAPELERA. Mantiene separados los dos almacenes.</summary>
public static class TrashStateRouting
{
    public static bool RefreshesPs5Trash(string state) => state == ModuleState.Ps5TrashChanged;
    public static bool RefreshesPcTrash(string state) => state == ModuleState.PcTrashChanged;
}
