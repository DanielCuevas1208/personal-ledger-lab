namespace PersonalLedgerLab.Core;

/// <summary>
/// The result of importing a statement file. Includes reconciliation outcome.
/// </summary>
public sealed class StatementImportResult
{
    public string FormatName { get; init; } = string.Empty;
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
/// applies categorization rules, and reconciles the statement balance evidence.
/// </summary>
public sealed class LedgerImportRunner
{
    private readonly RuleEngine _engine = new();
    private readonly ExchangeRateService _exchangeRates = new();

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
        IReadOnlyDictionary<long, Account> categoryAccountsById,
        IReadOnlyList<ExchangeRate> exchangeRates) =>
        Import(book, bankAccount, suspenseAccount, new ImportedStatement
        {
            FormatName = "CSV",
            Source = "csv-import",
            Rows = rows
        }, rules, categoryAccountsById, exchangeRates);

    /// <summary>
    /// Converts an adapter result into balanced ledger transactions.
    /// </summary>
    public StatementImportResult Import(
        LedgerBook book,
        Account bankAccount,
        Account suspenseAccount,
        ImportedStatement statement,
        IReadOnlyList<CategoryRule> rules,
        IReadOnlyDictionary<long, Account> categoryAccountsById,
        IReadOnlyList<ExchangeRate> exchangeRates)
    {
        if (statement is null) throw new ArgumentNullException(nameof(statement));
        if (statement.Rows is null) throw new ArgumentException("The statement rows are required.", nameof(statement));
        if (book is null) throw new ArgumentNullException(nameof(book));
        if (bankAccount is null) throw new ArgumentNullException(nameof(bankAccount));
        if (suspenseAccount is null) throw new ArgumentNullException(nameof(suspenseAccount));
        if (exchangeRates is null) throw new ArgumentNullException(nameof(exchangeRates));

        var rows = statement.Rows;
        var warnings = new List<string>();
        if (statement.OpeningBalanceIsInferred)
            warnings.Add("The opening balance was inferred from the closing balance and transaction total.");

        int txCount = 0;
        var firstBalance = rows.Count > 0 ? rows[0].RunningBalanceMinor : null;
        long opening = statement.OpeningBalanceMinor
            ?? (firstBalance.HasValue ? firstBalance.Value - rows[0].AmountMinor : 0);
        long actualClosing = opening;
        long? expectedClosing = statement.ClosingBalanceMinor
            ?? rows.LastOrDefault()?.RunningBalanceMinor;
        bool reconcileOK = true;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            actualClosing = checked(actualClosing + row.AmountMinor);
            if (row.RunningBalanceMinor.HasValue && row.RunningBalanceMinor.Value != actualClosing)
            {
                warnings.Add($"Row {i + 1}: stated balance {row.RunningBalanceMinor.Value} differs from replayed balance {actualClosing}.");
                reconcileOK = false;
            }

            var ruleResult = _engine.Categorize(DraftTransaction(row, statement.Source), rules);

            long offsetAccountId = suspenseAccount.Id;
            if (ruleResult.Matched && ruleResult.CategoryAccountId.HasValue &&
                categoryAccountsById.ContainsKey(ruleResult.CategoryAccountId.Value))
            {
                offsetAccountId = ruleResult.CategoryAccountId.Value;
            }

            var account = book.GetAccount(offsetAccountId);
            if (!string.Equals(account.CurrencyCode, bankAccount.CurrencyCode, StringComparison.Ordinal))
                warnings.Add($"Row {i + 1}: offset account {account.Code} uses a different currency than the bank account.");

            long bankAmountMinor = row.AmountMinor;
            long offsetAmountMinor = -row.AmountMinor;
            string postingCurrency = bankAccount.CurrencyCode;
            string? conversionNote = null;
            ExchangeRateLookupResult? conversion = null;

            if (!string.Equals(row.CurrencyCode, bankAccount.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                conversion = _exchangeRates.FindRate(exchangeRates, row.Date, row.CurrencyCode, bankAccount.CurrencyCode);
                bankAmountMinor = _exchangeRates.ConvertMinor(row.AmountMinor, conversion.Rate);
                offsetAmountMinor = -bankAmountMinor;
                conversionNote =
                    $"Converted {AccountMath.FormatSigned(row.AmountMinor, row.CurrencyCode)} to " +
                    $"{AccountMath.FormatSigned(bankAmountMinor, bankAccount.CurrencyCode)}. {conversion.Explanation}";
            }

            var tx = book.AddTransaction(
                row.Date,
                row.Description,
                row.Reference,
                string.IsNullOrWhiteSpace(statement.Source) ? "statement-import" : statement.Source,
                new[]
                {
                    (bankAccount.Id, bankAmountMinor, postingCurrency),
                    (offsetAccountId, offsetAmountMinor, postingCurrency)
                });

            tx.AppliedRuleId = ruleResult.AppliedRuleId;
            tx.RuleExplanation = conversionNote is null
                ? ruleResult.Explanation
                : $"{ruleResult.Explanation} {conversionNote}";
            if (conversion is not null)
            {
                tx.SourceCurrencyCode = row.CurrencyCode;
                tx.SourceAmountMinor = row.AmountMinor;
                tx.ExchangeRatePpm = conversion.Rate.RatePpm;
            }
            txCount++;
        }

        bool reconciles = reconcileOK
            && (!expectedClosing.HasValue || expectedClosing.Value == actualClosing);

        return new StatementImportResult
        {
            FormatName = statement.FormatName,
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

    private static Transaction DraftTransaction(ImportedRow row, string source) =>
        new(0, row.Date, row.Description, row.Reference,
            string.IsNullOrWhiteSpace(source) ? "statement-import" : source);
}
