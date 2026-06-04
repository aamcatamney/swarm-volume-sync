using System.Globalization;
using System.Text;

namespace SwarmVolumeSync.Core;

/// <summary>
/// Replication health for one volume across the mesh (CONTEXT.md, Coverage /
/// under-replication). <see cref="IsUnderReplicated"/> is the key failover-risk
/// signal: a volume not present on every node may fail over to a node lacking a
/// copy.
/// </summary>
public sealed record VolumeStatus(
    string Volume,
    long Version,
    int Holders,
    int TotalNodes,
    double SyncLagSeconds,
    long LastSyncUnix)
{
    public double Coverage => TotalNodes == 0 ? 0.0 : (double)Holders / TotalNodes;

    public bool IsUnderReplicated => Holders < TotalNodes;
}

/// <summary>
/// Renders <see cref="VolumeStatus"/> values as a Prometheus text exposition.
/// </summary>
public static class PrometheusFormatter
{
    public static string Format(IReadOnlyList<VolumeStatus> statuses, BuildInfo build)
    {
        var sb = new StringBuilder();

        // Agent version (CONTEXT.md): info-style gauge, always 1; read the labels.
        sb.Append("# HELP svs_build_info Running agent build (always 1; read the labels).\n");
        sb.Append("# TYPE svs_build_info gauge\n");
        sb.Append("svs_build_info{version=\"").Append(Escape(build.Version))
          .Append("\",commit=\"").Append(Escape(build.Commit)).Append("\"} 1\n");

        Gauge(sb, "svs_volume_coverage", "Fraction of nodes holding a copy of the volume (1.0 = fully covered).",
            statuses, s => Num(s.Coverage));
        Gauge(sb, "svs_sync_lag_seconds", "Seconds since the volume was last synced.",
            statuses, s => Num(s.SyncLagSeconds));
        Gauge(sb, "svs_last_sync_timestamp", "Unix timestamp of the last successful sync.",
            statuses, s => s.LastSyncUnix.ToString(CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    private static void Gauge(
        StringBuilder sb, string metric, string help,
        IReadOnlyList<VolumeStatus> statuses, Func<VolumeStatus, string> value)
    {
        sb.Append("# HELP ").Append(metric).Append(' ').Append(help).Append('\n');
        sb.Append("# TYPE ").Append(metric).Append(" gauge\n");
        foreach (var s in statuses)
            sb.Append(metric).Append("{volume=\"").Append(s.Volume).Append("\"} ").Append(value(s)).Append('\n');
    }

    private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
