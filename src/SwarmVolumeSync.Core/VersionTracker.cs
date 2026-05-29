namespace SwarmVolumeSync.Core;

/// <summary>
/// Decides a volume's metadata after observing its current content checksum.
/// The version is bumped only when the content actually changed, keeping the
/// generation monotonic and stable across no-op sync cycles.
/// </summary>
public static class VersionTracker
{
    public static VolumeMetadata RecordObservation(
        string volumeName,
        VolumeMetadata? existing,
        string checksum,
        string sourceNode,
        DateTimeOffset at)
    {
        if (existing is not null && existing.Checksum == checksum)
            return existing;

        var nextVersion = (existing?.Version ?? VolumeVersion.Initial).Next();
        return new VolumeMetadata(volumeName, nextVersion, checksum, at, sourceNode);
    }
}
