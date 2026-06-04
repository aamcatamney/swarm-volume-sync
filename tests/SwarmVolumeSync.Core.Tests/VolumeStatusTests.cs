using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class VolumeStatusTests
{
    [Fact]
    public void A_volume_present_on_every_node_is_fully_covered()
    {
        var status = new VolumeStatus("appdata", Version: 5, Holders: 3, TotalNodes: 3, SyncLagSeconds: 1, LastSyncUnix: 100);

        Assert.Equal(1.0, status.Coverage);
        Assert.False(status.IsUnderReplicated);
    }

    [Fact]
    public void A_volume_missing_from_some_node_is_under_replicated()
    {
        var status = new VolumeStatus("appdata", Version: 5, Holders: 2, TotalNodes: 3, SyncLagSeconds: 1, LastSyncUnix: 100);

        Assert.Equal(2.0 / 3.0, status.Coverage, precision: 6);
        Assert.True(status.IsUnderReplicated);
    }

    [Fact]
    public void Coverage_is_zero_when_there_are_no_nodes()
    {
        var status = new VolumeStatus("appdata", Version: 0, Holders: 0, TotalNodes: 0, SyncLagSeconds: 0, LastSyncUnix: 0);

        Assert.Equal(0.0, status.Coverage);
    }
}

public class PrometheusFormatterTests
{
    [Fact]
    public void Emits_the_documented_gauges_per_volume_with_labels()
    {
        var text = PrometheusFormatter.Format(
        [
            new VolumeStatus("appdata", Version: 5, Holders: 2, TotalNodes: 3, SyncLagSeconds: 12, LastSyncUnix: 1748520000),
        ],
        new BuildInfo("2026.6.0", "abc1234"));

        Assert.Contains("svs_volume_coverage{volume=\"appdata\"} 0.6666666666666666", text);
        Assert.Contains("svs_sync_lag_seconds{volume=\"appdata\"} 12", text);
        Assert.Contains("svs_last_sync_timestamp{volume=\"appdata\"} 1748520000", text);
        Assert.Contains("# TYPE svs_volume_coverage gauge", text);
    }

    [Fact]
    public void Emits_build_info_gauge_carrying_the_agent_version()
    {
        var text = PrometheusFormatter.Format([], new BuildInfo("2026.6.0", "abc1234"));

        Assert.Contains("# TYPE svs_build_info gauge", text);
        Assert.Contains("svs_build_info{version=\"2026.6.0\",commit=\"abc1234\"} 1", text);
    }
}
