using System.Security.Cryptography;
using System.Text;

namespace SwarmVolumeSync.Core;

/// <summary>
/// A deterministic fingerprint of a volume's <c>_data</c> tree, computed from
/// each file's relative path, length and last-write time. Because rsync's
/// archive mode (<c>-a</c>) preserves modification times, two nodes holding an
/// identical copy of a volume produce the same checksum — so it doubles as a
/// change detector (same node over time) and an integrity check (across nodes).
/// </summary>
public static class DirectoryChecksum
{
    public static string Compute(string directory)
    {
        var entries = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var info = new FileInfo(file);
            entries.Add($"{relative}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}");
        }

        entries.Sort(StringComparer.Ordinal);

        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", entries));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
