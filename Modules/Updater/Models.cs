namespace GarlicSaveMgr.UpdaterModule;

public sealed record GitHubReleaseInfo(
    string Tag,
    string Name,
    string Body,
    string HtmlUrl,
    string AssetName,
    string AssetDownloadUrl,
    long AssetSize,
    string? AssetDigest);
