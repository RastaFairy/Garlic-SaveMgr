using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GarlicSaveMgr.Models;
using GarlicSaveMgr.Services;

namespace GarlicSaveMgr.Infrastructure;

public interface IModuleHostContext
{
    Window HostWindow { get; }
    Dispatcher Dispatcher { get; }
    ConsoleConfig Config { get; }
    OperationRunner Runner { get; }
    GameMetadataService Metadata { get; }
    CoverCacheService Covers { get; }
    IReadOnlyList<TitleRow> Titles { get; }
    IReadOnlyList<BackupRow> Backups { get; }
    IReadOnlyList<ConsoleConnection> Connections { get; }
    IReadOnlyList<SnapshotRecord> Snapshots { get; }
    bool IsConsoleConfigured { get; }
    bool EnsureIp();
    Task ScanAsync();
    void ReloadBackups();
    void SetBusy(bool busy, bool restore = false);
    void SetStatus(string message);
    void Log(string message, string level);
    void NotifyCompletion(string title, string message);
}


public static class ModuleState
{
    public const string BackupsChanged = "backups-changed";
    public const string Ps5TrashChanged = "ps5-trash-changed";
    public const string PcTrashChanged = "pc-trash-changed";
    public const string ConnectionsChanged = "connections-changed";
    public const string SelectionChanged = "selection-changed";
    public const string ThemeChanged = "theme-changed";
    public const string ConfigChanged = "config-changed";
}

public interface IModuleCapability { }

public interface ISecurityModuleCapability : IModuleCapability
{
    Task<IReadOnlyList<ModuleIntegrityFailure>> VerifyForRestoreAsync(IReadOnlyList<BackupRow> rows);
}

public sealed record ModuleIntegrityFailure(BackupEntry Backup, string Message);

public interface IExecutableModule : IDisposable
{
    string Id { get; }
    string Header { get; }
    UserControl View { get; }
    void OnHostStateChanged(string state);
}
