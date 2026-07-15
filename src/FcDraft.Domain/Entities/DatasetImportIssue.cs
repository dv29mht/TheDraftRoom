namespace FcDraft.Domain.Entities;

/// <summary>
/// A validation finding recorded during an import so an admin can inspect exactly what was wrong
/// before activating. Errors skip the offending row and block activation; warnings are advisory.
/// </summary>
public sealed class DatasetImportIssue
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DatasetVersionId { get; init; }
    public required ImportIssueSeverity Severity { get; init; }

    /// <summary>1-based index of the offending row in the source, for pinpointing bad data.</summary>
    public required int Row { get; init; }
    public int? ExternalId { get; init; }
    public string? Field { get; init; }
    public required string Message { get; init; }
}

public enum ImportIssueSeverity
{
    Error = 1,
    Warning = 2,
}
