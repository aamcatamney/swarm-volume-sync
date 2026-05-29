using Docker.DotNet;
using Docker.DotNet.Models;
using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Reads ground-truth facts from the local Docker daemon: which named volumes
/// exist, and which of them are mounted by a running container on this node
/// (the signal that makes this node a volume's source — ADR-0002).
/// </summary>
public sealed class DockerFacts(IDockerClient client) : IAsyncDisposable
{
    public static DockerFacts Connect(string socketUri) =>
        new(new DockerClientConfiguration(new Uri(socketUri)).CreateClient());

    public async Task<IReadOnlyList<DockerVolume>> ListVolumesAsync(CancellationToken ct)
    {
        var response = await client.Volumes.ListAsync(ct);
        return response.Volumes
            .Select(v => new DockerVolume(
                v.Name,
                v.Driver,
                (IReadOnlyDictionary<string, string>)(v.Labels ?? new Dictionary<string, string>())))
            .ToList();
    }

    public async Task<IReadOnlyList<MountedVolume>> ListLocallyMountedVolumesAsync(CancellationToken ct)
    {
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = false }, ct); // running only

        return containers
            .SelectMany(c => c.Mounts ?? new List<MountPoint>())
            .Where(m => string.Equals(m.Type, "volume", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(m.Name))
            .Select(m => new MountedVolume(m.Name, string.IsNullOrEmpty(m.Driver) ? "local" : m.Driver))
            .DistinctBy(m => m.Name)
            .ToList();
    }

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
