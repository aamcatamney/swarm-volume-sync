using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class DebounceWindowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly DebounceWindow _window = new(TimeSpan.FromSeconds(5));

    [Fact]
    public void With_no_activity_it_is_never_settled()
    {
        Assert.False(_window.HasSettled(T0));
    }

    [Fact]
    public void It_is_not_settled_while_still_within_the_window()
    {
        _window.RecordActivity(T0);

        Assert.False(_window.HasSettled(T0.AddSeconds(3)));
    }

    [Fact]
    public void It_settles_once_the_window_elapses_without_new_activity()
    {
        _window.RecordActivity(T0);

        Assert.True(_window.HasSettled(T0.AddSeconds(5)));
    }

    [Fact]
    public void Fresh_activity_extends_the_window()
    {
        _window.RecordActivity(T0);
        _window.RecordActivity(T0.AddSeconds(4)); // burst keeps it busy

        Assert.False(_window.HasSettled(T0.AddSeconds(6)));   // 2s since last activity
        Assert.True(_window.HasSettled(T0.AddSeconds(9)));    // 5s since last activity
    }

    [Fact]
    public void Reset_clears_pending_activity()
    {
        _window.RecordActivity(T0);
        _window.Reset();

        Assert.False(_window.HasSettled(T0.AddSeconds(10)));
    }
}

public class VolumePathParserTests
{
    [Fact]
    public void Extracts_the_volume_name_from_a_changed_data_path()
    {
        var name = VolumePathParser.VolumeNameFromChange(
            "/var/lib/docker/volumes", "/var/lib/docker/volumes/appdata/_data/sub/file.txt");

        Assert.Equal("appdata", name);
    }

    [Fact]
    public void Returns_null_for_a_path_outside_the_volumes_root()
    {
        var name = VolumePathParser.VolumeNameFromChange("/var/lib/docker/volumes", "/etc/passwd");

        Assert.Null(name);
    }
}
