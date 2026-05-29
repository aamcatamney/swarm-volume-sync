namespace SwarmVolumeSync.Core;

/// <summary>
/// A Docker volume as reported by the local Docker daemon.
/// </summary>
public sealed record DockerVolume(string Name, string Driver, IReadOnlyDictionary<string, string> Labels);

/// <summary>
/// The hard scope boundary: this service only ever touches named volumes using
/// the <c>local</c> driver (see CONTEXT.md). Bind mounts, tmpfs and networked
/// volume drivers are excluded regardless of labels or selection mode.
/// </summary>
public static class VolumeScope
{
    public const string LocalDriver = "local";

    public static bool IsInScope(DockerVolume volume) => volume.Driver == LocalDriver;
}
