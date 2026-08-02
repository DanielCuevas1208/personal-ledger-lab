namespace PersonalLedgerLab.Core;

/// <summary>
/// Selects a statement reader. Auto selects a reader from the file contents.
/// </summary>
public enum StatementFormat
{
    Auto,
    Csv,
    Ofx
}

/// <summary>
/// A parsed statement and the balance evidence supplied by its source.
/// </summary>
public sealed class ImportedStatement
{
    public string FormatName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public IReadOnlyList<ImportedRow> Rows { get; init; } = Array.Empty<ImportedRow>();
    public long? OpeningBalanceMinor { get; init; }
    public long? ClosingBalanceMinor { get; init; }
    public bool OpeningBalanceIsInferred { get; init; }
}

/// <summary>
/// Reads one statement format into the shared imported-row model.
/// </summary>
public interface IStatementImporter
{
    StatementFormat Format { get; }
    bool CanRead(string contents);
    ImportedStatement ReadStatement(TextReader reader);
}

/// <summary>
/// Resolves CSV and OFX readers without coupling the application service to either format.
/// </summary>
public sealed class StatementImporterRegistry
{
    private readonly IReadOnlyList<IStatementImporter> _importers;

    public StatementImporterRegistry()
        : this(new IStatementImporter[] { new OfxStatementImporter(), new CsvStatementImporter() })
    {
    }

    public StatementImporterRegistry(IEnumerable<IStatementImporter> importers)
    {
        if (importers is null) throw new ArgumentNullException(nameof(importers));
        _importers = importers.ToList();
        if (_importers.Count == 0)
            throw new ArgumentException("At least one statement importer is required.", nameof(importers));
    }

    public IReadOnlyList<StatementFormat> Formats => _importers.Select(i => i.Format).ToList();

    public ImportedStatement ReadFile(string path, StatementFormat format = StatementFormat.Auto)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A statement path is required.", nameof(path));
        return Read(File.ReadAllText(path), format);
    }

    public ImportedStatement Read(string contents, StatementFormat format = StatementFormat.Auto)
    {
        if (contents is null) throw new ArgumentNullException(nameof(contents));

        var importer = Resolve(contents, format);
        using var reader = new StringReader(contents);
        return importer.ReadStatement(reader);
    }

    private IStatementImporter Resolve(string contents, StatementFormat format)
    {
        if (format != StatementFormat.Auto)
        {
            var selected = _importers.FirstOrDefault(i => i.Format == format);
            if (selected is null)
                throw new InvalidDataException($"No importer is registered for {format} statements.");
            return selected;
        }

        var detected = _importers.FirstOrDefault(i => i.CanRead(contents));
        if (detected is null)
            throw new InvalidDataException("The statement format could not be detected. Select CSV or OFX.");
        return detected;
    }
}