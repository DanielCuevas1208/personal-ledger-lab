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
        if (IsFreshDatabase)
            Seed();
    }

    public SqliteLedgerStore(SqliteConnection existingConnection)
    {
        _connection = existingConnection ?? throw new ArgumentNullException(nameof(existingConnection));
        _ownsConnection = false;
        DatabasePath = _connection.DataSource ?? string.Empty;
        EnsureSchema();
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
                       applied_rule_id, rule_explanation
                FROM transactions ORDER BY date, id;";
            using var reader = txCmd.ExecuteReader();
            while (reader.Read())
            {
                var tx = new Transaction
                {
                    Id = reader.GetInt64(0),
                    Date = DateOnly.Parse(reader.GetString(1), null),
                    Description = reader.GetString(2),
                    Reference = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Source = reader.IsDBNull(4) ? null : reader.GetString(4),
                    AppliedRuleId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    RuleExplanation = reader.IsDBNull(6) ? null : reader.GetString(6)
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
                        (date, description, reference, source, applied_rule_id, rule_explanation)
                    VALUES ($date, $desc, $ref, $source, $ruleId, $ruleExplain);
                    SELECT last_insert_rowid();";
                AddParam(txCmd, "$date", transaction.Date.ToString("yyyy-MM-dd"));
                AddParam(txCmd, "$desc", transaction.Description);
                AddParam(txCmd, "$ref", (object?)transaction.Reference ?? DBNull.Value);
                AddParam(txCmd, "$source", (object?)transaction.Source ?? DBNull.Value);
                AddParam(txCmd, "$ruleId", (object?)transaction.AppliedRuleId ?? DBNull.Value);
                AddParam(txCmd, "$ruleExplain", (object?)transaction.RuleExplanation ?? DBNull.Value);
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
                rule_explanation TEXT
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

            CREATE INDEX IF NOT EXISTS idx_postings_tx ON postings(transaction_id);
            CREATE INDEX IF NOT EXISTS idx_postings_acct ON postings(account_id);
            CREATE INDEX IF NOT EXISTS idx_transactions_date ON transactions(date);
        ");
    }

    private void Seed()
    {
        var seedAccounts = new (string Code, string Name, AccountType Type)[]
        {
            ("1000", "Checking", AccountType.Asset),
            ("2000", "Credit Card", AccountType.Liability),
            ("3000", "Opening Balances", AccountType.Equity),
            ("3999", "Uncategorized (Suspense)", AccountType.Equity),
            ("4000", "Salary", AccountType.Income),
            ("4100", "Other Income", AccountType.Income),
            ("5000", "Groceries", AccountType.Expense),
            ("5100", "Dining", AccountType.Expense),
            ("5200", "Coffee", AccountType.Expense),
            ("5300", "Transport", AccountType.Expense),
            ("5400", "Utilities", AccountType.Expense),
            ("5500", "Subscriptions", AccountType.Expense),
            ("5600", "Clothing", AccountType.Expense)
        };

        foreach (var (code, name, type) in seedAccounts)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO accounts (code, name, type, currency_code, note)
                VALUES ($code, $name, $type, 'USD', $note);";
            AddParam(cmd, "$code", code);
            AddParam(cmd, "$name", name);
            AddParam(cmd, "$type", (int)type);
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