namespace SwarmVolumeSync.Core;

/// <summary>
/// A monotonic generation counter for a replicated volume (see CONTEXT.md,
/// Volume version). The source increments it as it records new state; every
/// copy carries the version of the data it holds. Higher generation wins the
/// version check that guards pull-before-serve (ADR-0003).
/// </summary>
public readonly record struct VolumeVersion(long Generation) : IComparable<VolumeVersion>
{
    public static VolumeVersion Initial => new(0);

    public VolumeVersion Next() => new(Generation + 1);

    public int CompareTo(VolumeVersion other) => Generation.CompareTo(other.Generation);

    public static bool operator >(VolumeVersion a, VolumeVersion b) => a.Generation > b.Generation;
    public static bool operator <(VolumeVersion a, VolumeVersion b) => a.Generation < b.Generation;
    public static bool operator >=(VolumeVersion a, VolumeVersion b) => a.Generation >= b.Generation;
    public static bool operator <=(VolumeVersion a, VolumeVersion b) => a.Generation <= b.Generation;
}
