# Personal Ledger Lab

A local-first finance tracker. It imports statement files, applies transparent
categorization rules, and explains every balance and spending summary.

The data stays on your machine. The application uses a SQLite database and a
double-entry model. No account connects to the internet, and no secret is
needed to run the demo or the tests.

## What this release does

This first release ships a coherent offline loop:

- Import a bundled CSV bank statement.
- Reconcile the statement running balance against the replayed ledger.
- Apply categorization rules that explain each decision in plain language.
- Edit the chart of accounts and the categorization rules.
- Show a monthly summary with double-entry balances per account.
- Export the ledger and the summary to CSV files.
- Keep all data in a local SQLite file under your user profile.

## Architecture

The solution has four projects. The domain logic has no database or user-interface
dependency. The data layer implements the persistence interface. The desktop
interface calls only the application service, so the storage and the rules stay
isolated.

```text
PersonalLedgerLab.Core      Domain model, rules, import, export, summaries.
PersonalLedgerLab.Data      SQLite persistence of the Core model.
PersonalLedgerLab.App       Avalonia desktop interface and the service facade.
PersonalLedgerLab.Tests     Deterministic xUnit tests for the core behavior.
```

The dependency flow is one way. Core depends on nothing. Data depends on Core.
App depends on Core and Data. Tests depend on all three.

## Double-entry model

Each transaction has two or more postings. A positive posting amount is a debit.
A negative posting amount is a credit. The signed sum of the postings in one
transaction is always zero.

Account types use the standard normal-balance rule:

- Asset and expense accounts increase with a debit.
- Liability, equity, and income accounts increase with a credit.

The account balance reverses the sign for credit-normal accounts. Net worth is
the sum of asset balances minus the sum of liability balances.

## Explainable rules

A rule is pure data. It has a name, a priority, a match type, a match value, and
a target category account. The engine orders rules by priority, then by id, and
returns the first match. Each result includes a sentence that states which rule
matched, the reason it matched, and the account that received the offset.

A transaction that matches no rule keeps its offset in the suspense account.
The suspense account has code 3999. The explanation names the suspense account
so the operator can see what was not categorized.

Match types are limited on purpose:

- `DescriptionContains` matches a case-insensitive phrase.
- `DescriptionRegex` matches a regular expression.
- `ReferenceEquals` matches the reference field exactly, case-insensitive.

## Setup

You need the .NET 8 SDK. The desktop interface uses Avalonia 11. Restore the
packages, then build the solution.

```pwsh
dotnet restore PersonalLedgerLab.sln
dotnet build PersonalLedgerLab.sln --configuration Release
```

Run the desktop interface. The first run creates a SQLite database in your
local application data folder and seeds the chart of accounts and the sample
rules.

```pwsh
dotnet run --project src/PersonalLedgerLab.App/PersonalLedgerLab.App.csproj
```

Set the `PERSONAL_LEDGER_DB` environment variable to use a different database
path, for example a file in your project folder for a clean demo run.

Run the tests. Fifty-three deterministic tests cover the core behavior and the
SQLite persistence.

```pwsh
dotnet test PersonalLedgerLab.sln
```

## Using the desktop interface

Open the Import tab. Pick the bank account, which is the seeded Checking asset.
Use Browse to select `samples/statements/sample-statement.csv`. Choose Import.
The status line reports the transaction count, the opening and closing balance,
and whether the statement reconciles.

Open the Categorization rules tab. The inflation-left panel edits a rule.
Double-click a row to load it. Use the preview box to test a description against
the active rules before you save the rule.

Open the Monthly summary tab. It lists each account total per month. Use the
Export buttons on the Import tab to write the ledger or the summary to CSV.

## Sample data

The bundled statement is `samples/statements/sample-statement.csv`. It has
fifteen rows for January 2025. The opening balance is 1000.00 USD. The closing
balance is 1807.48 USD.

The seeded chart of accounts uses these codes:

