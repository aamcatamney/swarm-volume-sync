using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class MeshPeersTests
{
    [Fact]
    public void Push_targets_exclude_this_nodes_own_address()
    {
        var discovered = new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3" };

        var targets = MeshPeers.PushTargets(discovered, self: "10.0.0.2");

        Assert.Equal(new[] { "10.0.0.1", "10.0.0.3" }, targets);
    }

    [Fact]
    public void A_single_node_swarm_has_no_push_targets()
    {
        var discovered = new[] { "10.0.0.1" };

        var targets = MeshPeers.PushTargets(discovered, self: "10.0.0.1");

        Assert.Empty(targets);
    }
}
