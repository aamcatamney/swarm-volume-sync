using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwarmVolumeSync.Core;

/// <summary>
/// Per-node bookkeeping for one replicated volume (see CONTEXT.md, Volume
/// metadata). Persisted in an agent-owned directory <b>outside</b> the volume so
/// user volume bytes stay pristine.
/// </summary>
public sealed record VolumeMetadata(
    string VolumeName,
    VolumeVersion Version,
    string Checksum,
    DateTimeOffset LastSyncedAt,
    string SourceNode);

public static class VolumeMetadataSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string ToJson(VolumeMetadata metadata) => JsonSerializer.Serialize(metadata, Options);

    public static VolumeMetadata FromJson(string json) =>
        JsonSerializer.Deserialize<VolumeMetadata>(json, Options)
        ?? throw new FormatException("Volume metadata JSON deserialized to null.");
}
