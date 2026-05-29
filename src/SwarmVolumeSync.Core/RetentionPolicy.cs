namespace SwarmVolumeSync.Core;

/// <summary>What to do with a stored volume copy this cycle.</summary>
public enum ReclaimAction
{
    /// <summary>Still sourced somewhere in the mesh; keep it and clear any orphan clock.</summary>
    Active,

    /// <summary>No longer sourced anywhere; begin the retention countdown.</summary>
    MarkOrphaned,

    /// <summary>Orphaned but still within the retention window; keep waiting.</summary>
    Wait,

    /// <summary>Orphaned beyond the retention window; reclaim the bytes.</summary>
    Reclaim,
}

public sealed record ReclaimDecision(ReclaimAction Action, VolumeMetadata? UpdatedMetadata);

/// <summary>
/// Decides when a volume copy may be reclaimed (CONTEXT.md, Retention-based
/// reclaim). The service never mesh-deletes in response to a single
/// <c>docker volume rm</c> or a label change; instead, a copy that is sourced
/// <b>nowhere</b> in the mesh starts an orphan clock and is only reclaimed after
/// the retention window elapses with no source seen. Deletion is the only
/// irreversible op, so the policy biases hard toward keeping bytes.
/// </summary>
public static class RetentionPolicy
{
    public static ReclaimDecision Evaluate(
        VolumeMetadata metadata,
        bool sourcedSomewhere,
        DateTimeOffset now,
        TimeSpan retention)
    {
        if (sourcedSomewhere)
            return new ReclaimDecision(ReclaimAction.Active, metadata with { OrphanedAt = null });

        if (metadata.OrphanedAt is not { } orphanedAt)
            return new ReclaimDecision(ReclaimAction.MarkOrphaned, metadata with { OrphanedAt = now });

        return now - orphanedAt >= retention
            ? new ReclaimDecision(ReclaimAction.Reclaim, metadata)
            : new ReclaimDecision(ReclaimAction.Wait, metadata);
    }
}

/// <summary>
/// Detects split-brain: two nodes that independently sourced the same volume at
/// the same generation but produced diverging content. The mesh resolves this
/// by higher-version-wins on heal; this just surfaces the loud warning the
/// situation warrants (CONTEXT.md, Split-brain).
/// </summary>
public static class SplitBrain
{
    public static bool IsConflict(
        VolumeVersion localVersion, string localChecksum,
        VolumeVersion peerVersion, string peerChecksum) =>
        localVersion.Generation == peerVersion.Generation && localChecksum != peerChecksum;
}
