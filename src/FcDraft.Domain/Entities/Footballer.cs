namespace FcDraft.Domain.Entities;

/// <summary>
/// One FC 26 men's footballer within a dataset version. Card stats, roles, and PlayStyles are
/// display-only and stored as JSON text; positions are normalized into
/// <see cref="FootballerPosition"/> so the position pool can be filtered in the database.
/// </summary>
public sealed class Footballer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DatasetVersionId { get; init; }

    public required int ExternalId { get; init; }
    public required string CommonName { get; set; }
    public string? FullName { get; set; }
    public required int Overall { get; set; }
    public required string PrimaryPosition { get; set; }

    public required string Club { get; set; }
    public required string League { get; set; }
    public required string Nation { get; set; }
    public string? PreferredFoot { get; set; }
    public int WeakFoot { get; set; }
    public int SkillMoves { get; set; }
    public string? Height { get; set; }
    public string? ImageUrl { get; set; }
    public string? SourceUrl { get; set; }

    /// <summary>Base / Kick Off eligibility. Non-Kick-Off content is excluded at import.</summary>
    public bool IsKickOffEligible { get; set; } = true;

    /// <summary>Whether this footballer participates in draft pools (75+, active).</summary>
    public bool IsActive { get; set; } = true;

    // Display-only JSON payloads (card stats, roles with +/++ familiarity, PlayStyles).
    public string StatsJson { get; set; } = "[]";
    public string RolesJson { get; set; } = "[]";
    public string PlayStylesJson { get; set; } = "[]";

    public ICollection<FootballerPosition> Positions { get; init; } = new List<FootballerPosition>();
}

/// <summary>A position a footballer can fill (primary or alternate), normalized for pool filtering.</summary>
public sealed class FootballerPosition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FootballerId { get; init; }
    public required string Position { get; init; }
    public bool IsPrimary { get; init; }
}
