namespace FcDraft.Domain.Entities;

/// <summary>
/// A club represented in a dataset version, derived from the footballer feed. The EA ratings feed does
/// not include Kick Off club star ratings, so <see cref="StarRating"/> is unknown at import and
/// <see cref="IsFiveStarEligible"/> is curated later (PR-09) rather than being taken from the source.
/// </summary>
public sealed class Club
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DatasetVersionId { get; init; }
    public required string Name { get; set; }
    public required string League { get; set; }

    /// <summary>Kick Off club star rating, if known. Null because the source feed omits it.</summary>
    public int? StarRating { get; set; }

    /// <summary>Whether the club is an eligible five-star Kick Off club. Curated in PR-09.</summary>
    public bool IsFiveStarEligible { get; set; }
}
