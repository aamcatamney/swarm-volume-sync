using System.Diagnostics;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Executes an rsync transfer by shelling out to <c>rsync</c>. The C# agent
/// decides what/where (Core); rsync does the byte transfer (ADR-0001).
/// </summary>
public sealed class RsyncRunner(string rsyncPath = "rsync")
{
    public async Task<bool> RunAsync(string description, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = rsyncPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"rsync {description} failed (exit {process.ExitCode}): {stderr.Trim()}");
            return false;
        }

        return true;
    }
}
