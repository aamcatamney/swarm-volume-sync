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
    MeshStatusService statusService,
    SourceRegistry sourceRegistry,
    ILogger<SyncWorker> logger)
    : BackgroundService
{
    private readonly VolumeSelector _selector =
        VolumeSelector.For(config.SelectionMode, config.EnableLabelKey, config.IgnoreLabelKey);

    private readonly DebounceWindow _debounce = new(config.DebounceInterval);
    private volatile bool _pendingChanges;

    // Volumes this node sourced in the previous cycle, to detect promotion to source.
    private HashSet<string> _sourcedLastCycle = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "agent started: service={Service} mode={Mode} enableLabel={Enable} debounce={Debounce}s safetyPoll={Safety}s",
            config.ServiceName, config.SelectionMode, config.EnableLabelKey,
            config.DebounceInterval.TotalSeconds, config.SafetyPollInterval.TotalSeconds);

        using var watcher = new VolumeChangeWatcher(config.VolumesRoot, logger);
        watcher.VolumeChanged += OnVolumeChanged;
        watcher.Start();

        var lastFullSync = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) { break; }

            var now = DateTimeOffset.UtcNow;
            var debounced = _pendingChanges && _debounce.HasSettled(now);
            var safetyDue = now - lastFullSync >= config.SafetyPollInterval;
            if (!debounced && !safetyDue)
                continue;

            _pendingChanges = false;
            _debounce.Reset();
            lastFullSync = now;

            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "sync cycle error"); }
        }
    }

    private void OnVolumeChanged(string volume)
    {
        _pendingChanges = true;
        _debounce.RecordActivity(DateTimeOffset.UtcNow);
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
        sourceRegistry.Publish(sourcedNow);

        await ReclaimOrphansAsync(sourcedNow, peers, ct);
        await CheckSplitBrainAsync(sourcedNow, peers, ct);
        await WarnOnUnderReplicationAsync(ct);
    }

    private async Task ReclaimOrphansAsync(HashSet<string> selfSourced, IReadOnlyList<string> peers, CancellationToken ct)
    {
        var sourcedSomewhere = new HashSet<string>(selfSourced);
        foreach (var peer in peers)
            sourcedSomewhere.UnionWith(await peerClient.GetSourcesAsync(peer, ct));

        var now = DateTimeOffset.UtcNow;
        foreach (var meta in store.All())
        {
            var decision = RetentionPolicy.Evaluate(
                meta, sourcedSomewhere.Contains(meta.VolumeName), now, config.RetentionWindow);

            switch (decision.Action)
            {
                case ReclaimAction.Active:
                    if (meta.OrphanedAt is not null) store.Save(decision.UpdatedMetadata!);
                    break;
                case ReclaimAction.MarkOrphaned:
                    logger.LogInformation(
                        "orphaned '{Volume}' (sourced nowhere in mesh); reclaim in {Days}d unless a source returns",
                        meta.VolumeName, config.RetentionWindow.TotalDays);
                    store.Save(decision.UpdatedMetadata!);
                    break;
                case ReclaimAction.Reclaim:
                    logger.LogWarning("reclaiming '{Volume}': retention window elapsed with no source", meta.VolumeName);
                    ReclaimVolumeData(meta.VolumeName);
                    store.Delete(meta.VolumeName);
                    break;
                case ReclaimAction.Wait:
                default:
                    break;
            }
        }
    }

    private void ReclaimVolumeData(string volume)
    {
        var dataPath = RsyncPlan.DataPath(volume).TrimEnd('/');
        if (!Directory.Exists(dataPath)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(dataPath))
        {
            if (File.Exists(entry)) File.Delete(entry);
            else Directory.Delete(entry, recursive: true);
        }
    }

    private async Task CheckSplitBrainAsync(HashSet<string> selfSourced, IReadOnlyList<string> peers, CancellationToken ct)
    {
        foreach (var volume in selfSourced)
        {
            var local = store.TryGet(volume);
            if (local is null) continue;

            foreach (var peer in peers)
            {
                var peerMeta = await peerClient.GetMetadataAsync(peer, volume, ct);
                if (peerMeta is not null &&
                    SplitBrain.IsConflict(local.Version, local.Checksum, peerMeta.Version, peerMeta.Checksum))
                {
                    logger.LogWarning(
                        "split-brain on '{Volume}': local v{LocalV}/{LocalCk} vs {Peer} v{PeerV}/{PeerCk}; " +
                        "higher version wins on heal",
                        volume, local.Version.Generation, local.Checksum[..Math.Min(8, local.Checksum.Length)],
                        peer, peerMeta.Version.Generation, peerMeta.Checksum[..Math.Min(8, peerMeta.Checksum.Length)]);
                }
            }
        }
    }

    private async Task WarnOnUnderReplicationAsync(CancellationToken ct)
    {
        foreach (var status in await statusService.BuildAsync(ct))
        {
            if (status.IsUnderReplicated)
                logger.LogWarning(
                    "under-replicated: '{Volume}' v{Version} on {Holders}/{Total} nodes (coverage {Coverage:P0}); " +
                    "failover onto a node without a copy may fail",
                    status.Volume, status.Version, status.Holders, status.TotalNodes, status.Coverage);
        }
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
