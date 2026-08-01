# Personal Ledger Lab

Personal Ledger Lab is a local-first finance tracker. It imports statement
files, applies categorization rules, and explains each balance and spending
summary.

The data stays on the local machine. The application uses SQLite and a
double-entry model. It does not connect to the internet. The demo and tests do
not need a secret.

## What this release does

This release ships a coherent offline loop and a budget layer:

- Import a bundled CSV bank statement.
- Reconcile the statement running balance against the replayed ledger.
- Apply categorization rules that explain each decision in plain language.
- Edit the chart of accounts and the categorization rules.
- Set a monthly or recurring budget target per category account.
- Show a variance report that explains each under and over result.
- Show a monthly summary with double-entry balances per account.
- Export the ledger, the summary, the budgets, and the variance to CSV files.
- Keep all data in a local SQLite file under your user profile.

## Architecture

The solution has four projects. The domain logic has no database or user-interface
dependency. The data layer provides persistence. The desktop interface calls
the application service. This keeps storage and rules separate.

```text
PersonalLedgerLab.Core      Domain model, rules, import, export, summaries, budgets.
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

## Budgets and variance

A budget is a target for one category account in one month. A specific budget
sets a year and a month. A recurring budget uses year 0 and month 0, and it
applies to any month that has no specific budget for that account. A specific
budget beats a recurring budget when both cover the same month.

The variance service compares each budget with the matching monthly total. The
variance is the actual net minus the limit. The service writes one sentence per
result. An expense result says whether the spending went over or under the cap.
An income result says whether the earning went above or below the target.

The variance report covers only the budgets that match a month in the summary.
A budgeted category with no activity counts as zero actual. The operator can
export the budgets and the variance to CSV for offline review.

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

Run the tests. Sixty-six deterministic tests cover the core behavior, the
budgets, and the SQLite persistence.

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

Open the Budgets tab. The left panel edits a budget. Set a year, a month, a
category, and a limit. Use year 0 and month 0 for a recurring budget. Double
click a row on the right grid to load it. The lower grid shows the variance for
each month. Use the Export buttons to write the budgets or the variance to CSV.

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

The seeded budgets set January 2025 targets for each seeded category:

```text
4000 Salary         1500.00 USD target
5000 Groceries       250.00 USD cap
5100 Dining           80.00 USD cap
5200 Coffee           25.00 USD cap
5300 Transport        90.00 USD cap
5400 Utilities       250.00 USD cap
5500 Subscriptions    15.00 USD cap
5600 Clothing         60.00 USD cap
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

The budget variance report for January 2025 reads:

```csv
Year,Month,AccountCode,AccountName,AccountType,LimitMinor,ActualMinor,VarianceMinor,VarianceMajor,Currency,Recurring,Explanation
2025,1,4000,Salary,Income,150000,150000,0,0.00,USD,False,"Spent 1500.00 USD against a 1500.00 USD target ... exactly on target."
2025,1,5000,Groceries,Expense,25000,20308,-4692,-46.92,USD,False,"... 46.92 USD under budget."
2025,1,5100,Dining,Expense,8000,7890,-110,-1.10,USD,False,"... 1.10 USD under budget."
2025,1,5200,Coffee,Expense,2500,1525,-975,-9.75,USD,False,"... 9.75 USD under budget."
2025,1,5300,Transport,Expense,9000,10015,1015,10.15,USD,False,"... 10.15 USD over budget."
2025,1,5400,Utilities,Expense,25000,21895,-3105,-31.05,USD,False,"... 31.05 USD under budget."
2025,1,5500,Subscriptions,Expense,1500,1299,-201,-2.01,USD,False,"... 2.01 USD under budget."
2025,1,5600,Clothing,Expense,6000,6320,320,3.20,USD,False,"... 3.20 USD over budget."
```

Two expense accounts go over budget: Transport and Clothing. The rest stay
under. The Salary income lands exactly on target. Each row carries the sentence
the operator needs to audit the result back to the monthly total.

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

This release is intentionally small. The following items are out of scope
for this run and are listed in the roadmap:

- Single-currency ledgers. The schema stores currency per posting, but the
  summary and the import in this release assume one currency per file.
- One statement format. The importer reads six columns in a fixed order.
- Budgets track one category per row and one month per row. They do not roll
  over, they do not forecast, and they do not set envelope balances.
- No multi-user data or sync. The data file is for one local profile.
- The desktop interface is built in C# rather than XAML. The Avalonia XAML
  source generator needs a newer compiler than the .NET 8 SDK in this
  environment ships. Building the layout in code keeps the run reproducible.

## Test status

Sixty-six deterministic tests pass. They cover money math, ledger balance,
the rule engine, the CSV parser, statement reconciliation, the budget service,
budget persistence, CSV export, SQLite persistence, and an end-to-end run of
the sample file with variance.

```text
Passed!  - Failed: 0, Passed: 66, Skipped: 0, Total: 66
```

The CI workflow builds and tests the solution on Windows and Linux. Package
versions are pinned exactly in each csproj, so a restore is reproducible.

## Roadmap

The roadmap records what is complete and what remains. Each remaining step
ships as one coherent release.

1. Done: offline import, reconciliation, explainable rules, monthly summary,
   CSV export, and SQLite persistence.
2. Done: monthly and recurring budgets per category, a variance report, and
   budget and variance CSV export.
3. Statement adapters: add more bank CSV formats and an OFX reader.
4. Multi-currency: add exchange rates at posting time and a consolidated view.
5. Scheduled transactions: add recurring entries that generate postings.
6. Reports: add a net-worth chart, a category trend chart, and a print view.
7. Integrity: add a periodic full reconciliation against a manual trial balance.

## License

MIT. See the LICENSE file.
