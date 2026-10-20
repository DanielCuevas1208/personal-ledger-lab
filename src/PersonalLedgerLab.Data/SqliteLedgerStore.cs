using System.Globalization;
using Microsoft.Data.Sqlite;
using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Data;

/// <summary>
/// SQLite-backed implementation of <see cref="ILedgerStore"/>.
/// Creates the schema and seeds a chart of accounts plus sample rules
/// the first time a database file is opened.
/// </summary>
public sealed class SqliteLedgerStore : ILedgerStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly bool _ownsConnection;

    public string DatabasePath { get; }
    public bool IsFreshDatabase { get; }
    public string BaseCurrencyCode { get; private set; } = "USD";

    public SqliteLedgerStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A database path is required.", nameof(databasePath));

        DatabasePath = databasePath;
        string? dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        IsFreshDatabase = !File.Exists(databasePath);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connection = new SqliteConnection(builder.ToString());
        _ownsConnection = true;
        _connection.Open();

        Execute("PRAGMA foreign_keys = ON;");
        EnsureSchema();
        MigrateSchema();
        LoadSettings();
        if (IsFreshDatabase)
            Seed();
    }

    public SqliteLedgerStore(SqliteConnection existingConnection)
    {
        _connection = existingConnection ?? throw new ArgumentNullException(nameof(existingConnection));
        _ownsConnection = false;
        DatabasePath = _connection.DataSource ?? string.Empty;
        EnsureSchema();
        MigrateSchema();
        LoadSettings();
    }

    public IReadOnlyList<Account> LoadAccounts()
    {
        var accounts = new List<Account>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, code, name, type, currency_code, note FROM accounts ORDER BY code;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            accounts.Add(new Account(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                (AccountType)reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return accounts;
    }

    public long AddAccount(Account account)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO accounts (code, name, type, currency_code, note)
            VALUES ($code, $name, $type, $currency, $note);
            SELECT last_insert_rowid();";
        AddParam(cmd, "$code", account.Code);
        AddParam(cmd, "$name", account.Name);
        AddParam(cmd, "$type", (int)account.Type);
        AddParam(cmd, "$currency", account.CurrencyCode);
        AddParam(cmd, "$note", (object?)account.Note ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public bool UpdateAccount(Account account)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE accounts
            SET code = $code, name = $name, type = $type,
                currency_code = $currency, note = $note
            WHERE id = $id;";
        AddParam(cmd, "$id", account.Id);
        AddParam(cmd, "$code", account.Code);
        AddParam(cmd, "$name", account.Name);
        AddParam(cmd, "$type", (int)account.Type);
        AddParam(cmd, "$currency", account.CurrencyCode);
        AddParam(cmd, "$note", (object?)account.Note ?? DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<CategoryRule> LoadRules()
    {
        var rules = new List<CategoryRule>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, priority, match_type, match_value,
                   category_account_id, category_account_code
            FROM rules ORDER BY priority, id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rules.Add(new CategoryRule
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Priority = reader.GetInt32(2),
                MatchType = (RuleMatchType)reader.GetInt32(3),
                MatchValue = reader.GetString(4),
                CategoryAccountId = reader.GetInt64(5),
                CategoryAccountCode = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return rules;
    }

    public long AddRule(CategoryRule rule)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO rules (name, priority, match_type, match_value,
                               category_account_id, category_account_code)
            VALUES ($name, $priority, $matchType, $matchValue,
                    $categoryId, $categoryCode);
            SELECT last_insert_rowid();";
        AddParam(cmd, "$name", rule.Name);
        AddParam(cmd, "$priority", rule.Priority);
        AddParam(cmd, "$matchType", (int)rule.MatchType);
        AddParam(cmd, "$matchValue", rule.MatchValue);
        AddParam(cmd, "$categoryId", rule.CategoryAccountId);
        AddParam(cmd, "$categoryCode", (object?)rule.CategoryAccountCode ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public bool UpdateRule(CategoryRule rule)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE rules
            SET name = $name, priority = $priority, match_type = $matchType,
                match_value = $matchValue, category_account_id = $categoryId,
                category_account_code = $categoryCode
            WHERE id = $id;";
        AddParam(cmd, "$id", rule.Id);
        AddParam(cmd, "$name", rule.Name);
        AddParam(cmd, "$priority", rule.Priority);
        AddParam(cmd, "$matchType", (int)rule.MatchType);
        AddParam(cmd, "$matchValue", rule.MatchValue);
        AddParam(cmd, "$categoryId", rule.CategoryAccountId);
        AddParam(cmd, "$categoryCode", (object?)rule.CategoryAccountCode ?? DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteRule(long ruleId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM rules WHERE id = $id;";
        AddParam(cmd, "$id", ruleId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<Transaction> LoadTransactions()
    {
        var byId = new Dictionary<long, Transaction>();
        using (var txCmd = _connection.CreateCommand())
        {
            txCmd.CommandText = @"
                SELECT id, date, description, reference, source,
                       applied_rule_id, rule_explanation,
                       source_currency_code, source_amount_minor, exchange_rate_ppm,
                       recurring_template_id, recurring_occurrence_date
                FROM transactions ORDER BY date, id;";
            using var reader = txCmd.ExecuteReader();
            while (reader.Read())
            {
                var tx = new Transaction
                {
                    Id = reader.GetInt64(0),
                    Date = DateOnly.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    Description = reader.GetString(2),
                    Reference = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Source = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    AppliedRuleId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    RuleExplanation = reader.IsDBNull(6) ? null : reader.GetString(6),
                    SourceCurrencyCode = reader.IsDBNull(7) ? null : reader.GetString(7),
                    SourceAmountMinor = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    ExchangeRatePpm = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    RecurringTemplateId = reader.IsDBNull(10) ? null : reader.GetInt64(10),
                    RecurringOccurrenceDate = reader.IsDBNull(11)
                        ? null
                        : DateOnly.Parse(reader.GetString(11), CultureInfo.InvariantCulture)
                };
                byId[tx.Id] = tx;
            }
        }

        using (var postCmd = _connection.CreateCommand())
        {
            postCmd.CommandText = @"
                SELECT id, transaction_id, account_id, amount_minor, currency_code
                FROM postings ORDER BY id;";
            using var reader = postCmd.ExecuteReader();
            while (reader.Read())
            {
                long txId = reader.GetInt64(1);
                if (!byId.TryGetValue(txId, out var tx))
                    continue;
                tx.Postings.Add(new Posting
                {
                    Id = reader.GetInt64(0),
                    TransactionId = txId,
                    AccountId = reader.GetInt64(2),
                    AmountMinor = reader.GetInt64(3),
                    CurrencyCode = reader.GetString(4)
                });
            }
        }

        return byId.Values.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
    }

    public long SaveTransaction(Transaction transaction)
    {
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));
        if (!transaction.IsBalanced)
            throw new LedgerBalanceException(
                $"Cannot save unbalanced transaction '{transaction.Description}'.");

        using var tx = _connection.BeginTransaction();
        try
        {
            long newId;
            using (var txCmd = _connection.CreateCommand())
            {
                txCmd.Transaction = tx;
                txCmd.CommandText = @"
                    INSERT INTO transactions
                        (date, description, reference, source, applied_rule_id, rule_explanation,
                         source_currency_code, source_amount_minor, exchange_rate_ppm,
                         recurring_template_id, recurring_occurrence_date)
                    VALUES ($date, $desc, $ref, $source, $ruleId, $ruleExplain,
                            $sourceCurrency, $sourceAmount, $ratePpm,
                            $recurringTemplateId, $recurringOccurrenceDate);
                    SELECT last_insert_rowid();";
                AddParam(txCmd, "$date", transaction.Date.ToString("yyyy-MM-dd"));
                AddParam(txCmd, "$desc", transaction.Description);
                AddParam(txCmd, "$ref", (object?)transaction.Reference ?? DBNull.Value);
                AddParam(txCmd, "$source", (object?)transaction.Source ?? DBNull.Value);
                AddParam(txCmd, "$ruleId", (object?)transaction.AppliedRuleId ?? DBNull.Value);
                AddParam(txCmd, "$ruleExplain", (object?)transaction.RuleExplanation ?? DBNull.Value);
                AddParam(txCmd, "$sourceCurrency", (object?)transaction.SourceCurrencyCode ?? DBNull.Value);
                AddParam(txCmd, "$sourceAmount", (object?)transaction.SourceAmountMinor ?? DBNull.Value);
                AddParam(txCmd, "$ratePpm", (object?)transaction.ExchangeRatePpm ?? DBNull.Value);
                AddParam(txCmd, "$recurringTemplateId", (object?)transaction.RecurringTemplateId ?? DBNull.Value);
                AddParam(txCmd, "$recurringOccurrenceDate",
                    transaction.RecurringOccurrenceDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
                newId = (long)txCmd.ExecuteScalar()!;
            }

            foreach (var posting in transaction.Postings)
            {
                using var pCmd = _connection.CreateCommand();
                pCmd.Transaction = tx;
                pCmd.CommandText = @"
                    INSERT INTO postings (transaction_id, account_id, amount_minor, currency_code)
                    VALUES ($txId, $acctId, $amount, $currency);";
                AddParam(pCmd, "$txId", newId);
                AddParam(pCmd, "$acctId", posting.AccountId);
                AddParam(pCmd, "$amount", posting.AmountMinor);
                AddParam(pCmd, "$currency", posting.CurrencyCode);
                pCmd.ExecuteNonQuery();
            }

            tx.Commit();
            transaction.Id = newId;
            foreach (var posting in transaction.Postings)
                posting.TransactionId = newId;
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void ClearTransactions()
    {
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var p = _connection.CreateCommand()) { p.Transaction = tx; p.CommandText = "DELETE FROM postings;"; p.ExecuteNonQuery(); }
            using (var t = _connection.CreateCommand()) { t.Transaction = tx; t.CommandText = "DELETE FROM transactions;"; t.ExecuteNonQuery(); }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public IReadOnlyList<RecurringTransaction> LoadRecurringTransactions()
    {
        var recurring = new List<RecurringTransaction>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, reference, day_of_month,
                   debit_account_id, credit_account_id, amount_minor,
                   currency_code, note
            FROM recurring_transactions
            ORDER BY name, id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var template = new RecurringTransaction
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                Reference = reader.IsDBNull(3) ? null : reader.GetString(3),
                DayOfMonth = reader.GetInt32(4),
                DebitAccountId = reader.GetInt64(5),
                CreditAccountId = reader.GetInt64(6),
                AmountMinor = reader.GetInt64(7),
                CurrencyCode = reader.GetString(8),
                Note = reader.IsDBNull(9) ? null : reader.GetString(9)
            };
            template.Validate();
            recurring.Add(template);
        }
        return recurring;
    }

    public long AddRecurringTransaction(RecurringTransaction recurringTransaction)
    {
        if (recurringTransaction is null) throw new ArgumentNullException(nameof(recurringTransaction));
        recurringTransaction.Validate();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO recurring_transactions
                (name, description, reference, day_of_month, debit_account_id,
                 credit_account_id, amount_minor, currency_code, note)
            VALUES ($name, $description, $reference, $day, $debit, $credit,
                    $amount, $currency, $note);
            SELECT last_insert_rowid();";
        AddParam(cmd, "$name", recurringTransaction.Name);
        AddParam(cmd, "$description", recurringTransaction.Description);
        AddParam(cmd, "$reference", (object?)recurringTransaction.Reference ?? DBNull.Value);
        AddParam(cmd, "$day", recurringTransaction.DayOfMonth);
        AddParam(cmd, "$debit", recurringTransaction.DebitAccountId);
        AddParam(cmd, "$credit", recurringTransaction.CreditAccountId);
        AddParam(cmd, "$amount", recurringTransaction.AmountMinor);
        AddParam(cmd, "$currency", recurringTransaction.CurrencyCode);
        AddParam(cmd, "$note", (object?)recurringTransaction.Note ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public bool UpdateRecurringTransaction(RecurringTransaction recurringTransaction)
    {
        if (recurringTransaction is null) throw new ArgumentNullException(nameof(recurringTransaction));
        recurringTransaction.Validate();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE recurring_transactions
            SET name = $name, description = $description, reference = $reference,
                day_of_month = $day, debit_account_id = $debit,
                credit_account_id = $credit, amount_minor = $amount,
                currency_code = $currency, note = $note
            WHERE id = $id;";
        AddParam(cmd, "$id", recurringTransaction.Id);
        AddParam(cmd, "$name", recurringTransaction.Name);
        AddParam(cmd, "$description", recurringTransaction.Description);
        AddParam(cmd, "$reference", (object?)recurringTransaction.Reference ?? DBNull.Value);
        AddParam(cmd, "$day", recurringTransaction.DayOfMonth);
        AddParam(cmd, "$debit", recurringTransaction.DebitAccountId);
        AddParam(cmd, "$credit", recurringTransaction.CreditAccountId);
        AddParam(cmd, "$amount", recurringTransaction.AmountMinor);
        AddParam(cmd, "$currency", recurringTransaction.CurrencyCode);
        AddParam(cmd, "$note", (object?)recurringTransaction.Note ?? DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteRecurringTransaction(long recurringTransactionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM recurring_transactions WHERE id = $id;";
        AddParam(cmd, "$id", recurringTransactionId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<Budget> LoadBudgets()
    {
        var budgets = new List<Budget>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, category_account_id, category_account_code, name,
                   year, month, limit_minor, currency_code, note
            FROM budgets
            ORDER BY year, month, category_account_code;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            budgets.Add(new Budget
            {
                Id = reader.GetInt64(0),
                CategoryAccountId = reader.GetInt64(1),
                CategoryAccountCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                Name = reader.GetString(3),
                Year = reader.GetInt32(4),
                Month = reader.GetInt32(5),
                LimitMinor = reader.GetInt64(6),
                CurrencyCode = reader.GetString(7),
                Note = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return budgets;
    }

    public long AddBudget(Budget budget)
    {
        if (budget is null) throw new ArgumentNullException(nameof(budget));
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO budgets
                (category_account_id, category_account_code, name, year, month,
                 limit_minor, currency_code, note)
            VALUES ($acctId, $acctCode, $name, $year, $month, $limit, $currency, $note);
            SELECT last_insert_rowid();";
        AddParam(cmd, "$acctId", budget.CategoryAccountId);
        AddParam(cmd, "$acctCode", (object?)budget.CategoryAccountCode ?? DBNull.Value);
        AddParam(cmd, "$name", budget.Name);
        AddParam(cmd, "$year", budget.Year);
        AddParam(cmd, "$month", budget.Month);
        AddParam(cmd, "$limit", budget.LimitMinor);
        AddParam(cmd, "$currency", budget.CurrencyCode);
        AddParam(cmd, "$note", (object?)budget.Note ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public bool UpdateBudget(Budget budget)
    {
        if (budget is null) throw new ArgumentNullException(nameof(budget));
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE budgets
            SET category_account_id = $acctId, category_account_code = $acctCode,
                name = $name, year = $year, month = $month,
                limit_minor = $limit, currency_code = $currency, note = $note
            WHERE id = $id;";
        AddParam(cmd, "$id", budget.Id);
        AddParam(cmd, "$acctId", budget.CategoryAccountId);
        AddParam(cmd, "$acctCode", (object?)budget.CategoryAccountCode ?? DBNull.Value);
        AddParam(cmd, "$name", budget.Name);
        AddParam(cmd, "$year", budget.Year);
        AddParam(cmd, "$month", budget.Month);
        AddParam(cmd, "$limit", budget.LimitMinor);
        AddParam(cmd, "$currency", budget.CurrencyCode);
        AddParam(cmd, "$note", (object?)budget.Note ?? DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteBudget(long budgetId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM budgets WHERE id = $id;";
        AddParam(cmd, "$id", budgetId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void SetBaseCurrencyCode(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("A base currency code is required.", nameof(currencyCode));
        string normalized = currencyCode.ToUpperInvariant();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ledger_settings (key, value)
            VALUES ('base_currency_code', $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        AddParam(cmd, "$value", normalized);
        cmd.ExecuteNonQuery();
        BaseCurrencyCode = normalized;
    }

    public IReadOnlyList<ExchangeRate> LoadExchangeRates()
    {
        var rates = new List<ExchangeRate>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, effective_date, from_currency, to_currency, rate_ppm
            FROM exchange_rates
            ORDER BY effective_date, from_currency, to_currency, id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rates.Add(new ExchangeRate(
                reader.GetInt64(0),
                DateOnly.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4)));
        }
        return rates;
    }

    public long AddExchangeRate(ExchangeRate rate)
    {
        if (rate is null) throw new ArgumentNullException(nameof(rate));
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO exchange_rates (effective_date, from_currency, to_currency, rate_ppm)
            VALUES ($date, $from, $to, $ppm);
            SELECT last_insert_rowid();";
        AddParam(cmd, "$date", rate.EffectiveDate.ToString("yyyy-MM-dd"));
        AddParam(cmd, "$from", rate.FromCurrency);
        AddParam(cmd, "$to", rate.ToCurrency);
        AddParam(cmd, "$ppm", rate.RatePpm);
        return (long)cmd.ExecuteScalar()!;
    }

    public bool UpdateExchangeRate(ExchangeRate rate)
    {
        if (rate is null) throw new ArgumentNullException(nameof(rate));
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE exchange_rates
            SET effective_date = $date, from_currency = $from,
                to_currency = $to, rate_ppm = $ppm
            WHERE id = $id;";
        AddParam(cmd, "$id", rate.Id);
        AddParam(cmd, "$date", rate.EffectiveDate.ToString("yyyy-MM-dd"));
        AddParam(cmd, "$from", rate.FromCurrency);
        AddParam(cmd, "$to", rate.ToCurrency);
        AddParam(cmd, "$ppm", rate.RatePpm);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteExchangeRate(long rateId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM exchange_rates WHERE id = $id;";
        AddParam(cmd, "$id", rateId);
        return cmd.ExecuteNonQuery() > 0;
    }

    private void LoadSettings()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM ledger_settings WHERE key = 'base_currency_code';";
        var value = cmd.ExecuteScalar() as string;
        if (!string.IsNullOrWhiteSpace(value))
            BaseCurrencyCode = value.ToUpperInvariant();
    }

    private void EnsureSchema()
    {
        Execute(@"
            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY,
                code TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                type INTEGER NOT NULL,
                currency_code TEXT NOT NULL,
                note TEXT
            );

            CREATE TABLE IF NOT EXISTS rules (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                priority INTEGER NOT NULL,
                match_type INTEGER NOT NULL,
                match_value TEXT NOT NULL,
                category_account_id INTEGER NOT NULL,
                category_account_code TEXT
            );

            CREATE TABLE IF NOT EXISTS transactions (
                id INTEGER PRIMARY KEY,
                date TEXT NOT NULL,
                description TEXT NOT NULL,
                reference TEXT,
                source TEXT,
                applied_rule_id INTEGER,
                rule_explanation TEXT,
                source_currency_code TEXT,
                source_amount_minor INTEGER,
                exchange_rate_ppm INTEGER,
                recurring_template_id INTEGER,
                recurring_occurrence_date TEXT
            );

            CREATE TABLE IF NOT EXISTS postings (
                id INTEGER PRIMARY KEY,
                transaction_id INTEGER NOT NULL,
                account_id INTEGER NOT NULL,
                amount_minor INTEGER NOT NULL,
                currency_code TEXT NOT NULL,
                FOREIGN KEY (transaction_id) REFERENCES transactions(id) ON DELETE CASCADE,
                FOREIGN KEY (account_id) REFERENCES accounts(id)
            );

            CREATE TABLE IF NOT EXISTS budgets (
                id INTEGER PRIMARY KEY,
                category_account_id INTEGER NOT NULL,
                category_account_code TEXT,
                name TEXT NOT NULL,
                year INTEGER NOT NULL,
                month INTEGER NOT NULL,
                limit_minor INTEGER NOT NULL,
                currency_code TEXT NOT NULL,
                note TEXT,
                FOREIGN KEY (category_account_id) REFERENCES accounts(id)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS idx_budgets_scope
                ON budgets(category_account_id, year, month);

            CREATE TABLE IF NOT EXISTS recurring_transactions (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                reference TEXT,
                day_of_month INTEGER NOT NULL,
                debit_account_id INTEGER NOT NULL,
                credit_account_id INTEGER NOT NULL,
                amount_minor INTEGER NOT NULL,
                currency_code TEXT NOT NULL,
                note TEXT,
                FOREIGN KEY (debit_account_id) REFERENCES accounts(id),
                FOREIGN KEY (credit_account_id) REFERENCES accounts(id)
            );

            CREATE TABLE IF NOT EXISTS exchange_rates (
                id INTEGER PRIMARY KEY,
                effective_date TEXT NOT NULL,
                from_currency TEXT NOT NULL,
                to_currency TEXT NOT NULL,
                rate_ppm INTEGER NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS idx_exchange_rates_scope
                ON exchange_rates(effective_date, from_currency, to_currency);

            CREATE TABLE IF NOT EXISTS ledger_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_postings_tx ON postings(transaction_id);
            CREATE INDEX IF NOT EXISTS idx_postings_acct ON postings(account_id);
            CREATE INDEX IF NOT EXISTS idx_transactions_date ON transactions(date);
        ");
    }

    private void MigrateSchema()
    {
        TryAddColumn("transactions", "source_currency_code", "TEXT");
        TryAddColumn("transactions", "source_amount_minor", "INTEGER");
        TryAddColumn("transactions", "exchange_rate_ppm", "INTEGER");
        TryAddColumn("transactions", "recurring_template_id", "INTEGER");
        TryAddColumn("transactions", "recurring_occurrence_date", "TEXT");
        EnsureExchangeRatesTable();
        EnsureLedgerSettingsTable();
        EnsureRecurringTransactionsTable();
        Execute(@"
            CREATE UNIQUE INDEX IF NOT EXISTS idx_transactions_recurring_occurrence
                ON transactions(recurring_template_id, recurring_occurrence_date)
                WHERE recurring_template_id IS NOT NULL
                  AND recurring_occurrence_date IS NOT NULL;");
        if (!IsFreshDatabase)
        {
            EnsureEurSavingsAccount();
            EnsureSeedExchangeRates();
        }
    }

    private void TryAddColumn(string table, string column, string type)
    {
        if (ColumnExists(table, column))
            return;
        Execute($"ALTER TABLE {table} ADD COLUMN {column} {type};");
    }

    private bool ColumnExists(string table, string column)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void EnsureExchangeRatesTable()
    {
        Execute(@"
            CREATE TABLE IF NOT EXISTS exchange_rates (
                id INTEGER PRIMARY KEY,
                effective_date TEXT NOT NULL,
                from_currency TEXT NOT NULL,
                to_currency TEXT NOT NULL,
                rate_ppm INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_exchange_rates_scope
                ON exchange_rates(effective_date, from_currency, to_currency);");
    }

    private void EnsureLedgerSettingsTable()
    {
        Execute(@"
            CREATE TABLE IF NOT EXISTS ledger_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ledger_settings (key, value)
            SELECT 'base_currency_code', 'USD'
            WHERE NOT EXISTS (
                SELECT 1 FROM ledger_settings WHERE key = 'base_currency_code');";
        cmd.ExecuteNonQuery();
    }

    private void EnsureRecurringTransactionsTable()
    {
        Execute(@"
            CREATE TABLE IF NOT EXISTS recurring_transactions (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                reference TEXT,
                day_of_month INTEGER NOT NULL,
                debit_account_id INTEGER NOT NULL,
                credit_account_id INTEGER NOT NULL,
                amount_minor INTEGER NOT NULL,
                currency_code TEXT NOT NULL,
                note TEXT,
                FOREIGN KEY (debit_account_id) REFERENCES accounts(id),
                FOREIGN KEY (credit_account_id) REFERENCES accounts(id)
            );
            CREATE INDEX IF NOT EXISTS idx_recurring_transactions_day
                ON recurring_transactions(day_of_month, id);");
    }

    private void EnsureEurSavingsAccount()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM accounts WHERE code = '1050';";
        if ((long)cmd.ExecuteScalar()! > 0)
            return;

        using var insert = _connection.CreateCommand();
        insert.CommandText = @"
            INSERT INTO accounts (code, name, type, currency_code, note)
            VALUES ('1050', 'EUR Savings', $type, 'EUR', $note);";
        AddParam(insert, "$type", (int)AccountType.Asset);
        AddParam(insert, "$note", "Seeded EUR asset account for multi-currency demos.");
        insert.ExecuteNonQuery();
    }

    private void EnsureSeedExchangeRates()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM exchange_rates;";
        if ((long)cmd.ExecuteScalar()! > 0)
            return;

        var seedRates = new (DateOnly Date, string From, string To, long Ppm)[]
        {
            (new DateOnly(2025, 1, 1), "EUR", "USD", ExchangeRateMath.FromDecimal(1.085000m)),
            (new DateOnly(2025, 1, 15), "EUR", "USD", ExchangeRateMath.FromDecimal(1.090000m)),
            (new DateOnly(2025, 2, 1), "EUR", "USD", ExchangeRateMath.FromDecimal(1.078000m))
        };

        foreach (var (date, from, to, ppm) in seedRates)
        {
            AddExchangeRate(new ExchangeRate(0, date, from, to, ppm));
        }
    }

    private void Seed()
    {
        var seedAccounts = new (string Code, string Name, AccountType Type, string Currency)[]
        {
            ("1000", "Checking", AccountType.Asset, "USD"),
            ("1050", "EUR Savings", AccountType.Asset, "EUR"),
            ("2000", "Credit Card", AccountType.Liability, "USD"),
            ("3000", "Opening Balances", AccountType.Equity, "USD"),
            ("3999", "Uncategorized (Suspense)", AccountType.Equity, "USD"),
            ("4000", "Salary", AccountType.Income, "USD"),
            ("4100", "Other Income", AccountType.Income, "USD"),
            ("5000", "Groceries", AccountType.Expense, "USD"),
            ("5100", "Dining", AccountType.Expense, "USD"),
            ("5200", "Coffee", AccountType.Expense, "USD"),
            ("5300", "Transport", AccountType.Expense, "USD"),
            ("5400", "Utilities", AccountType.Expense, "USD"),
            ("5500", "Subscriptions", AccountType.Expense, "USD"),
            ("5600", "Clothing", AccountType.Expense, "USD")
        };

        foreach (var (code, name, type, currency) in seedAccounts)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO accounts (code, name, type, currency_code, note)
                VALUES ($code, $name, $type, $currency, $note);";
            AddParam(cmd, "$code", code);
            AddParam(cmd, "$name", name);
            AddParam(cmd, "$type", (int)type);
            AddParam(cmd, "$currency", currency);
            AddParam(cmd, "$note", $"Seeded chart of accounts entry {code}.");
            cmd.ExecuteNonQuery();
        }

        long AccountId(string code)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id FROM accounts WHERE code = $code;";
            AddParam(cmd, "$code", code);
            return (long)cmd.ExecuteScalar()!;
        }

        var seedRules = new (string Name, int Priority, RuleMatchType Match, string Value, string AccountCode)[]
        {
            ("Salary", 5, RuleMatchType.ReferenceEquals, "PAYROLL", "4000"),
            ("Groceries", 10, RuleMatchType.DescriptionContains, "supervalu", "5000"),
            ("Coffee", 20, RuleMatchType.DescriptionContains, "coffee", "5200"),
            ("Dining", 30, RuleMatchType.DescriptionContains, "restaurant", "5100"),
            ("Transport", 40, RuleMatchType.DescriptionRegex, "(uber|transit|metro)", "5300"),
            ("Utilities", 50, RuleMatchType.DescriptionContains, "electric", "5400"),
            ("Subscriptions", 60, RuleMatchType.DescriptionContains, "streamtv", "5500"),
            ("Clothing", 70, RuleMatchType.DescriptionContains, "apparel", "5600")
        };

        foreach (var (name, priority, match, value, code) in seedRules)
        {
            var rule = new CategoryRule
            {
                Name = name,
                Priority = priority,
                MatchType = match,
                MatchValue = value,
                CategoryAccountId = AccountId(code),
                CategoryAccountCode = code
            };
            AddRule(rule);
        }

        var seedBudgets = new (string Name, int Year, int Month, string AccountCode, long LimitMinor)[]
        {
            ("January salary target", 2025, 1, "4000", 1500_00L),
            ("January groceries cap", 2025, 1, "5000", 250_00L),
            ("January dining cap", 2025, 1, "5100", 80_00L),
            ("January coffee cap", 2025, 1, "5200", 25_00L),
            ("January transport cap", 2025, 1, "5300", 90_00L),
            ("January utilities cap", 2025, 1, "5400", 250_00L),
            ("January subscriptions cap", 2025, 1, "5500", 15_00L),
            ("January clothing cap", 2025, 1, "5600", 60_00L)
        };

        foreach (var (name, year, month, code, limit) in seedBudgets)
        {
            var budget = new Budget
            {
                Name = name,
                Year = year,
                Month = month,
                CategoryAccountId = AccountId(code),
                CategoryAccountCode = code,
                LimitMinor = limit,
                CurrencyCode = "USD",
                Note = $"Seeded budget for {code} ({name})."
            };
            AddBudget(budget);
        }

        SetBaseCurrencyCode("USD");

        var seedRates = new (DateOnly Date, string From, string To, long Ppm)[]
        {
            (new DateOnly(2025, 1, 1), "EUR", "USD", ExchangeRateMath.FromDecimal(1.085000m)),
            (new DateOnly(2025, 1, 15), "EUR", "USD", ExchangeRateMath.FromDecimal(1.090000m)),
            (new DateOnly(2025, 2, 1), "EUR", "USD", ExchangeRateMath.FromDecimal(1.078000m))
        };

        foreach (var (date, from, to, ppm) in seedRates)
            AddExchangeRate(new ExchangeRate(0, date, from, to, ppm));

        AddRecurringTransaction(new RecurringTransaction(
            0,
            "Monthly utility reserve",
            "Monthly utility reserve",
            "UTILITIES",
            31,
            AccountId("5400"),
            AccountId("1000"),
            95_00L,
            "USD",
            "Seeded day-31 example. February clamps to its last day."));
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void AddParam(SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public void Dispose()
    {
        if (_ownsConnection)
            _connection.Dispose();
    }
}
