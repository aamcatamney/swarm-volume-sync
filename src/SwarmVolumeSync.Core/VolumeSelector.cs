namespace SwarmVolumeSync.Core;

/// <summary>
/// Which in-scope volumes are candidates for replication (see CONTEXT.md,
/// Selection mode).
/// </summary>
public enum SelectionMode
{
    /// <summary>Opt-in: only volumes carrying a truthy enable label. The default.</summary>
    Labelled,

    /// <summary>Every in-scope volume, except those explicitly opted out via the ignore label.</summary>
    All,
}

/// <summary>
/// Decides which in-scope volumes are candidates for replication. An
/// <c>ignore</c> label always excludes a volume, even in <see cref="SelectionMode.All"/>
/// and even alongside an enable label.
/// </summary>
public sealed class VolumeSelector
{
    private readonly SelectionMode _mode;
    private readonly string _enableLabelKey;
    private readonly string _ignoreLabelKey;

    private VolumeSelector(SelectionMode mode, string enableLabelKey, string ignoreLabelKey)
    {
        _mode = mode;
        _enableLabelKey = enableLabelKey;
        _ignoreLabelKey = ignoreLabelKey;
    }

    public static VolumeSelector For(SelectionMode mode, string enableLabelKey, string ignoreLabelKey) =>
        new(mode, enableLabelKey, ignoreLabelKey);

    public bool IsSelected(DockerVolume volume)
    {
        if (IsTruthy(volume, _ignoreLabelKey))
            return false; // ignore always wins

        return _mode switch
        {
            SelectionMode.All => true,
            SelectionMode.Labelled => IsTruthy(volume, _enableLabelKey),
            _ => false,
        };
    }

    private static bool IsTruthy(DockerVolume volume, string key) =>
        volume.Labels.TryGetValue(key, out var value) &&
        value is "true" or "1" or "yes" or "True";
}
