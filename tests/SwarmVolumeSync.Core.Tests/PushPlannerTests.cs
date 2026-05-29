using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class PushPlannerTests
{
    private static readonly RsyncOptions Opts = new(SshKeyPath: "/run/secrets/svs_ssh_key");

    private static DockerVolume Local(string name) =>
        new(name, "local", new Dictionary<string, string>());

    [Fact]
    public void Plans_one_push_per_peer_for_a_sourced_local_volume()
    {
        var volumes = new[] { Local("appdata") };
        var mounted = new[] { new MountedVolume("appdata", "local") };
        var peers = new[] { "10.0.0.2", "10.0.0.3" };

        var ops = PushPlanner.Plan(volumes, mounted, peers, Opts);

        Assert.Equal(2, ops.Count);
        Assert.All(ops, o => Assert.Equal("appdata", o.VolumeName));
        Assert.Equal(new[] { "10.0.0.2", "10.0.0.3" }, ops.Select(o => o.PeerAddress));
    }

    [Fact]
    public void Plans_no_pushes_for_a_volume_this_node_only_holds_as_replica()
    {
        var volumes = new[] { Local("appdata") };
        var mounted = Array.Empty<MountedVolume>(); // not mounted here => replica
        var peers = new[] { "10.0.0.2" };

        var ops = PushPlanner.Plan(volumes, mounted, peers, Opts);

        Assert.Empty(ops);
    }

    [Fact]
    public void Plans_no_pushes_for_an_out_of_scope_volume_even_if_mounted()
    {
        var volumes = new[] { new DockerVolume("nfsdata", "nfs", new Dictionary<string, string>()) };
        var mounted = new[] { new MountedVolume("nfsdata", "nfs") };
        var peers = new[] { "10.0.0.2" };

        var ops = PushPlanner.Plan(volumes, mounted, peers, Opts);

        Assert.Empty(ops);
    }
}
