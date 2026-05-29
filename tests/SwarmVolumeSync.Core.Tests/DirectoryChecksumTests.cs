using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Core.Tests;

public class DirectoryChecksumTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "svs-test-" + Guid.NewGuid().ToString("N"));

    public DirectoryChecksumTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Dir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Adding_a_file_changes_the_checksum()
    {
        var dir = Dir("a");
        var before = DirectoryChecksum.Compute(dir);

        File.WriteAllText(Path.Combine(dir, "marker.txt"), "hello");
        var after = DirectoryChecksum.Compute(dir);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Two_directories_with_identical_contents_have_the_same_checksum()
    {
        var a = Dir("src");
        var b = Dir("dst");
        var when = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);

        foreach (var dir in new[] { a, b })
        {
            var file = Path.Combine(dir, "marker.txt");
            File.WriteAllText(file, "same-content");
            File.SetLastWriteTimeUtc(file, when); // rsync -a preserves mtime
        }

        Assert.Equal(DirectoryChecksum.Compute(a), DirectoryChecksum.Compute(b));
    }
}
