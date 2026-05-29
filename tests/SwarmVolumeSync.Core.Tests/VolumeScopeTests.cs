using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class VolumeScopeTests
{
    private static DockerVolume Vol(string name, string driver) =>
        new(name, driver, new Dictionary<string, string>());

    [Fact]
    public void Local_driver_volume_is_in_scope()
    {
        Assert.True(VolumeScope.IsInScope(Vol("appdata", "local")));
    }

    [Fact]
    public void Non_local_driver_volume_is_out_of_scope()
    {
        Assert.False(VolumeScope.IsInScope(Vol("nfsdata", "nfs")));
    }
}
