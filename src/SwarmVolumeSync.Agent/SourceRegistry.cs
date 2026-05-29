namespace SwarmVolumeSync.Agent;

/// <summary>
/// Publishes the set of volume names this node currently sources, so peers can
/// ask "is anyone still sourcing volume X?" via the control API. That mesh-wide
/// answer is what gates retention reclaim (a copy sourced nowhere is orphaned).
/// </summary>
public sealed class SourceRegistry
{
    private volatile IReadOnlyCollection<string> _sourced = Array.Empty<string>();

    public void Publish(IReadOnlyCollection<string> sourced) => _sourced = sourced;

    public IReadOnlyCollection<string> Current => _sourced;
}
