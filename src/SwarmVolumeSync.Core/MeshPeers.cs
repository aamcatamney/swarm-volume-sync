namespace SwarmVolumeSync.Core;

/// <summary>
/// Resolves the full-mesh peer set. Peer agents are discovered via the swarm
/// overlay DNS name <c>tasks.&lt;service&gt;</c>, which returns every agent task
/// IP including this node's own. Push targets are every peer except self.
/// </summary>
public static class MeshPeers
{
    public static IReadOnlyList<string> PushTargets(IEnumerable<string> discovered, string self) =>
        discovered.Where(addr => addr != self).ToList();
}
