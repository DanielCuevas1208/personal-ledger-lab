using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PersonalLedgerLab.Core;

/// <summary>
/// Reads common OFX 1.x and 2.x bank statement responses.
/// </summary>
public sealed class OfxStatementImporter : IStatementImporter
{
    private static readonly Regex TagPattern = new(
        @"<(?<name>[A-Za-z][A-Za-z0-9_]*)(?:\s[^>]*)?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ContainerTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "OFX", "BANKMSGSRSV1", "STMTTRNRS", "STMTRS", "BANKACCTFROM",
        "BANKTRANLIST", "STMTTRN", "LEDGERBAL", "AVAILBAL", "CREDITCARDMSGSRSV1",
        "CCSTMTTRNRS", "CCSTMTRS", "CCACCTFROM", "CCSTMTTRN"
    };

    public StatementFormat Format => StatementFormat.Ofx;

    public bool CanRead(string contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
            return false;

        var trimmed = contents.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return trimmed.StartsWith("OFXHEADER:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<OFX", StringComparison.OrdinalIgnoreCase);
    }

    public ImportedStatement ReadStatement(TextReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        string contents = reader.ReadToEnd();
        if (!CanRead(contents))
            throw new InvalidDataException("The file does not contain an OFX document.");

        XDocument document;
        try
        {
            document = XDocument.Parse(ToXml(contents), LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException)
        {
            throw new InvalidDataException("The OFX document is not valid XML or OFX SGML.", ex);
        }

        var rows = ReadRows(document);
        if (rows.Count == 0)
            throw new InvalidDataException("The OFX document contains no bank transactions.");

        string currency = Value(document, "CURDEF")?.ToUpperInvariant() ?? "USD";
        foreach (var row in rows)
            row.CurrencyCode = currency;

        long? closing = ReadClosingBalance(document);
        long? opening = null;
        bool inferred = false;
        if (closing.HasValue)
        {
            opening = checked(closing.Value - rows.Sum(r => r.AmountMinor));
            inferred = true;
        }

        return new ImportedStatement
        {
            FormatName = "OFX",
            Source = "ofx-import",
            Rows = rows,
            OpeningBalanceMinor = opening,
            ClosingBalanceMinor = closing,
            OpeningBalanceIsInferred = inferred
        };
    }

    private static List<ImportedRow> ReadRows(XDocument document)
    {
        var rows = new List<ImportedRow>();
        foreach (var element in document.Descendants().Where(e => NameIs(e, "STMTTRN")))
        {
            string amountText = Value(element, "TRNAMT")
                ?? throw new InvalidDataException("An OFX transaction is missing TRNAMT.");
            rows.Add(new ImportedRow
            {
                Date = ParseDate(Value(element, "DTPOSTED")
                    ?? throw new InvalidDataException("An OFX transaction is missing DTPOSTED.")),
                Reference = FirstValue(element, "FITID", "REFNUM", "CHECKNUM"),
                Description = BuildDescription(Value(element, "NAME"), Value(element, "MEMO")),
                AmountMinor = ParseAmount(amountText)
            });
        }

        return rows;
    }

    private static long? ReadClosingBalance(XDocument document)
    {
        var ledgerBalance = document.Descendants().FirstOrDefault(e => NameIs(e, "LEDGERBAL"));
        var text = ledgerBalance is null ? null : Value(ledgerBalance, "BALAMT");
        return string.IsNullOrWhiteSpace(text) ? null : ParseAmount(text);
    }

    private static string BuildDescription(string? name, string? memo)
    {
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
        if (name is null) return memo ?? "OFX transaction";
        if (memo is null || string.Equals(name, memo, StringComparison.OrdinalIgnoreCase)) return name;
        return $"{name} | {memo}";
    }

    private static DateOnly ParseDate(string text)
    {
        string value = text.Trim();
        if (value.Length < 8 || !DateOnly.TryParseExact(value[..8], "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException($"Invalid OFX transaction date '{text}'.");
        return date;
    }

    private static long ParseAmount(string text)
    {
        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"Invalid OFX amount '{text}'.");
        return checked((long)Math.Round(value * 100m, MidpointRounding.ToEven));
    }

    private static string? FirstValue(XElement element, params string[] names) =>
        names.Select(name => Value(element, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? Value(XContainer container, string name) =>
        container.Descendants().FirstOrDefault(e => NameIs(e, name))?.Value.Trim();

    private static bool NameIs(XElement element, string name) =>
        string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    private static bool CanContain(string parent, string child) => parent.ToUpperInvariant() switch
    {
        "OFX" => child.Equals("BANKMSGSRSV1", StringComparison.OrdinalIgnoreCase)
            || child.Equals("CREDITCARDMSGSRSV1", StringComparison.OrdinalIgnoreCase),
        "BANKMSGSRSV1" => child.Equals("STMTTRNRS", StringComparison.OrdinalIgnoreCase),
        "STMTTRNRS" => child.Equals("STMTRS", StringComparison.OrdinalIgnoreCase),
        "STMTRS" => child.Equals("BANKACCTFROM", StringComparison.OrdinalIgnoreCase)
            || child.Equals("BANKTRANLIST", StringComparison.OrdinalIgnoreCase)
            || child.Equals("LEDGERBAL", StringComparison.OrdinalIgnoreCase)
            || child.Equals("AVAILBAL", StringComparison.OrdinalIgnoreCase),
        "BANKTRANLIST" => child.Equals("STMTTRN", StringComparison.OrdinalIgnoreCase),
        "CREDITCARDMSGSRSV1" => child.Equals("CCSTMTTRNRS", StringComparison.OrdinalIgnoreCase),
        "CCSTMTTRNRS" => child.Equals("CCSTMTRS", StringComparison.OrdinalIgnoreCase),
        "CCSTMTRS" => child.Equals("CCACCTFROM", StringComparison.OrdinalIgnoreCase)
            || child.Equals("BANKTRANLIST", StringComparison.OrdinalIgnoreCase)
            || child.Equals("LEDGERBAL", StringComparison.OrdinalIgnoreCase)
            || child.Equals("AVAILBAL", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static string ToXml(string contents)
    {
        int start = contents.IndexOf("<OFX", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            throw new InvalidDataException("The OFX document has no OFX root element.");

        string body = contents[start..].Trim();
        try
        {
            XDocument.Parse(body);
            return body;
        }
        catch (System.Xml.XmlException)
        {
            // OFX 1.x uses SGML tags without closing tags.
        }

        body = Regex.Replace(body, @"</[A-Za-z][A-Za-z0-9_]*\s*>", string.Empty);
        var matches = TagPattern.Matches(body);
        if (matches.Count == 0)
            throw new InvalidDataException("The OFX document has no readable tags.");

        var xml = new StringBuilder();
        var stack = new Stack<string>();
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            string name = match.Groups["name"].Value;
            if (ContainerTags.Contains(name))
            {
                while (stack.Count > 0 && !CanContain(stack.Peek(), name))
                    xml.Append("</").Append(stack.Pop()).Append('>');
                xml.Append('<').Append(name).Append('>');
                stack.Push(name);
                continue;
            }

            int valueStart = match.Index + match.Length;
            int valueEnd = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            string value = body[valueStart..valueEnd].Trim();
            xml.Append('<').Append(name).Append('>')
                .Append(SecurityElement.Escape(value))
                .Append("</").Append(name).Append('>');
        }

        while (stack.Count > 0)
            xml.Append("</").Append(stack.Pop()).Append('>');
        return xml.ToString();
    }
}