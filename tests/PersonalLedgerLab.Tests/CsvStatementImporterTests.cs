using System.Globalization;
using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class CsvStatementImporterTests
{
    private const string Sample =
        "Date,Reference,Description,Amount,Balance,Currency\r\n" +
        "2025-01-03,PAYROLL,Salary ACME Corp,1500.00,2500.00,USD\r\n" +
        "2025-01-04,,SuperValu groceries,-84.37,2415.63,USD\r\n";

    private static List<ImportedRow> Read(string csv)
    {
        using var reader = new StringReader(csv);
        return new CsvStatementImporter().Read(reader).ToList();
    }

    [Fact]
    public void Reads_header_and_rows()
    {
        var rows = Read(Sample);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2025, 1, 3), rows[0].Date);
        Assert.Equal("PAYROLL", rows[0].Reference);
        Assert.Equal("Salary ACME Corp", rows[0].Description);
    }

    [Theory]
    [InlineData("1500.00", 150000)]
    [InlineData("-84.37", -8437)]
    [InlineData("$1,234.56", 123456)]
    [InlineData("1.234,56", 123456)]
    public void Parses_amount_in_minor_units(string text, long expected)
    {
        var rows = Read(
            "Date,Reference,Description,Amount,Balance,Currency\r\n" +
            $"2025-01-03,,Test,\"{text}\",1.00,USD\r\n");
        Assert.Equal(expected, rows[0].AmountMinor);
    }

    [Fact]
    public void Missing_balance_column_is_optional()
    {
        var rows = Read(
            "Date,Reference,Description,Amount\r\n" +
            "2025-01-03,R1,Test,10.00\r\n");
        Assert.Null(rows[0].RunningBalanceMinor);
    }

    [Fact]
    public void Quoted_fields_stay_intact()
    {
        var rows = Read(
            "Date,Reference,Description,Amount,Balance,Currency\r\n" +
            "2025-01-03,\"A, B\",\"Has, comma\",1.00,2.00,USD\r\n");
        Assert.Equal("Has, comma", rows[0].Description);
        Assert.Equal("A, B", rows[0].Reference);
    }

    [Fact]
    public void Missing_date_column_throws()
    {
        using var reader = new StringReader("Reference,Description,Amount\r\nR,T,1.00\r\n");
        Assert.Throws<InvalidDataException>(() => new CsvStatementImporter().Read(reader));
    }

    [Fact]
    public void Empty_lines_are_ignored()
    {
        var rows = Read(Sample + "\r\n\r\n");
        Assert.Equal(2, rows.Count);
    }
}