using GarlicSaveMgr.Infrastructure;
using GarlicSaveMgr.Modules.Trash;
using Xunit;

namespace GarlicSaveMgr.Tests;

public sealed class WorkflowStateTests
{
    [Fact]
    public void SavingBackup_DoesNotEnterEitherTrashFlow()
    {
        Assert.False(TrashStateRouting.RefreshesPs5Trash(ModuleState.BackupsChanged));
        Assert.False(TrashStateRouting.RefreshesPcTrash(ModuleState.BackupsChanged));
    }

    [Fact]
    public void Ps5TrashChange_OnlyRefreshesPs5Trash()
    {
        Assert.True(TrashStateRouting.RefreshesPs5Trash(ModuleState.Ps5TrashChanged));
        Assert.False(TrashStateRouting.RefreshesPcTrash(ModuleState.Ps5TrashChanged));
    }

    [Fact]
    public void PcTrashChange_OnlyRefreshesPcTrash()
    {
        Assert.True(TrashStateRouting.RefreshesPcTrash(ModuleState.PcTrashChanged));
        Assert.False(TrashStateRouting.RefreshesPs5Trash(ModuleState.PcTrashChanged));
    }
}
