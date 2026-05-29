using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class VolumeSelectorTests
{
    private const string EnableKey = "swarm-volume-sync.enable";

    private static DockerVolume Vol(string name, params (string key, string val)[] labels) =>
        new(name, "local", labels.ToDictionary(l => l.key, l => l.val));

    [Fact]
    public void Volume_carrying_the_enable_label_is_selected()
    {
        var selector = VolumeSelector.Labelled(EnableKey);

        Assert.True(selector.IsSelected(Vol("appdata", (EnableKey, "true"))));
    }

    [Fact]
    public void Volume_without_the_enable_label_is_not_selected()
    {
        var selector = VolumeSelector.Labelled(EnableKey);

        Assert.False(selector.IsSelected(Vol("appdata")));
    }
}
