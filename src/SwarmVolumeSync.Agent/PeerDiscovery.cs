using System.Net;
using System.Net.Sockets;
using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Discovers peer agents via the swarm overlay DNS name <c>tasks.&lt;service&gt;</c>,
/// which resolves to every agent task IP including this node's own. Push targets
/// are every resolved address except this node's own overlay addresses.
/// </summary>
public sealed class PeerDiscovery(string tasksDnsName)
{
    public async Task<IReadOnlyList<string>> DiscoverPushTargetsAsync()
    {
        var discovered = await ResolveAsync(tasksDnsName);
        var ownAddresses = await ResolveOwnAddressesAsync();

        // Exclude every address this node owns, not just one (a task may bind several).
        var targets = discovered;
        foreach (var own in ownAddresses)
            targets = MeshPeers.PushTargets(targets, own);

        return targets;
    }

    private static async Task<IReadOnlyList<string>> ResolveAsync(string host)
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host);
            return addrs
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Distinct()
                .ToList();
        }
        catch (SocketException)
        {
            return Array.Empty<string>(); // name not yet resolvable (e.g. single-node, startup)
        }
    }

    private static Task<IReadOnlyList<string>> ResolveOwnAddressesAsync() =>
        ResolveAsync(Dns.GetHostName());
}
