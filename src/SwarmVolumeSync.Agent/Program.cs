using SwarmVolumeSync.Agent;
using SwarmVolumeSync.Core;

var config = AgentConfig.FromEnvironment();
var rsyncOptions = new RsyncOptions(config.SshKeyPath); // MirrorDelete stays off until #5 (pull-before-serve)
var selector = VolumeSelector.For(config.SelectionMode, config.EnableLabelKey, config.IgnoreLabelKey);
var discovery = new PeerDiscovery(config.TasksDnsName);
var rsync = new RsyncRunner();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

Console.WriteLine($"swarm-volume-sync agent starting. service={config.ServiceName} " +
                  $"mode={config.SelectionMode} enableLabel={config.EnableLabelKey} " +
                  $"ignoreLabel={config.IgnoreLabelKey} poll={config.PollInterval.TotalSeconds}s");

while (!cts.IsCancellationRequested)
{
    try
    {
        await using var docker = DockerFacts.Connect(config.DockerSocket);

        var volumes = await docker.ListVolumesAsync(cts.Token);
        var mounted = await docker.ListLocallyMountedVolumesAsync(cts.Token);
        var peers = await discovery.DiscoverPushTargetsAsync();

        var candidates = volumes
            .Where(VolumeScope.IsInScope)
            .Where(selector.IsSelected)
            .ToList();

        var ops = PushPlanner.Plan(candidates, mounted, peers, rsyncOptions);

        if (ops.Count > 0)
            Console.WriteLine($"sourcing {ops.Select(o => o.VolumeName).Distinct().Count()} volume(s); " +
                              $"{ops.Count} push(es) to {peers.Count} peer(s)");

        foreach (var op in ops)
        {
            if (cts.IsCancellationRequested) break;
            await rsync.RunAsync(op, cts.Token);
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"sync cycle error: {ex.Message}");
    }

    try { await Task.Delay(config.PollInterval, cts.Token); }
    catch (OperationCanceledException) { break; }
}

Console.WriteLine("swarm-volume-sync agent stopped.");
