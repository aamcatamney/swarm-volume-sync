using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// The replication loop. Each cycle: read Docker facts, discover peers, decide
/// which selected in-scope volumes this node sources, version them by content,
/// push the bytes via rsync, and propagate metadata to peers.
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
    private readonly RsyncOptions _rsyncOptions = new(config.SshKeyPath); // MirrorDelete off until #5
    private readonly VolumeSelector _selector =
        VolumeSelector.For(config.SelectionMode, config.EnableLabelKey, config.IgnoreLabelKey);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "agent started: service={Service} mode={Mode} enableLabel={Enable} poll={Poll}s",
            config.ServiceName, config.SelectionMode, config.EnableLabelKey, config.PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "sync cycle error");
            }

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

        var candidates = volumes.Where(VolumeScope.IsInScope).Where(_selector.IsSelected).ToList();

        // Version each sourced volume by its current content before pushing.
        var metas = new Dictionary<string, VolumeMetadata>();
        foreach (var volume in candidates)
        {
            if (SourceDetector.RoleFor(volume.Name, mounted) != VolumeRole.Source)
                continue;

            var dataPath = RsyncPlan.DataPath(volume.Name);
            var checksum = Directory.Exists(dataPath) ? DirectoryChecksum.Compute(dataPath) : string.Empty;
            var meta = VersionTracker.RecordObservation(
                volume.Name, store.TryGet(volume.Name), checksum, nodeId, DateTimeOffset.UtcNow);
            store.Save(meta);
            metas[volume.Name] = meta;
        }

        var ops = PushPlanner.Plan(candidates, mounted, peers, _rsyncOptions);
        if (ops.Count == 0) return;

        logger.LogInformation("sourcing {Volumes} volume(s); {Pushes} push(es) to {Peers} peer(s)",
            metas.Count, ops.Count, peers.Count);

        foreach (var op in ops)
        {
            if (ct.IsCancellationRequested) break;
            if (await rsync.RunAsync(op, ct) && metas.TryGetValue(op.VolumeName, out var meta))
                await peerClient.PushMetadataAsync(op.PeerAddress, meta, ct);
        }
    }
}
