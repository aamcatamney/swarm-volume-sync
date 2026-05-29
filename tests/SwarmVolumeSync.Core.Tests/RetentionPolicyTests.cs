using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class RetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static VolumeMetadata Meta(DateTimeOffset? orphanedAt = null) =>
        new("appdata", new VolumeVersion(5), "X", Now.AddDays(-30), "node-a", orphanedAt);

    [Fact]
    public void A_volume_still_sourced_somewhere_is_active_and_its_orphan_clock_is_cleared()
    {
        var decision = RetentionPolicy.Evaluate(Meta(orphanedAt: Now.AddDays(-3)), sourcedSomewhere: true, Now, Retention);

        Assert.Equal(ReclaimAction.Active, decision.Action);
        Assert.Null(decision.UpdatedMetadata!.OrphanedAt);
    }

    [Fact]
    public void A_volume_sourced_nowhere_starts_the_orphan_clock()
    {
        var decision = RetentionPolicy.Evaluate(Meta(orphanedAt: null), sourcedSomewhere: false, Now, Retention);

        Assert.Equal(ReclaimAction.MarkOrphaned, decision.Action);
        Assert.Equal(Now, decision.UpdatedMetadata!.OrphanedAt);
    }

    [Fact]
    public void An_orphan_within_the_retention_window_waits()
    {
        var decision = RetentionPolicy.Evaluate(Meta(orphanedAt: Now.AddDays(-3)), sourcedSomewhere: false, Now, Retention);

        Assert.Equal(ReclaimAction.Wait, decision.Action);
    }

    [Fact]
    public void An_orphan_past_the_retention_window_is_reclaimed()
    {
        var decision = RetentionPolicy.Evaluate(Meta(orphanedAt: Now.AddDays(-8)), sourcedSomewhere: false, Now, Retention);

        Assert.Equal(ReclaimAction.Reclaim, decision.Action);
    }
}

public class SplitBrainTests
{
    [Fact]
    public void Same_version_with_diverging_content_is_split_brain()
    {
        Assert.True(SplitBrain.IsConflict(
            localVersion: new VolumeVersion(5), localChecksum: "A",
            peerVersion: new VolumeVersion(5), peerChecksum: "B"));
    }

    [Fact]
    public void Same_version_with_identical_content_is_not_a_conflict()
    {
        Assert.False(SplitBrain.IsConflict(new VolumeVersion(5), "A", new VolumeVersion(5), "A"));
    }

    [Fact]
    public void Different_versions_are_not_split_brain_just_normal_lag()
    {
        Assert.False(SplitBrain.IsConflict(new VolumeVersion(5), "A", new VolumeVersion(6), "B"));
    }
}
