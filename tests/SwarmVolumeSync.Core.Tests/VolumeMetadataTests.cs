using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class VolumeMetadataTests
{
    [Fact]
    public void Metadata_round_trips_through_json()
    {
        var original = new VolumeMetadata(
            VolumeName: "appdata",
            Version: new VolumeVersion(7),
            Checksum: "abc123",
            LastSyncedAt: new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero),
            SourceNode: "node-a");

        var restored = VolumeMetadataSerializer.FromJson(VolumeMetadataSerializer.ToJson(original));

        Assert.Equal(original, restored);
    }
}
