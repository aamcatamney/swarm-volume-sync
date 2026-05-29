namespace SwarmVolumeSync.Core;

/// <summary>
/// Decides which in-scope volumes are candidates for replication. The v1 tracer
/// only supports labelled (opt-in) selection; selection modes (<c>all</c> /
/// <c>ignore</c> override) are layered on in #2 (see CONTEXT.md, Selection mode).
/// </summary>
public sealed class VolumeSelector
{
    private readonly Func<DockerVolume, bool> _predicate;

    private VolumeSelector(Func<DockerVolume, bool> predicate) => _predicate = predicate;

    /// <summary>Opt-in selection: only volumes carrying the configured enable label.</summary>
    public static VolumeSelector Labelled(string enableLabelKey) =>
        new(v => v.Labels.ContainsKey(enableLabelKey));

    public bool IsSelected(DockerVolume volume) => _predicate(volume);
}
