using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Agent configuration, read from environment variables. Defaults match
/// CONTEXT.md (opt-in selection, 5s/5min sync cadence — cadence consumed by #4).
/// </summary>
public sealed record AgentConfig(
    string ServiceName,
    string SshKeyPath,
    string DockerSocket,
    SelectionMode SelectionMode,
    string EnableLabelKey,
    string IgnoreLabelKey,
    string MetadataDirectory,
    int ControlApiPort,
    TimeSpan PollInterval)
{
    public static AgentConfig FromEnvironment()
    {
        string Get(string key, string fallback) =>
            Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

        // The overlay DNS name that enumerates all agent tasks is "tasks.<service>".
        var service = Get("SVS_SERVICE_NAME", "swarm-volume-sync");

        var mode = Get("SVS_SYNC_MODE", "labelled").Equals("all", StringComparison.OrdinalIgnoreCase)
            ? SelectionMode.All
            : SelectionMode.Labelled;

        return new AgentConfig(
            ServiceName: service,
            SshKeyPath: Get("SVS_SSH_KEY_PATH", "/run/secrets/svs_ssh_key"),
            DockerSocket: Get("SVS_DOCKER_SOCKET", "unix:///var/run/docker.sock"),
            SelectionMode: mode,
            EnableLabelKey: Get("SVS_ENABLE_LABEL", "swarm-volume-sync.enable"),
            IgnoreLabelKey: Get("SVS_IGNORE_LABEL", "swarm-volume-sync.ignore"),
            MetadataDirectory: Get("SVS_METADATA_DIR", "/var/lib/swarm-volume-sync"),
            ControlApiPort: int.TryParse(Get("SVS_CONTROL_API_PORT", "8080"), out var p) ? p : 8080,
            PollInterval: TimeSpan.FromSeconds(
                int.TryParse(Get("SVS_POLL_INTERVAL_SECONDS", "30"), out var s) ? s : 30));
    }

    public string TasksDnsName => $"tasks.{ServiceName}";
}
