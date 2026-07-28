namespace PersonalLedgerLab.Core;

/// <summary>
/// The result of importing a statement file. Includes reconciliation outcome.
/// </summary>
public sealed class StatementImportResult
{
    public IReadOnlyList<ImportedRow> Rows { get; init; } = Array.Empty<ImportedRow>();
    public int TransactionCount { get; init; }
    public long OpeningBalanceMinor { get; init; }
    public long? ExpectedClosingBalanceMinor { get; init; }
    public long ActualClosingBalanceMinor { get; init; }
    public long? ReconciliationDifferenceMinor { get; init; }
    public bool Reconciles { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Builds balanced double-entry transactions from imported statement rows,
/// applies categorization rules, and reconciles the statement running balance.
/// </summary>
public sealed class LedgerImportRunner
{
    private readonly RuleEngine _engine = new();

    /// <summary>
    /// Converts rows into balanced transactions attached to <paramref name="book"/>.
    /// The offset leg moves to a matched category account or to the suspense account.
    /// </summary>
    public StatementImportResult Import(
        LedgerBook book,
        Account bankAccount,
        Account suspenseAccount,
        IReadOnlyList<ImportedRow> rows,
        IReadOnlyList<CategoryRule> rules,
        IReadOnlyDictionary<long, Account> categoryAccountsById)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        if (book is null) throw new ArgumentNullException(nameof(book));
        if (bankAccount is null) throw new ArgumentNullException(nameof(bankAccount));
        if (suspenseAccount is null) throw new ArgumentNullException(nameof(suspenseAccount));

        var warnings = new List<string>();
        int txCount = 0;

        long opening = 0;
        if (rows.Count > 0 && rows[0].RunningBalanceMinor.HasValue)
            opening = rows[0].RunningBalanceMinor!.Value - rows[0].AmountMinor;

        long actualClosing = opening;
        long? expectedClosing = null;
        bool reconcileOK = true;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            actualClosing += row.AmountMinor;
            if (row.RunningBalanceMinor.HasValue)
            {
                long stated = row.RunningBalanceMinor!.Value;
                expectedClosing = stated;
                if (stated != actualClosing)
                {
                    warnings.Add($"Row {i + 1}: stated balance {stated} differs from replayed balance {actualClosing}.");
                    reconcileOK = false;
                    actualClosing = stated;
                }
            }

            var ruleResult = _engine.Categorize(
                DraftTransaction(row), rules);

            long offsetAccountId = suspenseAccount.Id;
            if (ruleResult.Matched && ruleResult.CategoryAccountId.HasValue &&
                categoryAccountsById.ContainsKey(ruleResult.CategoryAccountId.Value))
            {
                offsetAccountId = ruleResult.CategoryAccountId.Value;
            }

            var account = book.GetAccount(offsetAccountId);
            if (!string.Equals(account.CurrencyCode, bankAccount.CurrencyCode, StringComparison.Ordinal))
                warnings.Add($"Row {i + 1}: offset account {account.Code} uses a different currency.");

            var tx = book.AddTransaction(
                row.Date,
                row.Description,
                row.Reference,
                "csv-import",
                new[]
                {
                    (bankAccount.Id, row.AmountMinor, row.CurrencyCode),
                    (offsetAccountId, -row.AmountMinor, row.CurrencyCode)
                });

            tx.AppliedRuleId = ruleResult.AppliedRuleId;
            tx.RuleExplanation = ruleResult.Explanation;
            txCount++;
        }

        bool reconciles = reconcileOK
            && (!expectedClosing.HasValue || expectedClosing.Value == actualClosing);

        return new StatementImportResult
        {
            Rows = rows,
            TransactionCount = txCount,
            OpeningBalanceMinor = opening,
            ExpectedClosingBalanceMinor = expectedClosing,
            ActualClosingBalanceMinor = actualClosing,
            ReconciliationDifferenceMinor = expectedClosing.HasValue ? expectedClosing - actualClosing : null,
            Reconciles = reconciles,
            Warnings = warnings
        };
    }

    private static Transaction DraftTransaction(ImportedRow row) =>
        new(0, row.Date, row.Description, row.Reference, "csv-import");
}