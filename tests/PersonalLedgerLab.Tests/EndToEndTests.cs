using PersonalLedgerLab.Core;
using PersonalLedgerLab.Data;

namespace PersonalLedgerLab.Tests;

public class EndToEndTests : IDisposable
{
    private readonly string _path;
    private readonly SqliteLedgerStore _store;

    public EndToEndTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"pll-e2e-{Guid.NewGuid():N}.sqlite");
        _store = new SqliteLedgerStore(_path);
    }

    private static string SamplePath =>
        Path.Combine(AppContext.BaseDirectory, "samples", "sample-statement.csv");

    [Fact]
    public void Sample_statement_import_balance_and_summary_match()
    {
        var accounts = _store.LoadAccounts();
        var rules = _store.LoadRules();
        var byId = accounts.ToDictionary(a => a.Id);
        var bank = accounts.First(a => a.Code == "1000");
        var suspense = accounts.First(a => a.Code == "3999");

        var rows = new CsvStatementImporter().ReadFile(SamplePath);
        var book = new LedgerBook();
        foreach (var a in accounts)
            book.AddAccount(a);

        var result = new LedgerImportRunner().Import(book, bank, suspense, rows, rules, byId);

        Assert.Equal(15, result.TransactionCount);
        Assert.True(result.Reconciles);
        Assert.Equal(1000_00L, result.OpeningBalanceMinor);
        Assert.Equal(1807_48L, result.ActualClosingBalanceMinor);
        Assert.Empty(result.Warnings);

        foreach (var tx in book.Transactions)
            _store.SaveTransaction(tx);

        var loaded = _store.LoadTransactions();
        Assert.Equal(15, loaded.Count);
        Assert.All(loaded, t => Assert.True(t.IsBalanced));

        var summaries = SummaryService.Build(loaded, accounts, "USD");
        var jan = Assert.Single(summaries);
        Assert.Equal(1500_00L, jan.IncomeMinor);
        Assert.True(jan.ExpenseMinor > 600_00L && jan.ExpenseMinor < 700_00L);

        var grocery = jan.Items.Single(i => i.AccountCode == "5000");
        Assert.Equal(203_08L, grocery.NetMinor);
        var coffee = jan.Items.Single(i => i.AccountCode == "5200");
        Assert.Equal(15_25L, coffee.NetMinor);
    }

    [Fact]
    public void Sample_import_attaches_rule_explanations()
    {
        var accounts = _store.LoadAccounts();
        var rules = _store.LoadRules();
        var bank = accounts.First(a => a.Code == "1000");
        var suspense = accounts.First(a => a.Code == "3999");
        var rows = new CsvStatementImporter().ReadFile(SamplePath);
        var book = new LedgerBook();
        foreach (var a in accounts)
            book.AddAccount(a);

        new LedgerImportRunner().Import(book, bank, suspense, rows, rules, accounts.ToDictionary(a => a.Id));

        var salaryTx = book.Transactions.Single(t => t.Description.StartsWith("Salary"));
        Assert.NotNull(salaryTx.AppliedRuleId);
        Assert.Contains("Salary", salaryTx.RuleExplanation!);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_path); } catch { }
    }
}