using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class SourceDetectorTests
{
    [Fact]
    public void Node_mounting_a_volume_via_a_running_container_is_its_source()
    {
        var locallyMounted = new[] { new MountedVolume("appdata", "local") };

        Assert.Equal(VolumeRole.Source, SourceDetector.RoleFor("appdata", locallyMounted));
    }

    [Fact]
    public void Node_not_mounting_a_volume_is_a_replica_for_it()
    {
        var locallyMounted = new[] { new MountedVolume("other", "local") };

        Assert.Equal(VolumeRole.Replica, SourceDetector.RoleFor("appdata", locallyMounted));
    }
}
