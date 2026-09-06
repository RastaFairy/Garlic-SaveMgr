using System.Text.Json;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Modules.Salud.Core;

public sealed record SaludHistoryRecord(
    DateTime TimestampLocal,
    int BackupCount,
    long BackupBytes,
    int SnapshotCount,
    long FreeBytes,
    long TotalBytes,
    int IntegrityValid,
    int IntegrityMissingHash,
    int IntegrityMissingFiles,
    bool ConsoleOnline,
    int ConnectionCount);

public sealed class SaludHistoryStore
{
    private readonly object _sync = new();
    private readonly string _file;
    private List<SaludHistoryRecord>? _cache;

    public SaludHistoryStore()
    {
        var dir = Path.Combine(AppPaths.AppDataDirectory, "Salud");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "history.json");
    }

    public void Append(SaludHistoryRecord record)
    {
        lock (_sync)
        {
            var items = LoadInternal();
            items.Add(record);
            if (items.Count > 1000) items = items.Skip(items.Count - 1000).ToList();
            _cache = items;
            try
            {
                File.WriteAllText(_file, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    public IReadOnlyList<SaludHistoryRecord> ReadRecent(int count)
    {
        lock (_sync)
            return LoadInternal().TakeLast(Math.Max(0, count)).ToArray();
    }

    private List<SaludHistoryRecord> LoadInternal()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (!File.Exists(_file)) return _cache = [];
            var json = File.ReadAllText(_file);
            return _cache = JsonSerializer.Deserialize<List<SaludHistoryRecord>>(json) ?? [];
        }
        catch
        {
            return _cache = [];
        }
    }
}
