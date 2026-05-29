using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class RsyncPlanTests
{
    private static readonly RsyncOptions Opts = new(SshKeyPath: "/run/secrets/svs_ssh_key");

    [Fact]
    public void Push_args_archive_volume_data_to_peer_over_ssh()
    {
        var args = RsyncPlan.PushArgs("appdata", "10.0.0.3", Opts);

        Assert.Equal(
            new[]
            {
                "-a",
                "-e", "ssh -i /run/secrets/svs_ssh_key -o StrictHostKeyChecking=no",
                "/var/lib/docker/volumes/appdata/_data/",
                "10.0.0.3:/var/lib/docker/volumes/appdata/_data/",
            },
            args);
    }

    [Fact]
    public void Push_does_not_mirror_delete_by_default()
    {
        var args = RsyncPlan.PushArgs("appdata", "10.0.0.3", Opts);

        Assert.DoesNotContain("--delete", args);
    }

    [Fact]
    public void Push_mirror_deletes_only_when_explicitly_enabled()
    {
        var args = RsyncPlan.PushArgs("appdata", "10.0.0.3", Opts with { MirrorDelete = true });

        Assert.Contains("--delete", args);
    }

    [Fact]
    public void A_bandwidth_limit_is_passed_to_rsync_when_set()
    {
        var args = RsyncPlan.PullArgs("appdata", "10.0.0.2", Opts with { BandwidthLimitKb = 2048 });

        Assert.Contains("--bwlimit=2048", args);
    }

    [Fact]
    public void No_bandwidth_limit_argument_when_unset()
    {
        var args = RsyncPlan.PushArgs("appdata", "10.0.0.3", Opts);

        Assert.DoesNotContain(args, a => a.StartsWith("--bwlimit"));
    }

    [Fact]
    public void Pull_args_archive_peer_data_into_the_local_volume_over_ssh()
    {
        var args = RsyncPlan.PullArgs("appdata", "10.0.0.2", Opts with { MirrorDelete = true });

        Assert.Equal(
            new[]
            {
                "-a",
                "--delete",
                "-e", "ssh -i /run/secrets/svs_ssh_key -o StrictHostKeyChecking=no",
                "10.0.0.2:/var/lib/docker/volumes/appdata/_data/",
                "/var/lib/docker/volumes/appdata/_data/",
            },
            args);
    }
}
