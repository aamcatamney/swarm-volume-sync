namespace SwarmVolumeSync.Core;

/// <summary>
/// A node's role for a given replicated volume.
/// The <see cref="Source"/> pushes; a <see cref="Replica"/> only receives.
/// </summary>
public enum VolumeRole
{
    Source,
    Replica,
}

/// <summary>
/// A volume currently mounted by a running container on this node, as reported
/// by the local Docker daemon.
/// </summary>
public sealed record MountedVolume(string Name, string Driver);

/// <summary>
/// Decides whether this node is the <see cref="VolumeRole.Source"/> for a volume.
/// The source is, by definition, the node whose running container mounts it
/// (see ADR-0002, mount-state drives sync direction).
/// </summary>
public static class SourceDetector
{
    public static VolumeRole RoleFor(string volumeName, IEnumerable<MountedVolume> locallyMountedVolumes)
    {
        var isMountedLocally = locallyMountedVolumes.Any(m => m.Name == volumeName);
        return isMountedLocally ? VolumeRole.Source : VolumeRole.Replica;
    }
}
