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
}
