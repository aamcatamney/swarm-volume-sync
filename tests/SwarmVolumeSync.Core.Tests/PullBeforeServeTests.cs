using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class PullBeforeServeTests
{
    private static (string, VolumeVersion) Peer(string name, long gen) => (name, new VolumeVersion(gen));

    [Fact]
    public void With_no_peers_a_source_serves()
    {
        var d = PullBeforeServe.Decide(localTrusted: new VolumeVersion(3), peerVersions: []);

        Assert.Equal(ServeAction.Serve, d.Action);
    }

    [Fact]
    public void An_empty_source_hydrates_from_a_peer_holding_a_higher_version()
    {
        // The catastrophic case: this node became source with version 0 (empty),
        // but a peer holds real data at version 5.
        var d = PullBeforeServe.Decide(localTrusted: VolumeVersion.Initial, peerVersions: [Peer("10.0.0.2", 5)]);

        Assert.Equal(ServeAction.Hydrate, d.Action);
        Assert.Equal("10.0.0.2", d.HydrateFromPeer);
        Assert.Equal(5, d.TargetVersion.Generation);
    }

    [Fact]
    public void A_source_at_the_mesh_high_water_mark_serves()
    {
        var d = PullBeforeServe.Decide(new VolumeVersion(5), [Peer("10.0.0.2", 5)]);

        Assert.Equal(ServeAction.Serve, d.Action);
    }

    [Fact]
    public void A_source_ahead_of_all_peers_serves()
    {
        var d = PullBeforeServe.Decide(new VolumeVersion(5), [Peer("10.0.0.2", 3)]);

        Assert.Equal(ServeAction.Serve, d.Action);
    }

    [Fact]
    public void Hydration_pulls_from_the_highest_versioned_peer()
    {
        var d = PullBeforeServe.Decide(VolumeVersion.Initial, [Peer("10.0.0.2", 3), Peer("10.0.0.3", 7)]);

        Assert.Equal(ServeAction.Hydrate, d.Action);
        Assert.Equal("10.0.0.3", d.HydrateFromPeer);
    }
}

public class TrustedVersionTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_local_metadata_means_no_trusted_version()
    {
        Assert.Equal(VolumeVersion.Initial, PullBeforeServe.TrustedVersion(stored: null, currentChecksum: "X"));
    }

    [Fact]
    public void Metadata_matching_current_content_is_trusted()
    {
        var stored = new VolumeMetadata("appdata", new VolumeVersion(5), "X", At, "node-a");

        Assert.Equal(new VolumeVersion(5), PullBeforeServe.TrustedVersion(stored, currentChecksum: "X"));
    }

    [Fact]
    public void Metadata_not_matching_current_content_is_not_trusted()
    {
        // e.g. metadata says v5/checksum X, but the volume is now empty (checksum Y).
        var stored = new VolumeMetadata("appdata", new VolumeVersion(5), "X", At, "node-a");

        Assert.Equal(VolumeVersion.Initial, PullBeforeServe.TrustedVersion(stored, currentChecksum: "Y"));
    }
}
