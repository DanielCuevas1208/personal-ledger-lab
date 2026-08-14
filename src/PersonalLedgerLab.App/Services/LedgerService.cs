using System.Globalization;
using System.Text;
using PersonalLedgerLab.Core;
using PersonalLedgerLab.Data;

namespace PersonalLedgerLab.App.Services;

/// <summary>
/// Facade that coordinates persistence, import, rules, summary and export.
/// The view-model calls only this class so the UI stays free of storage detail.
/// </summary>
public sealed class LedgerService
{
    private readonly ILedgerStore _store;
    private readonly LedgerImportRunner _importRunner = new();
    private readonly StatementImporterRegistry _importers = new();
    private readonly ConsolidatedBalanceService _consolidated = new();

    public LedgerService(ILedgerStore store)
    {
        _store = store ?? SqliteLedgerStoreFactory.CreateDefault();
        RefreshCache();
    }

    public IReadOnlyList<Account> Accounts { get; private set; } = Array.Empty<Account>();
    public IReadOnlyList<CategoryRule> Rules { get; private set; } = Array.Empty<CategoryRule>();
    public IReadOnlyList<Transaction> Transactions { get; private set; } = Array.Empty<Transaction>();
    public IReadOnlyList<Budget> Budgets { get; private set; } = Array.Empty<Budget>();
    public IReadOnlyList<ExchangeRate> ExchangeRates { get; private set; } = Array.Empty<ExchangeRate>();
    public string BaseCurrencyCode => _store.BaseCurrencyCode;

    public bool IsFreshDatabase => _store.IsFreshDatabase;
    public string DatabasePath => _store.DatabasePath;

    public Account GetAccount(long id) =>
        Accounts.First(a => a.Id == id);

