using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class BackfillPlannerTests
{
    private static MeshVolume Vol(string name, params (string peer, long gen)[] holders) =>
        new(name, holders.Select(h => (h.peer, new VolumeVersion(h.gen))).ToList());

    [Fact]
    public void A_missing_volume_is_pulled_from_its_highest_versioned_holder()
    {
        var local = new Dictionary<string, VolumeVersion>(); // node holds nothing yet
        var mesh = new[] { Vol("appdata", ("10.0.0.2", 3), ("10.0.0.3", 7)) };

        var ops = BackfillPlanner.Plan(local, mesh);

        var op = Assert.Single(ops);
        Assert.Equal("appdata", op.Volume);
        Assert.Equal("10.0.0.3", op.FromPeer);
        Assert.Equal(7, op.Version.Generation);
    }

    [Fact]
    public void A_volume_already_at_the_mesh_high_water_mark_is_not_backfilled()
    {
        var local = new Dictionary<string, VolumeVersion> { ["appdata"] = new VolumeVersion(7) };
        var mesh = new[] { Vol("appdata", ("10.0.0.2", 7)) };

        Assert.Empty(BackfillPlanner.Plan(local, mesh));
    }

    [Fact]
    public void A_stale_local_copy_is_backfilled_to_the_higher_mesh_version()
    {
        var local = new Dictionary<string, VolumeVersion> { ["appdata"] = new VolumeVersion(2) };
        var mesh = new[] { Vol("appdata", ("10.0.0.2", 9)) };

        var op = Assert.Single(BackfillPlanner.Plan(local, mesh));
        Assert.Equal(9, op.Version.Generation);
    }
}
