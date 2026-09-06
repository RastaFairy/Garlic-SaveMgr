namespace GarlicSaveMgr.Services;

/// <summary>
/// Single source of truth for the logical size of a real local file.
/// Metadata stored in sidecars/snapshots is never used as the source of truth.
/// </summary>
public static class SizeResolutionService
{
    public readonly record struct ResolvedSize(long Bytes, string Source, bool IsVerified);

    public static ResolvedSize ResolveLocalFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ResolvedSize(0, "missing", false);

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return new ResolvedSize(0, "missing", false);

            // FileInfo.Length is the actual logical length of the physical file.
            // This is intentionally independent from JSON metadata and from
            // filesystem allocation APIs: SALUD is reporting the file size here,
            // not its allocation unit consumption.
            return new ResolvedSize(Math.Max(0, info.Length), "FileInfo.Length", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new ResolvedSize(0, "unavailable", false);
        }
    }

    public static long GetRealFileSize(string path) => Math.Max(0, ResolveLocalFile(path).Bytes);
}
