namespace SwarmVolumeSync.Core;

/// <summary>
/// The running agent's software release (CONTEXT.md, Agent version) — distinct
/// from a volume's data generation. <see cref="Version"/> is CalVer
/// (<c>YYYY.M.MICRO</c>, or <c>0.0.0-dev</c> for local builds); <see cref="Commit"/>
/// is the source revision baked in at build. Surfaced as <c>agentVersion</c> on
/// <c>GET /status</c> and the <c>svs_build_info</c> Prometheus gauge.
/// </summary>
public sealed record BuildInfo(string Version, string Commit)
{
    public static readonly BuildInfo Dev = new("0.0.0-dev", "dev");
}
