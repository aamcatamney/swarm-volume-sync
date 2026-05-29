using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class VolumeSelectorTests
{
    private const string EnableKey = "swarm-volume-sync.enable";
    private const string IgnoreKey = "swarm-volume-sync.ignore";

    private static DockerVolume Vol(string name, params (string key, string val)[] labels) =>
        new(name, "local", labels.ToDictionary(l => l.key, l => l.val));

    private static VolumeSelector Labelled() => VolumeSelector.For(SelectionMode.Labelled, EnableKey, IgnoreKey);
    private static VolumeSelector All() => VolumeSelector.For(SelectionMode.All, EnableKey, IgnoreKey);

    [Fact]
    public void Labelled_mode_selects_a_volume_carrying_the_enable_label()
    {
        Assert.True(Labelled().IsSelected(Vol("appdata", (EnableKey, "true"))));
    }

    [Fact]
    public void Labelled_mode_does_not_select_an_unlabelled_volume()
    {
        Assert.False(Labelled().IsSelected(Vol("appdata")));
    }

    [Fact]
    public void Labelled_mode_ignores_a_non_truthy_enable_value()
    {
        Assert.False(Labelled().IsSelected(Vol("appdata", (EnableKey, "false"))));
    }

    [Fact]
    public void All_mode_selects_an_unlabelled_volume()
    {
        Assert.True(All().IsSelected(Vol("appdata")));
    }

    [Fact]
    public void All_mode_excludes_a_volume_marked_ignore()
    {
        Assert.False(All().IsSelected(Vol("scratch", (IgnoreKey, "true"))));
    }

    [Fact]
    public void Ignore_label_always_wins_even_with_enable_in_labelled_mode()
    {
        var conflicted = Vol("appdata", (EnableKey, "true"), (IgnoreKey, "true"));

        Assert.False(Labelled().IsSelected(conflicted));
    }
}
