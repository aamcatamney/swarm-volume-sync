namespace SwarmVolumeSync.Core;

/// <summary>
/// Transport options for an rsync-over-SSH push. The SSH key is delivered to
/// every agent as a swarm secret (see CONTEXT.md, Agent).
/// </summary>
public sealed record RsyncOptions(string SshKeyPath, bool MirrorDelete = false);

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
        var args = new List<string> { "-a" };

        if (options.MirrorDelete)
            args.Add("--delete");

        args.Add("-e");
        args.Add($"ssh -i {options.SshKeyPath} -o StrictHostKeyChecking=no");

        var dataPath = DataPath(volumeName);
        args.Add(dataPath);
        args.Add($"{peerAddress}:{dataPath}");

        return args;
    }
}
