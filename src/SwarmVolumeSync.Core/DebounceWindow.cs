namespace SwarmVolumeSync.Core;

/// <summary>
/// A debounce window: records activity timestamps and reports when activity has
/// settled (no new activity for the configured window). Drives change-triggered
/// syncs so a burst of writes coalesces into a single push (CONTEXT.md, Sync
/// trigger). Time is passed in, so the policy is deterministic and testable.
/// </summary>
public sealed class DebounceWindow(TimeSpan window)
{
    private DateTimeOffset? _lastActivity;

    public void RecordActivity(DateTimeOffset at) => _lastActivity = at;

    public bool HasSettled(DateTimeOffset now) =>
        _lastActivity is { } last && now - last >= window;

    public void Reset() => _lastActivity = null;
}
