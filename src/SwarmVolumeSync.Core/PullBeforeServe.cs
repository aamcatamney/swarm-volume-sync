namespace SwarmVolumeSync.Core;

/// <summary>What a newly-promoted source should do for a volume.</summary>
public enum ServeAction
{
    /// <summary>This node holds the highest version; it may push (and mirror-delete).</summary>
    Serve,

    /// <summary>A peer holds a higher version; pull that first to avoid overwriting good data.</summary>
    Hydrate,
}

public sealed record PullDecision(ServeAction Action, string? HydrateFromPeer, VolumeVersion TargetVersion);

/// <summary>
/// The anti-wipe guard (ADR-0003). Before a source pushes, it compares the
/// version it can vouch for against the versions peers advertise. If a peer is
/// ahead, the source must hydrate from it first; only a source at the mesh
/// high-water mark may serve. An empty freshly-created volume has no trusted
/// version, so it always loses to a peer that holds data — it can never
/// overwrite good copies.
/// </summary>
public static class PullBeforeServe
{
    /// <summary>
    /// The version this node can prove it holds: its stored version, but only if
    /// the stored checksum still matches the volume's current content. Otherwise
    /// (no metadata, or content no longer matches — e.g. an empty fresh volume)
    /// the node cannot vouch for any version.
    /// </summary>
    public static VolumeVersion TrustedVersion(VolumeMetadata? stored, string currentChecksum) =>
        stored is not null && stored.Checksum == currentChecksum ? stored.Version : VolumeVersion.Initial;

    public static PullDecision Decide(
        VolumeVersion localTrusted,
        IReadOnlyList<(string Peer, VolumeVersion Version)> peerVersions)
    {
        if (peerVersions.Count == 0)
            return new PullDecision(ServeAction.Serve, null, localTrusted);

        var highest = peerVersions.MaxBy(p => p.Version.Generation);

        return highest.Version > localTrusted
            ? new PullDecision(ServeAction.Hydrate, highest.Peer, highest.Version)
            : new PullDecision(ServeAction.Serve, null, localTrusted);
    }
}
