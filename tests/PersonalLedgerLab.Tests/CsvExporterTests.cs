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

    [Fact]
    public void Budget_export_has_header_and_rows()
    {
        var accounts = new List<Account>
        {
            new(3, "5000", "Groceries", AccountType.Expense, "USD")
        };
        var budgets = new List<Budget>
        {
            new(1, 3, "5000", "Groceries cap", 2025, 1, 250_00L, "USD", "note")
        };
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        CsvExporter.ExportBudgets(budgets, accounts, writer);
        var lines = sb.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("LimitMajor", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Groceries", lines[1]);
        Assert.Contains("250.00", lines[1]);
        Assert.Contains("False", lines[1]);
    }

    [Fact]
    public void Budget_variance_export_emits_explanation()
    {
        var rows = new List<BudgetVariance>
        {
            new()
            {
                Year = 2025, Month = 1,
                AccountId = 3, AccountCode = "5000", AccountName = "Groceries", AccountType = AccountType.Expense,
                LimitMinor = 250_00L, ActualMinor = 203_08L, VarianceMinor = -46_92L,
                IsRecurring = false, CurrencyCode = "USD",
                Explanation = "Spent 203.08 USD against a 250.00 USD limit for 5000 Groceries in 2025-01. The result is 46.92 USD under budget."
            }
        };
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        CsvExporter.ExportBudgetVariance(rows, writer);
        var csv = sb.ToString();
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("VarianceMajor", lines[0]);
        Assert.Contains("5000", lines[1]);
        Assert.Contains("-46.92", lines[1]);
        Assert.Contains("under budget", lines[1]);
    }
}