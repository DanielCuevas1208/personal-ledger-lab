using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PersonalLedgerLab.App.Services;
using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LedgerService _service;
    private AccountRow? _selectedAssetAccount;
    private string _status = string.Empty;
    private string _detail = string.Empty;
    private string _importFilePath = string.Empty;
    private StatementFormat _importFormat = StatementFormat.Auto;
    private long _netWorthMinor;
    private long _latestMonthIncomeMinor;
    private long _latestMonthExpenseMinor;
    private string _latestMonthLabel = "—";

    private string _editorName = string.Empty;
    private int _editorPriority = 100;
    private RuleMatchType _editorMatchType = RuleMatchType.DescriptionContains;
    private string _editorMatchValue = string.Empty;
    private Account? _editorCategoryAccount;
    private long? _editingRuleId;

    private string _previewDescription = string.Empty;
    private string _previewReference = string.Empty;
    private string _previewResult = "Enter a description to test the active rules.";

    private string _budgetName = string.Empty;
    private Account? _budgetCategoryAccount;
    private int _budgetYear = 2025;
    private int _budgetMonth = 1;
    private decimal _budgetLimitMajor = 100m;
    private long? _editingBudgetId;
    private string _budgetNote = string.Empty;

    public MainViewModel() : this(new LedgerService(null!)) { }

    public MainViewModel(LedgerService service)
    {
        _service = service ?? new LedgerService(SqliteLedgerStoreAsService());
        ImportCommand = new RelayCommand(ExecuteImport, () => !string.IsNullOrWhiteSpace(ImportFilePath) && SelectedAssetAccount is not null);
        ExportLedgerCommand = new RelayCommand(ExecuteExportLedger, () => Transactions.Count > 0);
        ExportSummaryCommand = new RelayCommand(ExecuteExportSummary, () => Transactions.Count > 0);
        SaveRuleCommand = new RelayCommand(ExecuteSaveRule, CanSaveRule);
        DeleteRuleCommand = new RelayCommand(ExecuteDeleteRule, () => EditingRuleId.HasValue);
        PreviewCommand = new RelayCommand(ExecutePreview, () => true);
        ClearImportPathCommand = new RelayCommand(() => ImportFilePath = string.Empty);
        SaveBudgetCommand = new RelayCommand(ExecuteSaveBudget, CanSaveBudget);
        DeleteBudgetCommand = new RelayCommand(ExecuteDeleteBudget, () => EditingBudgetId.HasValue);
        ExportBudgetsCommand = new RelayCommand(ExecuteExportBudgets, () => Budgets.Count > 0);
        ExportBudgetVarianceCommand = new RelayCommand(ExecuteExportBudgetVariance, () => Transactions.Count > 0 && Budgets.Count > 0);

        Status = _service.StartupReport();
        Load();
    }

    public ObservableCollection<AccountRow> Accounts { get; } = new();
    public ObservableCollection<RuleRow> Rules { get; } = new();
    public ObservableCollection<TransactionRow> Transactions { get; } = new();
    public ObservableCollection<SummaryRow> Summary { get; } = new();
    public ObservableCollection<MonthlyTotalRow> MonthlyTotals { get; } = new();
    public ObservableCollection<Account> CategoryAccounts { get; } = new();
    public ObservableCollection<AccountRow> AssetAccounts { get; } = new();
    public ObservableCollection<BudgetRow> Budgets { get; } = new();
    public ObservableCollection<BudgetVarianceRow> BudgetVariance { get; } = new();

    public ICommand ImportCommand { get; }
    public ICommand ExportLedgerCommand { get; }
    public ICommand ExportSummaryCommand { get; }
    public ICommand SaveRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ClearImportPathCommand { get; }
    public ICommand SaveBudgetCommand { get; }
    public ICommand DeleteBudgetCommand { get; }
    public ICommand ExportBudgetsCommand { get; }
    public ICommand ExportBudgetVarianceCommand { get; }

    public AccountRow? SelectedAssetAccount
    {
        get => _selectedAssetAccount;
        set { _selectedAssetAccount = value; OnPropertyChanged(); }
    }

    public string ImportFilePath
    {
        get => _importFilePath;
        set { _importFilePath = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public StatementFormat ImportFormat
    {
        get => _importFormat;
        set { _importFormat = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public string Detail
    {
        get => _detail;
        private set { _detail = value; OnPropertyChanged(); }
    }

    public long NetWorthMinor
    {
        get => _netWorthMinor;
        private set { _netWorthMinor = value; OnPropertyChanged(); OnPropertyChanged(nameof(NetWorthLabel)); }
    }

    public string NetWorthLabel => LedgerService.FormatMinor(NetWorthMinor) + " USD";

    public string LatestMonthLabel
    {
        get => _latestMonthLabel;
        private set { _latestMonthLabel = value; OnPropertyChanged(); }
    }

    public long LatestMonthIncomeMinor
    {
        get => _latestMonthIncomeMinor;
        private set { _latestMonthIncomeMinor = value; OnPropertyChanged(); OnPropertyChanged(nameof(LatestMonthIncomeLabel)); }
    }

    public long LatestMonthExpenseMinor
    {
        get => _latestMonthExpenseMinor;
        private set { _latestMonthExpenseMinor = value; OnPropertyChanged(); OnPropertyChanged(nameof(LatestMonthExpenseLabel)); }
    }

    public string LatestMonthIncomeLabel => LedgerService.FormatMinor(LatestMonthIncomeMinor);
    public string LatestMonthExpenseLabel => LedgerService.FormatMinor(LatestMonthExpenseMinor);

    public string EditorName
    {
        get => _editorName;
        set { _editorName = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public int EditorPriority
    {
        get => _editorPriority;
        set { _editorPriority = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public RuleMatchType EditorMatchType
    {
        get => _editorMatchType;
        set { _editorMatchType = value; OnPropertyChanged(); }
    }

    public string EditorMatchValue
    {
        get => _editorMatchValue;
        set { _editorMatchValue = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public Account? EditorCategoryAccount
    {
        get => _editorCategoryAccount;
        set { _editorCategoryAccount = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public long? EditingRuleId
    {
        get => _editingRuleId;
        set { _editingRuleId = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public string PreviewDescription
    {
        get => _previewDescription;
        set { _previewDescription = value; OnPropertyChanged(); }
    }

    public string PreviewReference
    {
        get => _previewReference;
        set { _previewReference = value; OnPropertyChanged(); }
    }

    public string PreviewResult
    {
        get => _previewResult;
        private set { _previewResult = value; OnPropertyChanged(); }
    }

    public string BudgetName
    {
        get => _budgetName;
        set { _budgetName = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public Account? BudgetCategoryAccount
    {
        get => _budgetCategoryAccount;
        set { _budgetCategoryAccount = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public int BudgetYear
    {
        get => _budgetYear;
        set { _budgetYear = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public int BudgetMonth
    {
        get => _budgetMonth;
        set { _budgetMonth = Math.Clamp(value, 0, 12); OnPropertyChanged(); RefreshCommandState(); }
    }

    public decimal BudgetLimitMajor
    {
        get => _budgetLimitMajor;
        set { _budgetLimitMajor = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public string BudgetNote
    {
        get => _budgetNote;
        set { _budgetNote = value; OnPropertyChanged(); }
    }

    public long? EditingBudgetId
    {
        get => _editingBudgetId;
        set { _editingBudgetId = value; OnPropertyChanged(); RefreshCommandState(); }
    }

    public string BudgetLimitLabel => $"{BudgetLimitMajor:0.00} {(BudgetCategoryAccount?.CurrencyCode ?? "USD")}";

    public IEnumerable<StatementFormat> ImportFormats => Enum.GetValues<StatementFormat>();

    public IEnumerable<RuleMatchType> MatchTypes => Enum.GetValues(typeof(RuleMatchType)).Cast<RuleMatchType>();

    private void Load()
    {
        Accounts.Clear();
        Rules.Clear();
        Transactions.Clear();
        Summary.Clear();
        MonthlyTotals.Clear();
        CategoryAccounts.Clear();
        AssetAccounts.Clear();
        Budgets.Clear();
        BudgetVariance.Clear();

        foreach (var account in _service.Accounts.OrderBy(a => a.Code))
        {
            long balance = _service.AccountBalance(account.Id);
            Accounts.Add(new AccountRow(account.Id, account.Code, account.Name, account.Type.ToString(),
                LedgerService.FormatMinor(balance), account.CurrencyCode));
            if (account.Type == AccountType.Asset)
                AssetAccounts.Add(Accounts[^1]);
            if (account.Type == AccountType.Expense || account.Type == AccountType.Income)
                CategoryAccounts.Add(account);
        }

        if (AssetAccounts.Count > 0)
            SelectedAssetAccount = Accounts.FirstOrDefault(a => a.Code == "1000") ?? Accounts[0];

        var ruleLookups = _service.Accounts.ToDictionary(a => a.Id);
        foreach (var rule in _service.Rules.OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            var category = ruleLookups.GetValueOrDefault(rule.CategoryAccountId);
            Rules.Add(new RuleRow(rule.Id, rule.Name, rule.Priority, rule.MatchType.ToString(),
                rule.MatchValue, category?.Code ?? "?", category?.Name ?? "?"));
        }

        foreach (var tx in _service.Transactions
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(200))
        {
            var legs = tx.Postings.Select(p =>
            {
                var ac = _service.Accounts.FirstOrDefault(a => a.Id == p.AccountId);
                return $"{(p.AmountMinor >= 0 ? "+" : "-")}{LedgerService.FormatMinor(p.AmountMinor)} {ac?.Code}";
            });
            Transactions.Add(new TransactionRow(
                tx.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                tx.Description,
                tx.Reference ?? string.Empty,
                string.Join(" | ", legs),
                tx.RuleExplanation ?? "No rule applied."));
        }

        var summaries = _service.BuildSummary();
        foreach (var s in summaries)
        {
            foreach (var item in s.Items
                         .Where(i => i.DebitMinor != 0 || i.CreditMinor != 0)
                         .OrderBy(i => i.AccountCode))
            {
                Summary.Add(new SummaryRow(
                    $"{s.Year:D4}-{s.Month:D2}",
                    item.AccountCode,
                    item.AccountName,
                    item.AccountType.ToString(),
                    LedgerService.FormatMinor(item.NetMinor),
                    item.CurrencyCode));
            }
        }

        foreach (var s in summaries)
        {
            MonthlyTotals.Add(new MonthlyTotalRow(
                $"{s.Year:D4}-{s.Month:D2}", s.TransactionCountPlaceholder(),
                LedgerService.FormatMinor(s.IncomeMinor), LedgerService.FormatMinor(s.ExpenseMinor),
                LedgerService.FormatMinor(s.NetIncomeMinor)));
        }

        NetWorthMinor = _service.NetWorth();

        var latest = summaries.LastOrDefault();
        if (latest is not null)
        {
            LatestMonthLabel = $"{latest.Year:D4}-{latest.Month:D2}";
            LatestMonthIncomeMinor = latest.IncomeMinor;
            LatestMonthExpenseMinor = latest.ExpenseMinor;
        }
        else
        {
            LatestMonthLabel = "—";
            LatestMonthIncomeMinor = 0;
            LatestMonthExpenseMinor = 0;
        }

        Detail = $"Net worth: {NetWorthLabel}. Latest month {LatestMonthLabel}: " +
                 $"income {LatestMonthIncomeLabel}, expense {LatestMonthExpenseLabel}.";

        foreach (var budget in _service.Budgets
            .OrderBy(b => b.Year).ThenBy(b => b.Month).ThenBy(b => b.CategoryAccountCode))
        {
            var category = _service.Accounts.FirstOrDefault(a => a.Id == budget.CategoryAccountId);
            Budgets.Add(new BudgetRow(
                budget.Id,
                budget.IsRecurring ? "recurring" : $"{budget.Year:D4}-{budget.Month:D2}",
                budget.Name,
                budget.CategoryAccountCode ?? category?.Code ?? "?",
                category?.Name ?? "?",
                category?.Type.ToString() ?? "?",
                LedgerService.FormatMinor(budget.LimitMinor),
                budget.CurrencyCode,
                budget.Note ?? string.Empty));
        }

        var varianceRows = _service.BuildBudgetVariance();
        foreach (var row in varianceRows)
        {
            BudgetVariance.Add(new BudgetVarianceRow(
                $"{row.Year:D4}-{row.Month:D2}",
                row.AccountCode,
                row.AccountName,
                row.AccountType.ToString(),
                LedgerService.FormatMinor(row.LimitMinor),
                LedgerService.FormatMinor(row.ActualMinor),
                LedgerService.FormatMinor(row.VarianceMinor),
                row.CurrencyCode,
                row.Explanation));
        }

        if (BudgetCategoryAccount is null)
            BudgetCategoryAccount = CategoryAccounts.FirstOrDefault();

        RefreshCommandState();
    }

    private void ExecuteImport()
    {
        try
        {
            if (SelectedAssetAccount is null)
            {
                Status = "Select an asset account first.";
                return;
            }
            var bank = _service.AssetAccounts.FirstOrDefault(a => a.Code == SelectedAssetAccount.Code)
                       ?? _service.Accounts.First(a => a.Code == SelectedAssetAccount.Code);
            var result = _service.ImportStatement(ImportFilePath, bank.Id, ImportFormat);
            Status = $"Imported {result.TransactionCount} {result.FormatName} transactions. " +
                     $"Opening {LedgerService.FormatMinor(result.OpeningBalanceMinor)} " +
                     $"closing {LedgerService.FormatMinor(result.ActualClosingBalanceMinor)}. " +
                     $"Reconciles: {result.Reconciles}.";
            if (result.ReconciliationDifferenceMinor.HasValue && result.ReconciliationDifferenceMinor != 0)
                Status += $" Difference {LedgerService.FormatMinor(result.ReconciliationDifferenceMinor.Value)}.";
            if (result.Warnings.Count > 0)
                Status += " " + string.Join(" ", result.Warnings.Take(3));
            Load();
        }
        catch (Exception ex)
        {
            Status = "Import failed: " + ex.Message;
        }
    }

    private void ExecuteExportLedger()
    {
        try
        {
            string path = PromptSave("ledger-export.csv");
            if (path is null) return;
            _service.ExportLedger(path);
            Status = $"Wrote ledger export to {path}.";
        }
        catch (Exception ex)
        {
            Status = "Export failed: " + ex.Message;
        }
    }

    private void ExecuteExportSummary()
    {
        try
        {
            string path = PromptSave("summary-export.csv");
            if (path is null) return;
            _service.ExportSummary(path);
            Status = $"Wrote summary export to {path}.";
        }
        catch (Exception ex)
        {
            Status = "Export failed: " + ex.Message;
        }
    }

    private bool CanSaveRule() =>
        !string.IsNullOrWhiteSpace(EditorName) &&
        !string.IsNullOrWhiteSpace(EditorMatchValue) &&
        EditorCategoryAccount is not null;

    private void ExecuteSaveRule()
    {
        try
        {
            if (EditorCategoryAccount is null) return;
            if (EditingRuleId.HasValue)
            {
                _service.UpdateRule(EditingRuleId.Value, EditorName, EditorPriority,
                    EditorMatchType, EditorMatchValue, EditorCategoryAccount.Id);
                Status = $"Updated rule {EditorName}.";
            }
            else
            {
                var id = _service.AddRule(EditorName, EditorPriority,
                    EditorMatchType, EditorMatchValue, EditorCategoryAccount.Id);
                EditingRuleId = id;
                Status = $"Created rule {EditorName} (id {id}).";
            }
            Load();
        }
        catch (Exception ex)
        {
            Status = "Save rule failed: " + ex.Message;
        }
    }

    private void ExecuteDeleteRule()
    {
        try
        {
            if (!EditingRuleId.HasValue) return;
            _service.DeleteRule(EditingRuleId.Value);
            EditingRuleId = null;
            EditorName = string.Empty;
            EditorMatchValue = string.Empty;
            Status = "Rule deleted.";
            Load();
        }
        catch (Exception ex)
        {
            Status = "Delete rule failed: " + ex.Message;
        }
    }

    private void ExecutePreview()
    {
        var result = _service.PreviewCategorization(
            string.IsNullOrWhiteSpace(PreviewDescription) ? "" : PreviewDescription,
            string.IsNullOrWhiteSpace(PreviewReference) ? null : PreviewReference);
        PreviewResult = $"Matched: {result.Matched}. {result.Explanation}";
    }

    private bool CanSaveBudget() =>
        !string.IsNullOrWhiteSpace(BudgetName) &&
        BudgetCategoryAccount is not null &&
        BudgetLimitMajor > 0m &&
        BudgetYear >= 0 && BudgetMonth >= 0 && BudgetMonth <= 12;

    private void ExecuteSaveBudget()
    {
        try
        {
            if (BudgetCategoryAccount is null) return;
            long limitMinor = (long)Math.Round(BudgetLimitMajor * 100m, MidpointRounding.ToEven);
            if (EditingBudgetId.HasValue)
            {
                _service.UpdateBudget(EditingBudgetId.Value, BudgetName, BudgetCategoryAccount.Id,
                    BudgetYear, BudgetMonth, limitMinor, string.IsNullOrWhiteSpace(BudgetNote) ? null : BudgetNote);
                Status = $"Updated budget {BudgetName}.";
            }
            else
            {
                var id = _service.AddBudget(BudgetName, BudgetCategoryAccount.Id,
                    BudgetYear, BudgetMonth, limitMinor, string.IsNullOrWhiteSpace(BudgetNote) ? null : BudgetNote);
                EditingBudgetId = id;
                Status = $"Created budget {BudgetName} (id {id}).";
            }
            Load();
        }
        catch (Exception ex)
        {
            Status = "Save budget failed: " + ex.Message;
        }
    }

    private void ExecuteDeleteBudget()
    {
        try
        {
            if (!EditingBudgetId.HasValue) return;
            _service.DeleteBudget(EditingBudgetId.Value);
            Status = "Budget deleted.";
            ResetBudgetEditor();
            Load();
        }
        catch (Exception ex)
        {
            Status = "Delete budget failed: " + ex.Message;
        }
    }

    public void BeginEditBudget(BudgetRow row)
    {
        var budget = _service.Budgets.FirstOrDefault(b => b.Id == row.Id);
        if (budget is null) return;
        EditingBudgetId = budget.Id;
        BudgetName = budget.Name;
        BudgetYear = budget.Year;
        BudgetMonth = budget.Month;
        BudgetLimitMajor = budget.LimitMinor / 100m;
        BudgetNote = budget.Note ?? string.Empty;
        BudgetCategoryAccount = CategoryAccounts.FirstOrDefault(a => a.Id == budget.CategoryAccountId);
    }

    public void ResetBudgetEditor()
    {
        EditingBudgetId = null;
        BudgetName = string.Empty;
        BudgetYear = 2025;
        BudgetMonth = 1;
        BudgetLimitMajor = 100m;
        BudgetNote = string.Empty;
        BudgetCategoryAccount = CategoryAccounts.FirstOrDefault();
    }

    private void ExecuteExportBudgets()
    {
        try
        {
            string path = PromptSave("budgets-export.csv");
            _service.ExportBudgets(path);
            Status = $"Wrote budgets export to {path}.";
        }
        catch (Exception ex)
        {
            Status = "Budget export failed: " + ex.Message;
        }
    }

    private void ExecuteExportBudgetVariance()
    {
        try
        {
            string path = PromptSave("budget-variance-export.csv");
            _service.ExportBudgetVariance(path);
            Status = $"Wrote budget variance export to {path}.";
        }
        catch (Exception ex)
        {
            Status = "Budget variance export failed: " + ex.Message;
        }
    }

    public void BeginEditRule(RuleRow row)
    {
        var rule = _service.Rules.FirstOrDefault(r => r.Id == row.Id);
        if (rule is null) return;
        EditingRuleId = rule.Id;
        EditorName = rule.Name;
        EditorPriority = rule.Priority;
        EditorMatchType = rule.MatchType;
        EditorMatchValue = rule.MatchValue;
        EditorCategoryAccount = CategoryAccounts.FirstOrDefault(a => a.Id == rule.CategoryAccountId);
    }

    public void ResetEditor()
    {
        EditingRuleId = null;
        EditorName = string.Empty;
        EditorMatchValue = string.Empty;
        EditorPriority = 100;
        EditorMatchType = RuleMatchType.DescriptionContains;
        EditorCategoryAccount = CategoryAccounts.FirstOrDefault();
    }

    public void SetImportFilePath(string path)
    {
        ImportFilePath = path;
    }

    private void RefreshCommandState()
    {
        (ImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportLedgerCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportSummaryCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveRuleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteRuleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveBudgetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteBudgetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportBudgetsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportBudgetVarianceCommand as RelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(BudgetLimitLabel));
    }

    private static string PromptSave(string defaultName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), defaultName);

    private static ILedgerStore SqliteLedgerStoreAsService() =>
        PersonalLedgerLab.Data.SqliteLedgerStoreFactory.CreateDefault();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose() { }
}

public sealed record AccountRow(long Id, string Code, string Name, string Type, string Balance, string Currency);
public sealed record RuleRow(long Id, string Name, int Priority, string MatchType, string MatchValue, string CategoryCode, string CategoryName);
public sealed record TransactionRow(string Date, string Description, string Reference, string Postings, string Explanation);
public sealed record SummaryRow(string Month, string AccountCode, string AccountName, string Type, string Net, string Currency);
public sealed record MonthlyTotalRow(string Month, string Transactions, string Income, string Expense, string NetIncome);
public sealed record BudgetRow(long Id, string Period, string Name, string AccountCode, string AccountName, string AccountType, string Limit, string Currency, string Note);
public sealed record BudgetVarianceRow(string Month, string AccountCode, string AccountName, string AccountType, string Limit, string Actual, string Variance, string Currency, string Explanation);

internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute ?? (() => true);
    }

    public bool CanExecute(object? parameter) => _canExecute();
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal static class SummaryExtensions
{
    public static string TransactionCountPlaceholder(this MonthlySummary s) => "—";
}