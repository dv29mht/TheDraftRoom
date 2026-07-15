using System.Reflection;
using System.Text.Json;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;

namespace FcDraft.Infrastructure.Datasets;

/// <summary>
/// Loads the FC 26 dataset packaged as an embedded resource and projects it into import rows. Used by
/// the admin "import bundled" action and by development seeding, so the app has a working dataset
/// without an external file or upload.
/// </summary>
public sealed class BundledPlayerDataset : IBundledDataset
{
    private const string ResourceName = "FcDraft.Infrastructure.Data.fc26-players.json";

    private readonly Lazy<(string Label, string Source, IReadOnlyList<FootballerImportRow> Rows)> _data = new(Parse);

    public string Label => _data.Value.Label;
    public string Source => _data.Value.Source;
    public IReadOnlyList<FootballerImportRow> Load() => _data.Value.Rows;

    private static (string, string, IReadOnlyList<FootballerImportRow>) Parse()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Bundled dataset resource '{ResourceName}' was not found.");
        using var document = JsonDocument.Parse(stream);
        return DatasetJsonParser.Parse(document.RootElement);
    }
}
