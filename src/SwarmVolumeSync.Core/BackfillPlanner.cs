namespace SwarmVolumeSync.Core;

/// <summary>A volume as seen across the mesh: the versions its holders advertise.</summary>
public sealed record MeshVolume(string Name, IReadOnlyList<(string Peer, VolumeVersion Version)> Holders);

/// <summary>One volume to pull during backfill, from the holder that has the most recent copy.</summary>
public sealed record BackfillOperation(string Volume, string FromPeer, VolumeVersion Version);

/// <summary>
/// Plans backfill for a node that needs to (re)reach full coverage — e.g. a
/// freshly-joined node (CONTEXT.md, Backfill). For each mesh volume the node is
/// missing or behind on, it pulls from the highest-versioned holder. This reuses
/// the same highest-version-wins decision as pull-before-serve (ADR-0003), so a
/// node never backfills a copy older than what it already holds.
/// </summary>
public static class BackfillPlanner
{
    public static IReadOnlyList<BackfillOperation> Plan(
        IReadOnlyDictionary<string, VolumeVersion> localVersions,
        IReadOnlyList<MeshVolume> mesh)
    {
        var ops = new List<BackfillOperation>();

        foreach (var volume in mesh)
        {
            var local = localVersions.TryGetValue(volume.Name, out var v) ? v : VolumeVersion.Initial;
            var decision = PullBeforeServe.Decide(local, volume.Holders);

            if (decision.Action == ServeAction.Hydrate)
                ops.Add(new BackfillOperation(volume.Name, decision.HydrateFromPeer!, decision.TargetVersion));
        }

        return ops;
    }
}
