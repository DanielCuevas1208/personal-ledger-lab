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
/// Reads common bank CSV layouts into imported rows.
/// </summary>
public sealed class CsvStatementImporter : IStatementImporter
{
    public StatementFormat Format => StatementFormat.Csv;

    public bool CanRead(string contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
            return false;

        string? headerLine = new StringReader(contents).ReadLine();
        if (headerLine is null)
            return false;

        var headers = ParseLine(headerLine).Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return HasAny(headers, "date", "transactiondate", "posteddate", "postingdate")
            && HasAny(headers, "description", "details", "detail", "memo", "name", "payee")
            && (HasAny(headers, "amount", "transactionamount", "signedamount")
                || HasAny(headers, "debit", "withdrawal", "withdrawals", "outflow")
                || HasAny(headers, "credit", "deposit", "deposits", "inflow"));
    }

    public IReadOnlyList<ImportedRow> Read(TextReader reader) => ReadStatement(reader).Rows;

    public ImportedStatement ReadStatement(TextReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        var rows = new List<ImportedRow>();
        string? headerLine = reader.ReadLine();
        if (headerLine is null)
            return CreateStatement(rows);

        var index = BuildIndex(ParseLine(headerLine));
        string? line;
        int lineNo = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseLine(line);
            if (fields.Length <= index.RequiredField)
                throw new InvalidDataException(
                    $"Line {lineNo} has {fields.Length} fields. The selected columns are not present.");

            var row = new ImportedRow
            {
                Date = ParseDate(RequiredField(fields, index.Date, lineNo), lineNo),
                Description = RequiredField(fields, index.Description, lineNo).Trim(),
                Reference = OptionalField(fields, index.Reference)?.Trim(),
                AmountMinor = ParseSignedAmount(fields, index),
                CurrencyCode = string.IsNullOrWhiteSpace(OptionalField(fields, index.Currency))
                    ? "USD"
                    : OptionalField(fields, index.Currency)!.Trim().ToUpperInvariant()
            };

            string? balanceText = OptionalField(fields, index.Balance);
            if (!string.IsNullOrWhiteSpace(balanceText))
                row.RunningBalanceMinor = ParseAmount(balanceText!);

            rows.Add(row);
        }

        return CreateStatement(rows);
    }

    public IReadOnlyList<ImportedRow> ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        return Read(reader);
    }

    private static ImportedStatement CreateStatement(IReadOnlyList<ImportedRow> rows)
    {
        long? opening = null;
        long? closing = rows.LastOrDefault()?.RunningBalanceMinor;
        var firstBalance = rows.Count > 0 ? rows[0].RunningBalanceMinor : null;
        if (firstBalance.HasValue)
            opening = firstBalance.Value - rows[0].AmountMinor;

        return new ImportedStatement
        {
            FormatName = "CSV",
            Source = "csv-import",
            Rows = rows,
            OpeningBalanceMinor = opening,
            ClosingBalanceMinor = closing
        };
    }

    private static long ParseSignedAmount(string[] fields, ColumnIndex index)
    {
        if (index.Amount >= 0)
            return ParseAmount(RequiredField(fields, index.Amount, 0));

        long debit = index.Debit >= 0 ? Math.Abs(ParseAmount(OptionalField(fields, index.Debit) ?? "0")) : 0;
        long credit = index.Credit >= 0 ? Math.Abs(ParseAmount(OptionalField(fields, index.Credit) ?? "0")) : 0;
        return credit - debit;
    }

    private static string RequiredField(string[] fields, int index, int lineNo)
    {
        if (index < 0 || index >= fields.Length)
            throw new InvalidDataException($"Line {lineNo} is missing a required statement field.");
        return fields[index];
    }

    private static string? OptionalField(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index] : null;

    private static DateOnly ParseDate(string text, int lineNo)
    {
        string[] formats = { "yyyy-MM-dd", "yyyyMMdd", "MM/dd/yyyy", "M/d/yyyy", "dd/MM/yyyy", "d/M/yyyy" };
        if (DateOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var date)
            || DateOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out date))
            return date;

        throw new InvalidDataException($"Line {lineNo} contains an invalid date: '{text}'.");
    }

    private static long ParseAmount(string text)
    {
        text = text.Trim().Replace("$", "", StringComparison.Ordinal)
                         .Replace("€", "", StringComparison.Ordinal)
                         .Replace("£", "", StringComparison.Ordinal)
                         .Replace("â‚¬", "", StringComparison.Ordinal)
                         .Replace("Â£", "", StringComparison.Ordinal)
                         .Trim();
        if (text.Length == 0)
            return 0;

        bool parentheses = text.StartsWith('(') && text.EndsWith(')');
        if (parentheses)
            text = text[1..^1];

        int lastDot = text.LastIndexOf('.');
        int lastComma = text.LastIndexOf(',');
        bool hasDot = lastDot >= 0;
        bool hasComma = lastComma >= 0;
        int decimalPos = -1;
        if (hasDot && hasComma)
            decimalPos = Math.Max(lastDot, lastComma);
        else if (hasDot || hasComma)
        {
            int separatorPos = hasDot ? lastDot : lastComma;
            int digitsAfter = text.Length - separatorPos - 1;
            if (digitsAfter != 3 || !HasLeadingGrouping(text, separatorPos))
                decimalPos = separatorPos;
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

        if (sb.Length == 0)
            throw new InvalidDataException($"Invalid amount '{text}'.");

        decimal value = decimal.Parse(sb.ToString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
        if (parentheses)
            value = -value;
        return checked((long)Math.Round(value * 100m, MidpointRounding.ToEven));
    }

    private static bool HasLeadingGrouping(string text, int separatorPos) => separatorPos > 0;

    private static ColumnIndex BuildIndex(IReadOnlyList<string> headers)
    {
        var normalised = headers.Select(NormalizeHeader).ToList();
        int Find(params string[] names) => normalised.FindIndex(name => names.Contains(name, StringComparer.OrdinalIgnoreCase));

        int date = Find("date", "transactiondate", "posteddate", "postingdate", "valuedate");
        int description = Find("description", "details", "detail", "memo", "name", "payee");
        int amount = Find("amount", "transactionamount", "signedamount");
        int debit = Find("debit", "withdrawal", "withdrawals", "outflow");
        int credit = Find("credit", "deposit", "deposits", "inflow");

        if (date < 0)
            throw new InvalidDataException("The CSV header must contain a date column.");
        if (description < 0)
            throw new InvalidDataException("The CSV header must contain a description column.");
        if (amount < 0 && debit < 0 && credit < 0)
            throw new InvalidDataException("The CSV header must contain an amount, debit, or credit column.");

        return new ColumnIndex(
            date,
            Find("reference", "ref", "fitid", "transactionid", "checknumber", "checknum"),
            description,
            amount,
            debit,
            credit,
            Find("balance", "runningbalance", "ledgerbalance", "availablebalance"),
            Find("currency", "currencycode"));
    }

    private static string NormalizeHeader(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static bool HasAny(HashSet<string> headers, params string[] names) =>
        names.Any(headers.Contains);

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

    private sealed record ColumnIndex(
        int Date,
        int Reference,
        int Description,
        int Amount,
        int Debit,
        int Credit,
        int Balance,
        int Currency)
    {
        public int RequiredField => Math.Max(Date, Math.Max(Description, Math.Max(Amount, Math.Max(Debit, Credit))));
    }
}