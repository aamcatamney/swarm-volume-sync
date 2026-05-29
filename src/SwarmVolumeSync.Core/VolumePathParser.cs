namespace SwarmVolumeSync.Core;

/// <summary>
/// Maps a changed filesystem path under the Docker volumes root back to the
/// volume name it belongs to (the first path segment beneath the root), so a
/// single recursive watch over the root can attribute changes to volumes.
/// </summary>
public static class VolumePathParser
{
    public static string? VolumeNameFromChange(string volumesRoot, string changedPath)
    {
        var root = volumesRoot.Replace('\\', '/').TrimEnd('/');
        var path = changedPath.Replace('\\', '/');

        if (!path.StartsWith(root + "/", StringComparison.Ordinal))
            return null;

        var remainder = path[(root.Length + 1)..];
        var slash = remainder.IndexOf('/');
        var name = slash < 0 ? remainder : remainder[..slash];
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
