using System.Reflection;
using SwarmVolumeSync.Core;

namespace SwarmVolumeSync.Agent;

/// <summary>
/// Reads the agent's <see cref="BuildInfo"/> (CONTEXT.md, Agent version) from the
/// assembly's informational version, which the build bakes in as
/// <c>&lt;CalVer&gt;+&lt;commit&gt;</c> (see Dockerfile). Falls back to
/// <see cref="BuildInfo.Dev"/> for local/test runs with no baked version.
/// </summary>
public static class BuildInfoReader
{
    public static BuildInfo Current { get; } = Read();

    private static BuildInfo Read()
    {
        var informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return BuildInfo.Dev;

        // SDK appends "+<SourceRevisionId>" to the informational version.
        var plus = informational.IndexOf('+');
        var version = plus < 0 ? informational : informational[..plus];
        var commit = plus < 0 || plus + 1 >= informational.Length
            ? BuildInfo.Dev.Commit
            : informational[(plus + 1)..];

        // A bare assembly version (e.g. "1.0.0") means nothing was injected.
        return version is "1.0.0" or "0.0.0"
            ? BuildInfo.Dev
            : new BuildInfo(version, commit);
    }
}
