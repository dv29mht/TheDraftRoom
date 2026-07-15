using FcDraft.Application.Features.Datasets;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Admin-side dataset management: validate-and-import a version (as a draft), inspect its issues,
/// activate a clean version (archiving the previous active one), and list retained versions. Backed
/// by the database; the in-memory foundation reports that management requires SQL persistence.
/// </summary>
public interface IDatasetAdminService
{
    Task<DatasetImportReport> ImportAsync(DatasetImportRequest request, CancellationToken cancellationToken);
    Task<DatasetImportReport> ImportBundledAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DatasetVersionSummary>> ListVersionsAsync(CancellationToken cancellationToken);
    Task<DatasetVersionDetail?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken);
    Task<DatasetVersionSummary> ActivateAsync(Guid versionId, CancellationToken cancellationToken);
}

/// <summary>Provides the packaged FC 26 dataset rows for the "import bundled" path and dev seeding.</summary>
public interface IBundledDataset
{
    string Label { get; }
    string Source { get; }
    IReadOnlyList<FootballerImportRow> Load();
}
