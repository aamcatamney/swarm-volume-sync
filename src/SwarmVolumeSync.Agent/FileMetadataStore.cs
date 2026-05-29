using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Stores one <c>&lt;volume&gt;.meta</c> JSON file per volume in an agent-owned
/// directory, never inside the volume's <c>_data</c> (CONTEXT.md, Volume metadata).
/// </summary>
public sealed class FileMetadataStore : IVolumeMetadataStore
{
    private const string Extension = ".meta";
    private readonly string _directory;
    private readonly object _gate = new();

    public FileMetadataStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    private string PathFor(string volumeName) => Path.Combine(_directory, volumeName + Extension);

    public VolumeMetadata? TryGet(string volumeName)
    {
        var path = PathFor(volumeName);
        lock (_gate)
        {
            if (!File.Exists(path)) return null;
            return VolumeMetadataSerializer.FromJson(File.ReadAllText(path));
        }
    }

    public void Save(VolumeMetadata metadata)
    {
        var path = PathFor(metadata.VolumeName);
        var json = VolumeMetadataSerializer.ToJson(metadata);
        lock (_gate)
        {
            // Write-then-rename for atomicity.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
    }

    public IReadOnlyList<VolumeMetadata> All()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory)) return Array.Empty<VolumeMetadata>();
            return Directory.EnumerateFiles(_directory, "*" + Extension)
                .Select(f => VolumeMetadataSerializer.FromJson(File.ReadAllText(f)))
                .ToList();
        }
    }
}
