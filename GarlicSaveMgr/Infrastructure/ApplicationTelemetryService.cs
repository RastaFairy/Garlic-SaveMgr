using System.Collections.Concurrent;

namespace GarlicSaveMgr.Infrastructure;

/// <summary>
/// Telemetría de proceso para observabilidad. No ejecuta operaciones ni contiene lógica de negocio.
/// Es intencionadamente ligera y en memoria; el histórico persistente pertenece a SALUD.
/// </summary>
public static class ApplicationTelemetryService
{
    private static readonly DateTime StartedAt = DateTime.Now;
    private static long _sequence;
    private static long _lastHeartbeatTicks = StartedAt.Ticks;
    private static readonly ConcurrentDictionary<Guid, TelemetryOperation> Operations = new();
    private static readonly ConcurrentDictionary<string, TelemetryModule> Modules = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<TelemetryException> Exceptions = new();

    public static DateTime StartedLocal => StartedAt;
    public static DateTime LastHeartbeatLocal => new(Volatile.Read(ref _lastHeartbeatTicks));

    public static void Heartbeat(string source)
    {
        Volatile.Write(ref _lastHeartbeatTicks, DateTime.Now.Ticks);
    }

    public static Guid StartOperation(string type, string description)
    {
        var id = Guid.NewGuid();
        Operations[id] = new TelemetryOperation(id, type, description, DateTime.Now, DateTime.Now, "RUNNING", null, null, Interlocked.Increment(ref _sequence));
        return id;
    }

    public static void ReportOperation(Guid id, string state, double? progress = null, string? message = null)
    {
        if (!Operations.TryGetValue(id, out var current)) return;
        Operations[id] = current with { State = state, LastActivityLocal = DateTime.Now, Progress = progress, Message = message };
    }

    public static void CompleteOperation(Guid id, bool success, bool canceled = false, string? error = null)
    {
        if (!Operations.TryGetValue(id, out var current)) return;
        Operations[id] = current with
        {
            State = canceled ? "CANCELLED" : success ? "COMPLETED" : "FAILED",
            LastActivityLocal = DateTime.Now,
            Message = error
        };

        foreach (var pair in Operations.Where(x => x.Value.State is "COMPLETED" or "FAILED" or "CANCELLED")
                                         .OrderByDescending(x => x.Value.Sequence)
                                         .Skip(200))
            Operations.TryRemove(pair.Key, out _);
    }

    public static void RecordException(string source, Exception? exception, bool unhandled = false)
    {
        if (exception is null) return;
        Exceptions.Enqueue(new TelemetryException(DateTime.Now, source, exception.GetType().Name, exception.Message, unhandled));
        while (Exceptions.Count > 200) Exceptions.TryDequeue(out _);
    }

    public static void RegisterModule(string id, string state, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        Modules[id] = new TelemetryModule(id, state, DateTime.Now, detail);
    }

    public static ApplicationTelemetrySnapshot GetSnapshot()
    {
        var operations = Operations.Values.OrderByDescending(x => x.LastActivityLocal).ToArray();
        var now = DateTime.Now;
        var stalled = operations.Count(x => x.State is "RUNNING" && now - x.LastActivityLocal > TimeSpan.FromSeconds(15));
        var running = operations.Count(x => x.State == "RUNNING");
        var failed = operations.Count(x => x.State == "FAILED");
        var recentFailures = operations.Count(x => x.State == "FAILED" && now - x.LastActivityLocal <= TimeSpan.FromMinutes(10));
        var recentFailedUpdateChecks = operations.Count(x =>
            x.State == "FAILED" &&
            x.Type.Equals("UPDATE_CHECK", StringComparison.OrdinalIgnoreCase) &&
            now - x.LastActivityLocal <= TimeSpan.FromMinutes(10));
        var modules = Modules.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var exceptionArray = Exceptions.ToArray();
        var recentExceptions = exceptionArray.Count(x => now - x.Timestamp <= TimeSpan.FromMinutes(10));
        var unhandled = exceptionArray.Count(x => x.Unhandled && now - x.Timestamp <= TimeSpan.FromMinutes(10));

        return new ApplicationTelemetrySnapshot(
            StartedAt,
            LastHeartbeatLocal,
            now - LastHeartbeatLocal,
            running,
            failed,
            recentFailures,
            recentFailedUpdateChecks,
            stalled,
            recentExceptions,
            unhandled,
            modules,
            operations,
            exceptionArray);
    }
}

public sealed record TelemetryOperation(
    Guid Id,
    string Type,
    string Description,
    DateTime StartedLocal,
    DateTime LastActivityLocal,
    string State,
    double? Progress,
    string? Message,
    long Sequence);

public sealed record TelemetryModule(string Id, string State, DateTime UpdatedLocal, string? Detail);

public sealed record TelemetryException(DateTime Timestamp, string Source, string ExceptionType, string Message, bool Unhandled);

public sealed record ApplicationTelemetrySnapshot(
    DateTime StartedLocal,
    DateTime LastHeartbeatLocal,
    TimeSpan HeartbeatAge,
    int RunningOperations,
    int FailedOperations,
    int RecentFailedOperations,
    int RecentFailedUpdateChecks,
    int StalledOperations,
    int RecentExceptions,
    int RecentUnhandledExceptions,
    IReadOnlyList<TelemetryModule> Modules,
    IReadOnlyList<TelemetryOperation> Operations,
    IReadOnlyList<TelemetryException> Exceptions)
{
    public int LoadedModules => Modules.Count(x => x.State.Equals("Loaded", StringComparison.OrdinalIgnoreCase));
    public int FailedModules => Modules.Count(x => x.State.Equals("Failed", StringComparison.OrdinalIgnoreCase));
    public int MissingModules => Modules.Count(x => x.State.Equals("Missing", StringComparison.OrdinalIgnoreCase));
}
