using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class StatementImporterTests
{
    private const string OfxStatement = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII
        CHARSET:1252
        <OFX>
        <BANKMSGSRSV1>
        <STMTTRNRS>
        <STMTRS>
        <CURDEF>USD
        <BANKACCTFROM>
        <BANKID>000000000
        <ACCTID>1000000000
        <BANKTRANLIST>
        <STMTTRN>
        <TRNTYPE>CREDIT
        <DTPOSTED>20250103120000
        <TRNAMT>1500.00
        <FITID>20250103-1
        <NAME>Salary ACME Corp
        <MEMO>Payroll
        <STMTTRN>
        <TRNTYPE>DEBIT
        <DTPOSTED>20250104120000
        <TRNAMT>-84.37
        <FITID>20250104-1
        <NAME>SuperValu groceries
        <MEMO>Store 47
        <LEDGERBAL>
        <BALAMT>2415.63
        <DTASOF>20250104120000
        </STMTRS>
        </STMTTRNRS>
        </BANKMSGSRSV1>
        </OFX>
        """;

    private const string XmlOfxStatement = """
        <OFX>
          <BANKMSGSRSV1>
            <STMTTRNRS>
              <STMTRS>
                <CURDEF>USD</CURDEF>
                <BANKTRANLIST>
                  <STMTTRN>
                    <TRNTYPE>DEBIT</TRNTYPE>
                    <DTPOSTED>20250105120000</DTPOSTED>
                    <TRNAMT>-5.00</TRNAMT>
                    <FITID>xml-1</FITID>
                    <NAME>Coffee</NAME>
                  </STMTTRN>
                </BANKTRANLIST>
                <LEDGERBAL>
                  <BALAMT>995.00</BALAMT>
                  <DTASOF>20250105120000</DTASOF>
                </LEDGERBAL>
              </STMTRS>
            </STMTTRNRS>
          </BANKMSGSRSV1>
        </OFX>
        """;
    [Fact]
    public void Csv_aliases_support_debit_and_credit_columns()
    {
        const string csv =
            "Transaction Date,Transaction ID,Details,Debit,Credit,Running Balance,Currency Code\n" +
            "01/03/2025,one,Salary,,1500.00,2500.00,USD\n" +
            "01/04/2025,two,Market,84.37,,2415.63,USD\n";

        var statement = new StatementImporterRegistry().Read(csv);

        Assert.Equal(StatementFormat.Csv, new CsvStatementImporter().Format);
        Assert.Equal(2, statement.Rows.Count);
        Assert.Equal(150000L, statement.Rows[0].AmountMinor);
        Assert.Equal(-8437L, statement.Rows[1].AmountMinor);
        Assert.Equal(100000L, statement.OpeningBalanceMinor);
        Assert.Equal(241563L, statement.ClosingBalanceMinor);
    }

    [Fact]
    public void Registry_auto_detects_ofx_sgml()
    {
        var statement = new StatementImporterRegistry().Read(OfxStatement);

        Assert.Equal("OFX", statement.FormatName);
        Assert.Equal("ofx-import", statement.Source);
        Assert.Equal(2, statement.Rows.Count);
        Assert.Equal(new DateOnly(2025, 1, 3), statement.Rows[0].Date);
        Assert.Equal("20250103-1", statement.Rows[0].Reference);
        Assert.Equal("Salary ACME Corp | Payroll", statement.Rows[0].Description);
        Assert.Equal(-8437L, statement.Rows[1].AmountMinor);
        Assert.Equal(100000L, statement.OpeningBalanceMinor);
        Assert.True(statement.OpeningBalanceIsInferred);
    }

    [Fact]
    public void Registry_can_force_ofx_format()
    {
        var statement = new StatementImporterRegistry().Read(OfxStatement, StatementFormat.Ofx);

        Assert.Equal(StatementFormat.Ofx, new OfxStatementImporter().Format);
        Assert.Equal(241563L, statement.ClosingBalanceMinor);
    }

    [Fact]
    public void Registry_reads_ofx_xml()
    {
        var statement = new StatementImporterRegistry().Read(XmlOfxStatement);

        Assert.Single(statement.Rows);
        Assert.Equal(-500L, statement.Rows[0].AmountMinor);
        Assert.Equal(100000L, statement.OpeningBalanceMinor);
        Assert.Equal(99500L, statement.ClosingBalanceMinor);
    }

    [Fact]
    public void Registry_rejects_unknown_format()
    {
        Assert.Throws<InvalidDataException>(() => new StatementImporterRegistry().Read("not a statement"));
    }
}