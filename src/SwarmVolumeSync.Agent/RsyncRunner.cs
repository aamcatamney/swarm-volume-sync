using System.Diagnostics;
using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Executes a planned <see cref="PushOperation"/> by shelling out to <c>rsync</c>.
/// The C# agent decides what/where (Core); rsync does the byte transfer (ADR-0001).
/// </summary>
public sealed class RsyncRunner(string rsyncPath = "rsync")
{
    public async Task<bool> RunAsync(PushOperation op, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = rsyncPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in op.RsyncArgs)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine(
                $"rsync push of '{op.VolumeName}' to {op.PeerAddress} failed (exit {process.ExitCode}): {stderr.Trim()}");
            return false;
        }

        return true;
    }
}
