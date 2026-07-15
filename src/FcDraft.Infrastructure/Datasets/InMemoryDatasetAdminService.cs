using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;

namespace FcDraft.Infrastructure.Datasets;

/// <summary>
/// Dataset admin for the in-memory foundation. Versioned import/activation needs the database, so
/// those operations are refused with a clear message; the bundled dataset is surfaced as a single,
/// read-only active version so the admin screen still shows accurate totals without a database.
/// </summary>
public sealed class InMemoryDatasetAdminService(IBundledDataset bundled) : IDatasetAdminService
{
    // Stable synthetic id for the read-only bundled version.
    private static readonly Guid BundledVersionId = new("00000000-0000-0000-0000-0000000000d1");

    private const string RequiresDatabase =
        "Dataset import and versioning require the PostgreSQL persistence configuration.";

    public Task<DatasetImportReport> ImportAsync(DatasetImportRequest request, CancellationToken cancellationToken) =>
        throw new ConflictAppException(RequiresDatabase);

    public Task<DatasetImportReport> ImportBundledAsync(CancellationToken cancellationToken) =>
        throw new ConflictAppException(RequiresDatabase);

    public Task<DatasetVersionSummary> ActivateAsync(Guid versionId, CancellationToken cancellationToken) =>
        throw new ConflictAppException(RequiresDatabase);

    public Task<IReadOnlyList<DatasetVersionSummary>> ListVersionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DatasetVersionSummary>>([BundledSummary()]);

    public Task<DatasetVersionDetail?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken) =>
        Task.FromResult<DatasetVersionDetail?>(
            versionId == BundledVersionId ? new DatasetVersionDetail(BundledSummary(), []) : null);

    private DatasetVersionSummary BundledSummary()
    {
        var rows = bundled.Load();
        var clubCount = rows
            .Select(row => row.Club)
            .Where(club => !string.IsNullOrWhiteSpace(club))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new DatasetVersionSummary(
            BundledVersionId,
            bundled.Label,
            bundled.Source,
            "Active",
            rows.Count,
            clubCount,
            ErrorCount: 0,
            WarningCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch,
            ActivatedAt: DateTimeOffset.UnixEpoch);
    }
}
