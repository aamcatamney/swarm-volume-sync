using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class VolumeVersionTests
{
    [Fact]
    public void A_fresh_volume_starts_at_generation_zero()
    {
        Assert.Equal(0, VolumeVersion.Initial.Generation);
    }

    [Fact]
    public void Next_increments_the_generation_monotonically()
    {
        var v = VolumeVersion.Initial.Next().Next();

        Assert.Equal(2, v.Generation);
    }

    [Fact]
    public void A_higher_generation_compares_greater()
    {
        Assert.True(new VolumeVersion(5) > new VolumeVersion(4));
        Assert.False(new VolumeVersion(4) > new VolumeVersion(5));
    }
}
