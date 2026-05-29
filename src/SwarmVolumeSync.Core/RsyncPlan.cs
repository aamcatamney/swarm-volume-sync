namespace SwarmVolumeSync.Core;

/// <summary>
/// Transport options for an rsync-over-SSH push. The SSH key is delivered to
/// every agent as a swarm secret (see CONTEXT.md, Agent).
/// </summary>
public sealed record RsyncOptions(string SshKeyPath, bool MirrorDelete = false, int? BandwidthLimitKb = null);

/// <summary>
/// Builds the <c>rsync</c> argument vector for pushing a volume's data to a peer.
/// The C# agent orchestrates; <c>rsync</c> performs the delta transfer
/// (see ADR-0001 / CONTEXT.md). <c>--delete</c> (mirror) is opt-in and must only
/// be enabled by a source that has won the version check (ADR-0003); the v1
/// tracer never sets it.
/// </summary>
public static class RsyncPlan
{
    public static string DataPath(string volumeName) => $"/var/lib/docker/volumes/{volumeName}/_data/";

    public static IReadOnlyList<string> PushArgs(string volumeName, string peerAddress, RsyncOptions options)
    {
        var dataPath = DataPath(volumeName);
        return BuildArgs(options, source: dataPath, destination: $"{peerAddress}:{dataPath}");
    }

    /// <summary>
    /// Pull (hydrate) args: copy a peer's volume data <em>into</em> this node's
    /// volume. Used by pull-before-serve (ADR-0003) before a source may push.
    /// </summary>
    public static IReadOnlyList<string> PullArgs(string volumeName, string peerAddress, RsyncOptions options)
    {
        var dataPath = DataPath(volumeName);
        return BuildArgs(options, source: $"{peerAddress}:{dataPath}", destination: dataPath);
    }

    private static List<string> BuildArgs(RsyncOptions options, string source, string destination)
    {
        var args = new List<string> { "-a" };

        if (options.MirrorDelete)
            args.Add("--delete");

        if (options.BandwidthLimitKb is { } limit)
            args.Add($"--bwlimit={limit}");

        args.Add("-e");
        args.Add($"ssh -i {options.SshKeyPath} -o StrictHostKeyChecking=no");

        args.Add(source);
        args.Add(destination);

        return args;
    }
}
