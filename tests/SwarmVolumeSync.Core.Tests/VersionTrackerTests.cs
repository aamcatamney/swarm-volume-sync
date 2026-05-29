using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class VersionTrackerTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_observation_of_a_volume_starts_at_generation_one()
    {
        var meta = VersionTracker.RecordObservation("appdata", existing: null, checksum: "X", sourceNode: "node-a", at: At);

        Assert.Equal(1, meta.Version.Generation);
        Assert.Equal("appdata", meta.VolumeName);
        Assert.Equal("X", meta.Checksum);
        Assert.Equal("node-a", meta.SourceNode);
    }

    [Fact]
    public void Unchanged_checksum_does_not_bump_the_version()
    {
        var existing = new VolumeMetadata("appdata", new VolumeVersion(3), "X", At, "node-a");

        var meta = VersionTracker.RecordObservation("appdata", existing, checksum: "X", sourceNode: "node-a", at: At);

        Assert.Equal(3, meta.Version.Generation);
    }

    [Fact]
    public void Changed_checksum_bumps_the_version_and_records_the_new_checksum()
    {
        var existing = new VolumeMetadata("appdata", new VolumeVersion(3), "X", At, "node-a");

        var meta = VersionTracker.RecordObservation("appdata", existing, checksum: "Y", sourceNode: "node-b", at: At);

        Assert.Equal(4, meta.Version.Generation);
        Assert.Equal("Y", meta.Checksum);
        Assert.Equal("node-b", meta.SourceNode);
    }
}
