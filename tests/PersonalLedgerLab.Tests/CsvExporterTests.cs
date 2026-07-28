using System.Text;
using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class CsvExporterTests
{
    private static List<Account> Accounts() => new()
    {
        new(1, "1000", "Checking", AccountType.Asset, "USD"),
        new(2, "5000", "Groceries", AccountType.Expense, "USD")
    };

    private static List<Transaction> Transactions() => new()
    {
        new Transaction(1, new DateOnly(2025, 1, 4), "SuperValu", null, "csv")
        {
            AppliedRuleId = 7,
            RuleExplanation = "Rule 'Groceries' (priority 10) matched.",
            Postings =
            {
                new Posting(10, 1, 1, -50_00, "USD"),
                new Posting(11, 1, 2, 50_00, "USD")
            }
        }
    };

    private static string ExportLedger()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        CsvExporter.ExportLedger(Transactions(), Accounts(), writer);
        return sb.ToString();
    }

    [Fact]
    public void Ledger_export_has_header_row()
    {
        var csv = ExportLedger();
        var first = csv.Split(Environment.NewLine)[0];
        Assert.Contains("TransactionId", first);
        Assert.Contains("DebitMinor", first);
    }

    [Fact]
    public void Ledger_export_emits_one_row_per_posting()
    {
        var lines = ExportLedger()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("SuperValu", lines[1]);
        Assert.Contains("Groceries", lines[2]);
        Assert.Contains("Rule 'Groceries'", lines[1]);
    }

    [Fact]
    public void Account_export_emits_all_codes()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        CsvExporter.ExportAccounts(Accounts(), writer);
        var lines = sb.ToString().Split(Environment.NewLine);
        Assert.Contains(lines, l => l.StartsWith("1,1000,Checking"));
    }

    [Fact]
    public void Rule_export_quotes_commas()
    {
        var rules = new List<CategoryRule>
        {
            new(1, "Coffee, cold", 1, RuleMatchType.DescriptionContains, "latte", 2, "5200")
        };
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        CsvExporter.ExportRules(rules, writer);
        Assert.Contains("\"Coffee, cold\"", sb.ToString());
    }
}