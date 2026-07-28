using System.Globalization;
using System.Text;

namespace PersonalLedgerLab.Core;

/// <summary>
/// One parsed row from an imported statement file.
/// Amount is signed from the account holder view: positive is a deposit,
/// negative is a payment. RunningBalance is the statement running balance
/// after this row, in minor units.
/// </summary>
public sealed class ImportedRow
{
    public DateOnly Date { get; set; }
    public string? Reference { get; set; }
    public string Description { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public long? RunningBalanceMinor { get; set; }
    public string CurrencyCode { get; set; } = "USD";

    public override string ToString() =>
        $"{Date:yyyy-MM-dd} {Description} {(AmountMinor / 100m):0.00;-0.00} {CurrencyCode}";
}

/// <summary>
/// Reads a CSV bank statement into <see cref="ImportedRow"/> values.
/// Expected header, in order: Date,Reference,Description,Amount,Balance,Currency.
/// The Reference and Currency columns may be empty. The Balance column is optional.
/// </summary>
public sealed class CsvStatementImporter
{
    private static readonly string[] HeaderNames =
        { "Date", "Reference", "Description", "Amount", "Balance", "Currency" };

    public IReadOnlyList<ImportedRow> Read(TextReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        var rows = new List<ImportedRow>();
        string? headerLine = reader.ReadLine();
        if (headerLine is null)
            return rows;

        var headers = ParseLine(headerLine);
        var index = BuildIndex(headers);

        string? line;
        int lineNo = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseLine(line);
            if (fields.Length < 4)
                throw new InvalidDataException(
                    $"Line {lineNo} has {fields.Length} fields. At least four fields are required.");

            var row = new ImportedRow
            {
                Date = DateOnly.Parse(fields[index.Date], CultureInfo.InvariantCulture),
                Description = SafeField(fields, index.Description).Trim(),
                Reference = string.IsNullOrEmpty(SafeField(fields, index.Reference))
                    ? null
                    : SafeField(fields, index.Reference).Trim(),
                AmountMinor = ParseAmount(SafeField(fields, index.Amount)),
                CurrencyCode = string.IsNullOrWhiteSpace(SafeField(fields, index.Currency))
                    ? "USD"
                    : SafeField(fields, index.Currency).Trim().ToUpperInvariant()
            };

            string balanceText = SafeField(fields, index.Balance);
            if (!string.IsNullOrWhiteSpace(balanceText))
                row.RunningBalanceMinor = ParseAmount(balanceText);

            rows.Add(row);
        }

        return rows;
    }

    public IReadOnlyList<ImportedRow> ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        return Read(reader);
    }

    private static string SafeField(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index] : string.Empty;

    private static long ParseAmount(string text)
    {
        text = text.Trim().Replace("$", "", StringComparison.Ordinal)
                         .Replace("€", "", StringComparison.Ordinal)
                         .Replace("£", "", StringComparison.Ordinal)
                         .Trim();
        if (text.Length == 0)
            return 0;

        int lastDot = text.LastIndexOf('.');
        int lastComma = text.LastIndexOf(',');
        bool hasDot = lastDot >= 0;
        bool hasComma = lastComma >= 0;

        int decimalPos;
        char? decimalChar = null;

        if (hasDot && hasComma)
        {
            decimalPos = Math.Max(lastDot, lastComma);
            decimalChar = text[decimalPos];
        }
        else if (hasDot || hasComma)
        {
            int pos = hasDot ? lastDot : lastComma;
            int digitsAfter = text.Length - pos - 1;
            if (digitsAfter == 3 && HasLeadingGrouping(text, pos))
            {
                decimalPos = -1;
            }
            else
            {
                decimalPos = pos;
                decimalChar = text[pos];
            }
        }
        else
        {
            decimalPos = -1;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '.' || c == ',')
            {
                if (i == decimalPos)
                    sb.Append('.');
            }
            else if (c == '-' || c == '+' || char.IsDigit(c))
            {
                sb.Append(c);
            }
        }

        decimal value = decimal.Parse(sb.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture);
        return (long)Math.Round(value * 100m, MidpointRounding.ToEven);
    }

    private static bool HasLeadingGrouping(string text, int separatorPos)
    {
        int before = separatorPos;
        if (before == 0)
            return false;
        if (before > 3)
            return true;
        return true;
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static (int Date, int Reference, int Description, int Amount, int Balance, int Currency)
        BuildIndex(IReadOnlyList<string> headers)
    {
        var normalised = headers.Select(h => h.Trim()).ToList();
        int Index(string name)
        {
            for (int i = 0; i < normalised.Count; i++)
                if (string.Equals(normalised[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        int date = Index(HeaderNames[0]);
        if (date < 0)
            throw new InvalidDataException("The CSV header must contain a 'Date' column.");

        return (
            date,
            Index(HeaderNames[1]),
            Index(HeaderNames[2]),
            Index(HeaderNames[3]),
            Index(HeaderNames[4]),
            Index(HeaderNames[5])
        );
    }
}