```text
1000 Checking        Asset
2000 Credit Card     Liability
3000 Opening Balances Equity
3999 Uncategorized Suspense Equity
4000 Salary          Income
4100 Other Income    Income
5000 Groceries       Expense
5100 Dining          Expense
5200 Coffee          Expense
5300 Transport       Expense
5400 Utilities       Expense
5500 Subscriptions   Expense
5600 Clothing        Expense
```

## Sample output

After the sample import, the status line reads:

```text
Imported 15 transactions. Opening 1000.00 closing 1807.48. Reconciles: True.
```

The monthly summary for January 2025 has these account totals:

```csv
Year,Month,AccountCode,AccountName,AccountType,DebitMinor,CreditMinor,NetMinor,NetMajor,Currency
2025,1,1000,Checking,Asset,150000,69252,80748,807.48,USD
2025,1,4000,Salary,Income,0,150000,150000,1500.00,USD
2025,1,5000,Groceries,Expense,20308,0,20308,203.08,USD
2025,1,5100,Dining,Expense,7890,0,7890,78.90,USD
2025,1,5200,Coffee,Expense,1525,0,1525,15.25,USD
2025,1,5300,Transport,Expense,10015,0,10015,100.15,USD
2025,1,5400,Utilities,Expense,21895,0,21895,218.95,USD
2025,1,5500,Subscriptions,Expense,1299,0,1299,12.99,USD
2025,1,5600,Clothing,Expense,6320,0,6320,63.20,USD
```

The income total is 1500.00 USD. The expense total is 692.52 USD. The net income
for the month is 807.48 USD, which equals the change in the Checking balance.

A trimmed ledger export shows how each transaction is explained:

```csv
TransactionId,Date,Description,Reference,Source,AccountId,AccountCode,AccountName,AccountType,DebitMinor,CreditMinor,Currency,RuleId,RuleExplanation
1,2025-01-03,Salary ACME Corp,PAYROLL,csv-import,1,1000,Checking,Asset,150000,0,USD,2,"Rule 'Salary' (priority 5) matched ... The offset moves to account 4000."
1,2025-01-03,Salary ACME Corp,PAYROLL,csv-import,2,4000,Salary,Income,0,150000,USD,2,"Rule 'Salary' (priority 5) matched ..."
4,2025-01-05,Coffee Time Corp latte,,csv-import,1,1000,Checking,Asset,0,540,USD,3,"Rule 'Coffee' (priority 20) matched ... account 5200."
4,2025-01-05,Coffee Time Corp latte,,csv-import,3,5200,Coffee,Expense,540,0,USD,3,"Rule 'Coffee' (priority 20) matched ..."
```

Rule explanations contain the matched phrase, the priority, and the target
account code so a reader can audit a total back to a statement row.

## Limitations

This first release is intentionally small. The following items are out of
scope for this run and are listed in the roadmap:

- Single-currency ledgers. The schema stores currency per posting, but the
  summary and the import in this release assume one currency per file.
- One statement format. The importer reads six columns in a fixed order.
- No budget planning or forecasting.
- No multi-user data or sync. The data file is for one local profile.
- The desktop interface is built in C# rather than XAML. The Avalonia XAML
  source generator needs a newer compiler than the .NET 8 SDK in this
  environment ships. Building the layout in code keeps the run reproducible.

## Test status

Fifty-three deterministic tests pass. They cover money math, ledger balance,
the rule engine, the CSV parser, statement reconciliation, monthly summaries,
CSV export, SQLite persistence, and an end-to-end run of the sample file.

```text
Passed!  - Failed: 0, Passed: 53, Skipped: 0, Total: 53
```

The CI workflow builds and tests the solution on Windows and Linux. Package
versions are pinned exactly in each csproj, so a restore is reproducible.

## Roadmap

The next releases can ship in this order, one coherent release at a time:

1. Statement adapters: add more bank CSV formats and an OFX reader.
2. Budgets: add monthly budget targets per category and a variance report.
3. Multi-currency: add exchange rates at posting time and a consolidated view.
4. Scheduled transactions: add recurring entries that generate postings.
5. Reports: add a net-worth chart, a category trend chart, and a print view.
6. Integrity: add a periodic full reconciliation against a manual trial balance.

## License

MIT. See the LICENSE file.