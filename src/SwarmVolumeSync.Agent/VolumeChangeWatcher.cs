using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Watches the Docker volumes root (inotify on Linux) and reports the name of
/// any volume whose data changed, so syncs can be change-triggered rather than
/// polled (CONTEXT.md, Sync trigger). A slow safety-net poll in the worker
/// backstops any events this misses.
/// </summary>
public sealed class VolumeChangeWatcher : IDisposable
{
    private readonly string _volumesRoot;
    private readonly FileSystemWatcher? _watcher;

    public event Action<string>? VolumeChanged;

    public VolumeChangeWatcher(string volumesRoot, ILogger logger)
    {
        _volumesRoot = volumesRoot;

        if (!Directory.Exists(volumesRoot))
        {
            logger.LogWarning("volumes root {Root} not present; relying on safety-net poll only", volumesRoot);
            return;
        }

        _watcher = new FileSystemWatcher(volumesRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                           | NotifyFilters.DirectoryName | NotifyFilters.Size,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
    }

    public void Start()
    {
        if (_watcher is not null) _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var volume = VolumePathParser.VolumeNameFromChange(_volumesRoot, e.FullPath);
        if (volume is not null)
            VolumeChanged?.Invoke(volume);
    }

    public void Dispose() => _watcher?.Dispose();
}
