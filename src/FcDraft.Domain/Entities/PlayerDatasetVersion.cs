namespace FcDraft.Domain.Entities;

/// <summary>
/// A validated, versioned snapshot of the FC 26 men's dataset. Imports create a <see cref="Draft"/>
/// version; activation promotes one version to <see cref="Active"/> (archiving the previous active
/// one) so historical versions are retained and an in-progress draft can pin the version it started
/// with (PRD §9.3).
/// </summary>
public sealed class PlayerDatasetVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Label { get; set; }
    public required string Source { get; set; }
    public DatasetVersionStatus Status { get; set; } = DatasetVersionStatus.Draft;

    public int FootballerCount { get; set; }
    public int ClubCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ActivatedAt { get; set; }
}

public enum DatasetVersionStatus
{
    Draft = 1,
    Active = 2,
    Archived = 3,
}
