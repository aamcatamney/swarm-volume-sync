using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Aggregates replication health across the mesh: for each volume this node
/// knows, counts how many nodes hold a copy (self + peers reporting a version)
/// against the total node count, producing <see cref="VolumeStatus"/> for the
/// control API, metrics, and under-replication warnings (CONTEXT.md, Coverage).
/// </summary>
public sealed class MeshStatusService(
    IVolumeMetadataStore store,
    PeerDiscovery discovery,
    PeerMetadataClient peerClient)
{
    public async Task<IReadOnlyList<VolumeStatus>> BuildAsync(CancellationToken ct)
    {
        var peers = await discovery.DiscoverPushTargetsAsync();
        var totalNodes = peers.Count + 1; // peers + self
        var now = DateTimeOffset.UtcNow;

        var statuses = new List<VolumeStatus>();
        foreach (var meta in store.All())
        {
            var holders = 1; // self holds it
            foreach (var peer in peers)
            {
                if (await peerClient.GetVersionAsync(peer, meta.VolumeName, ct) is not null)
                    holders++;
            }

            statuses.Add(new VolumeStatus(
                Volume: meta.VolumeName,
                Version: meta.Version.Generation,
                Holders: holders,
                TotalNodes: totalNodes,
                SyncLagSeconds: Math.Max(0, (now - meta.LastSyncedAt).TotalSeconds),
                LastSyncUnix: meta.LastSyncedAt.ToUnixTimeSeconds()));
        }

        return statuses;
    }
}
