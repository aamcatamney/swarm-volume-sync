using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// The replication loop. Each cycle: read Docker facts, discover peers, decide
/// which selected in-scope volumes this node sources, and for each:
/// when newly promoted to source, run the pull-before-serve guard (ADR-0003);
/// otherwise version it by content, push the bytes (mirror-delete enabled, since
/// a confirmed source has won the version check), and propagate metadata.
/// </summary>
public sealed class SyncWorker(
    AgentConfig config,
    IVolumeMetadataStore store,
    PeerDiscovery discovery,
    RsyncRunner rsync,
    PeerMetadataClient peerClient,
    ILogger<SyncWorker> logger)
    : BackgroundService
{
    private readonly VolumeSelector _selector =
        VolumeSelector.For(config.SelectionMode, config.EnableLabelKey, config.IgnoreLabelKey);

    // Volumes this node sourced in the previous cycle, to detect promotion to source.
    private HashSet<string> _sourcedLastCycle = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "agent started: service={Service} mode={Mode} enableLabel={Enable} poll={Poll}s",
            config.ServiceName, config.SelectionMode, config.EnableLabelKey, config.PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "sync cycle error"); }

            try { await Task.Delay(config.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var docker = DockerFacts.Connect(config.DockerSocket);

        var nodeId = await docker.GetNodeIdAsync(ct);
        var volumes = await docker.ListVolumesAsync(ct);
        var mounted = await docker.ListLocallyMountedVolumesAsync(ct);
        var peers = await discovery.DiscoverPushTargetsAsync();

        var sourced = volumes
            .Where(VolumeScope.IsInScope)
            .Where(_selector.IsSelected)
            .Where(v => SourceDetector.RoleFor(v.Name, mounted) == VolumeRole.Source)
            .ToList();

        var sourcedNow = new HashSet<string>();

        foreach (var volume in sourced)
        {
            if (ct.IsCancellationRequested) break;
            sourcedNow.Add(volume.Name);

            var newlyPromoted = !_sourcedLastCycle.Contains(volume.Name);
            if (newlyPromoted && await TryHydrateAsync(volume.Name, peers, ct))
                continue; // hydrated this cycle; serve on a later cycle once established

            await VersionAndPushAsync(volume.Name, nodeId, peers, ct);
        }

        _sourcedLastCycle = sourcedNow;
    }

    /// <summary>
    /// Pull-before-serve guard. Returns true if this node hydrated from a peer
    /// (and therefore must not push this cycle).
    /// </summary>
    private async Task<bool> TryHydrateAsync(string volume, IReadOnlyList<string> peers, CancellationToken ct)
    {
        var dataPath = RsyncPlan.DataPath(volume);
        var checksum = Directory.Exists(dataPath) ? DirectoryChecksum.Compute(dataPath) : string.Empty;
        var trusted = PullBeforeServe.TrustedVersion(store.TryGet(volume), checksum);

        var peerVersions = new List<(string, VolumeVersion)>();
        foreach (var peer in peers)
        {
            var v = await peerClient.GetVersionAsync(peer, volume, ct);
            if (v is { } version) peerVersions.Add((peer, version));
        }

        var decision = PullBeforeServe.Decide(trusted, peerVersions);
        if (decision.Action != ServeAction.Hydrate)
            return false;

        logger.LogWarning(
            "pull-before-serve: hydrating '{Volume}' from {Peer} (local v{Local} < mesh v{Target}) before sourcing",
            volume, decision.HydrateFromPeer, trusted.Generation, decision.TargetVersion.Generation);

        var pullArgs = RsyncPlan.PullArgs(volume, decision.HydrateFromPeer!, new RsyncOptions(config.SshKeyPath, MirrorDelete: true));
        if (!await rsync.RunAsync($"hydrate {volume} from {decision.HydrateFromPeer}", pullArgs, ct))
            return true; // pull failed; still must not push our (untrusted) copy

        var adopted = await peerClient.GetMetadataAsync(decision.HydrateFromPeer!, volume, ct);
        if (adopted is not null)
            store.Save(adopted);

        return true;
    }

    private async Task VersionAndPushAsync(string volume, string nodeId, IReadOnlyList<string> peers, CancellationToken ct)
    {
        var dataPath = RsyncPlan.DataPath(volume);
        var checksum = Directory.Exists(dataPath) ? DirectoryChecksum.Compute(dataPath) : string.Empty;

        var meta = VersionTracker.RecordObservation(volume, store.TryGet(volume), checksum, nodeId, DateTimeOffset.UtcNow);
        store.Save(meta);

        // A confirmed source has won the version check, so mirror-delete is safe (ADR-0003).
        var options = new RsyncOptions(config.SshKeyPath, MirrorDelete: true);

        foreach (var peer in peers)
        {
            if (ct.IsCancellationRequested) break;
            var pushArgs = RsyncPlan.PushArgs(volume, peer, options);
            if (await rsync.RunAsync($"push {volume} to {peer}", pushArgs, ct))
                await peerClient.PushMetadataAsync(peer, meta, ct);
        }
    }
}
