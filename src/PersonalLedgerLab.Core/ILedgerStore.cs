namespace PersonalLedgerLab.Core;

/// <summary>
/// A persistence boundary for the ledger, implemented by the data layer.
/// </summary>
public interface ILedgerStore
{
    IReadOnlyList<Account> LoadAccounts();
    long AddAccount(Account account);
    bool UpdateAccount(Account account);

    IReadOnlyList<CategoryRule> LoadRules();
    long AddRule(CategoryRule rule);
    bool UpdateRule(CategoryRule rule);
    bool DeleteRule(long ruleId);

    IReadOnlyList<Transaction> LoadTransactions();
    long SaveTransaction(Transaction transaction);
    void ClearTransactions();

    IReadOnlyList<RecurringTransaction> LoadRecurringTransactions();
    long AddRecurringTransaction(RecurringTransaction recurringTransaction);
    bool UpdateRecurringTransaction(RecurringTransaction recurringTransaction);
    bool DeleteRecurringTransaction(long recurringTransactionId);

    IReadOnlyList<Budget> LoadBudgets();
    long AddBudget(Budget budget);
    bool UpdateBudget(Budget budget);
    bool DeleteBudget(long budgetId);

    string BaseCurrencyCode { get; }
    void SetBaseCurrencyCode(string currencyCode);

    IReadOnlyList<ExchangeRate> LoadExchangeRates();
    long AddExchangeRate(ExchangeRate rate);
    bool UpdateExchangeRate(ExchangeRate rate);
    bool DeleteExchangeRate(long rateId);

    string DatabasePath { get; }
    bool IsFreshDatabase { get; }
}