    public Account? GetAccountByCode(string code) =>
        Accounts.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.Ordinal));

    public IReadOnlyList<Account> CategorizationTargets =>
        Accounts.Where(a => a.Type == AccountType.Expense || a.Type == AccountType.Income)
            .OrderBy(a => a.Code).ToList();

    public IReadOnlyList<Account> AssetAccounts =>
        Accounts.Where(a => a.Type == AccountType.Asset).OrderBy(a => a.Code).ToList();

    public void RefreshCache()
    {
        Accounts = _store.LoadAccounts();
        Rules = _store.LoadRules();
        Transactions = _store.LoadTransactions();
        Budgets = _store.LoadBudgets();
        ExchangeRates = _store.LoadExchangeRates();
    }

    public StatementImportResult ImportStatement(string csvPath, long bankAccountId) =>
        ImportStatement(csvPath, bankAccountId, StatementFormat.Auto);

    public StatementImportResult ImportStatement(string statementPath, long bankAccountId, StatementFormat format)
    {
        var bankAccount = Accounts.First(a => a.Id == bankAccountId);
        var suspense = Accounts.First(a => a.Code == "3999");
        var categoryById = Accounts.ToDictionary(a => a.Id);
        var statement = _importers.ReadFile(statementPath, format);

        var book = new LedgerBook();
        foreach (var account in Accounts)
            book.AddAccount(account);

        var result = _importRunner.Import(
            book, bankAccount, suspense, statement, Rules, categoryById, ExchangeRates);

        foreach (var tx in book.Transactions)
            _store.SaveTransaction(tx);

        RefreshCache();
        return result;
    }

    public LedgerBook BuildBook()
    {
        var book = new LedgerBook();
        foreach (var account in Accounts)
            book.AddAccount(account);
        foreach (var tx in Transactions)
            book.AttachTransaction(tx);
        return book;
    }

    public long AccountBalance(long accountId)
    {
        var account = Accounts.First(a => a.Id == accountId);
        long raw = Transactions.SelectMany(t => t.Postings)
            .Where(p => p.AccountId == accountId)
            .Sum(p => p.AmountMinor);
        return account.IsDebitNormal ? raw : -raw;
    }

    public long NetWorth() => ConsolidatedNetWorth();

    public long ConsolidatedNetWorth(DateOnly? asOfDate = null) =>
        BuildConsolidatedBalance(asOfDate).NetWorthMinor;

    public ConsolidatedBalanceReport BuildConsolidatedBalance(DateOnly? asOfDate = null) =>
        _consolidated.Build(Transactions, Accounts, ExchangeRates, BaseCurrencyCode, asOfDate);

    public IReadOnlyList<MonthlySummary> BuildSummary(string? currency = null) =>
        SummaryService.Build(Transactions, Accounts, currency ?? BaseCurrencyCode);

    public long AddRule(string name, int priority, RuleMatchType matchType, string matchValue, long categoryId)
    {
        var account = Accounts.First(a => a.Id == categoryId);
        var rule = new CategoryRule
        {
            Name = name,
            Priority = priority,
            MatchType = matchType,
            MatchValue = matchValue,
            CategoryAccountId = categoryId,
            CategoryAccountCode = account.Code
        };
        var id = _store.AddRule(rule);
        RefreshCache();
        return id;
    }

    public bool UpdateRule(long id, string name, int priority, RuleMatchType matchType,
        string matchValue, long categoryId)
    {
        var account = Accounts.First(a => a.Id == categoryId);
        var rule = new CategoryRule
        {
            Id = id,
            Name = name,
            Priority = priority,
            MatchType = matchType,
            MatchValue = matchValue,
            CategoryAccountId = categoryId,
            CategoryAccountCode = account.Code
        };
        var ok = _store.UpdateRule(rule);
        RefreshCache();
        return ok;
    }

    public bool DeleteRule(long id)
    {
        var ok = _store.DeleteRule(id);
        RefreshCache();
        return ok;
    }

    public RuleResult PreviewCategorization(string description, string? reference)
    {
        var draft = new Transaction(0, DateOnly.MinValue, description, reference, "preview");
        var engine = new RuleEngine();
        return engine.Categorize(draft, Rules);
    }

    public long AddBudget(string name, long categoryAccountId, int year, int month, long limitMinor, string? note)
    {
        var account = Accounts.First(a => a.Id == categoryAccountId);
        var budget = new Budget
        {
            Name = name,
            CategoryAccountId = categoryAccountId,
            CategoryAccountCode = account.Code,
            Year = year,
            Month = month,
            LimitMinor = limitMinor,
            CurrencyCode = account.CurrencyCode,
            Note = note
        };
        var id = _store.AddBudget(budget);
        RefreshCache();
        return id;
    }

    public bool UpdateBudget(long id, string name, long categoryAccountId, int year, int month, long limitMinor, string? note)
    {
        var account = Accounts.First(a => a.Id == categoryAccountId);
        var budget = new Budget
        {
            Id = id,
            Name = name,
            CategoryAccountId = categoryAccountId,
            CategoryAccountCode = account.Code,
            Year = year,
            Month = month,
            LimitMinor = limitMinor,
            CurrencyCode = account.CurrencyCode,
            Note = note
        };
        var ok = _store.UpdateBudget(budget);
        RefreshCache();
        return ok;
    }

    public bool DeleteBudget(long id)
    {
        var ok = _store.DeleteBudget(id);
        RefreshCache();
        return ok;
    }

    public IReadOnlyList<BudgetVariance> BuildBudgetVariance(string? currency = null) =>
        BudgetService.Build(BuildSummary(currency), Budgets, Accounts, currency ?? BaseCurrencyCode);

    public void ExportLedger(string path)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        CsvExporter.ExportLedger(Transactions, Accounts, writer);
    }

    public void ExportSummary(string path)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        CsvExporter.ExportSummary(BuildSummary(), writer);
    }

    public void ExportBudgets(string path)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        CsvExporter.ExportBudgets(Budgets, Accounts, writer);
    }

    public void ExportBudgetVariance(string path)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        CsvExporter.ExportBudgetVariance(BuildBudgetVariance(), writer);
    }

    public void ExportConsolidatedBalances(string path, DateOnly? asOfDate = null)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        CsvExporter.ExportConsolidatedBalances(BuildConsolidatedBalance(asOfDate), writer);
    }

    public string StartupReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Personal Ledger Lab");
        sb.AppendLine($"Database: {DatabasePath}");
        sb.AppendLine($"Accounts: {Accounts.Count}, Rules: {Rules.Count}, Transactions: {Transactions.Count}");
        sb.AppendLine($"Exchange rates: {ExchangeRates.Count}, Base currency: {BaseCurrencyCode}");
        sb.AppendLine($"Fresh database: {IsFreshDatabase}");
        return sb.ToString();
    }

    public static string FormatMinor(long minor) =>
        (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}