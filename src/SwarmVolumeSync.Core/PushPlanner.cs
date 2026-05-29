namespace SwarmVolumeSync.Core;

/// <summary>
/// A single planned rsync push of one volume to one peer.
/// </summary>
public sealed record PushOperation(string VolumeName, string PeerAddress, IReadOnlyList<string> RsyncArgs);

/// <summary>
/// The agent's decision core for the push side: given what the local Docker
/// daemon reports and the discovered peer set, decide what to push where.
/// A volume is pushed only if it is in scope (named <c>local</c> driver) and
/// this node is its <see cref="VolumeRole.Source"/> (ADR-0002).
/// </summary>
public static class PushPlanner
{
    public static IReadOnlyList<PushOperation> Plan(
        IEnumerable<DockerVolume> localVolumes,
        IEnumerable<MountedVolume> locallyMounted,
        IEnumerable<string> peers,
        RsyncOptions options)
    {
        var mounted = locallyMounted.ToList();
        var peerList = peers.ToList();
        var ops = new List<PushOperation>();

        foreach (var volume in localVolumes)
        {
            if (!VolumeScope.IsInScope(volume))
                continue;

            if (SourceDetector.RoleFor(volume.Name, mounted) != VolumeRole.Source)
                continue;

            foreach (var peer in peerList)
                ops.Add(new PushOperation(volume.Name, peer, RsyncPlan.PushArgs(volume.Name, peer, options)));
        }

        return ops;
    }
}
