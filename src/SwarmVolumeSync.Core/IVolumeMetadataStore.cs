namespace SwarmVolumeSync.Core;

/// <summary>
/// Persists <see cref="VolumeMetadata"/> per node, outside the volume's data.
/// Implementations live in the agent (filesystem); the version it returns backs
/// the control API and the pull-before-serve version check (ADR-0003).
/// </summary>
public interface IVolumeMetadataStore
{
    VolumeMetadata? TryGet(string volumeName);

    void Save(VolumeMetadata metadata);

    IReadOnlyList<VolumeMetadata> All();
}
