using System.Net;
using System.Net.Http.Json;
using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Talks to peer agents' control APIs over the overlay network: reads the
/// version a peer holds for a volume, and propagates this node's metadata to a
/// peer after a successful data push (so every copy carries its version —
/// CONTEXT.md, Volume version / Control API).
/// </summary>
public sealed class PeerMetadataClient(HttpClient http, int controlApiPort)
{
    private Uri Base(string peer) => new($"http://{peer}:{controlApiPort}");

    public async Task<VolumeVersion?> GetVersionAsync(string peer, string volume, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync(new Uri(Base(peer), $"/volumes/{volume}/version"), ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<VersionResponse>(ct);
            return body is null ? null : new VolumeVersion(body.Generation);
        }
        catch (HttpRequestException)
        {
            return null; // peer not reachable / not ready
        }
    }

    public async Task<VolumeMetadata?> GetMetadataAsync(string peer, string volume, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync(new Uri(Base(peer), $"/volumes/{volume}"), ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<VolumeMetadata>(ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<VolumeMetadata>> GetVolumesAsync(string peer, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync(new Uri(Base(peer), "/volumes"), ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<List<VolumeMetadata>>(ct) ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<string>> GetSourcesAsync(string peer, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync(new Uri(Base(peer), "/sources"), ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<List<string>>(ct) ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task PushMetadataAsync(string peer, VolumeMetadata metadata, CancellationToken ct)
    {
        try
        {
            await http.PostAsJsonAsync(
                new Uri(Base(peer), $"/volumes/{metadata.VolumeName}/metadata"), metadata, ct);
        }
        catch (HttpRequestException)
        {
            // best-effort; the next cycle re-propagates
        }
    }

    public sealed record VersionResponse(long Generation);
}
