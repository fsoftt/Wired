namespace Wired.Core.Enrichment;

public enum EnrichmentPhase
{
    Idle,

    /// <summary>Covers, tags and durations of the existing library.</summary>
    Covers,

    /// <summary>Loudness of the existing library.</summary>
    Loudness,
}

/// <summary>Progress of the library backfill, and what went wrong, so it can be shown and reported.</summary>
public sealed record EnrichmentStatus(EnrichmentPhase Phase, int Done, int Total, IReadOnlyList<string> Problems)
{
    public static readonly EnrichmentStatus Idle = new(EnrichmentPhase.Idle, 0, 0, []);
}
