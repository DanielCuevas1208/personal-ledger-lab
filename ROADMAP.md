# Personal Ledger Lab roadmap

This roadmap keeps releases small. Each milestone has a stable id.

Status values are `complete`, `planned`, and `blocked`.

## Complete releases

### `foundation-import` — complete

Outcome: Import statements, reconcile balances, explain rules, summarize months, export CSV, and persist locally.

Verification: `dotnet test PersonalLedgerLab.sln`

### `budget-variance` — complete

Outcome: Set monthly or recurring budgets and explain variance rows.

Verification: `dotnet test tests/PersonalLedgerLab.Tests/PersonalLedgerLab.Tests.csproj --filter BudgetServiceTests`

### `statement-adapters` — complete

Outcome: Read common CSV and OFX bank statements with automatic format detection.

Verification: `dotnet test tests/PersonalLedgerLab.Tests/PersonalLedgerLab.Tests.csproj --filter StatementImporterTests`

### `multi-currency-foundation` — complete

Outcome: Convert imported rows and show consolidated balances with exchange-rate evidence.

Verification: `dotnet test tests/PersonalLedgerLab.Tests/PersonalLedgerLab.Tests.csproj --filter ExchangeRateServiceTests`

## Current release

### `scheduled-postings` — complete

Outcome: Review monthly recurring entries and post selected entries once.

Deliverable: Core recurrence service, SQLite templates, durable occurrence markers, UI preview, and seeded demo data.

Acceptance:

- Preview an inclusive date range.
- Clamp day 29, 30, or 31 to month end.
- Post selected entries with balanced postings.
- Repeated posting creates no duplicate occurrence.
- Reconciliation totals stay unchanged on repeat runs.

Verification:

```pwsh
dotnet build PersonalLedgerLab.sln --configuration Release --no-restore -warnaserror -m:1
dotnet test tests/PersonalLedgerLab.Tests/PersonalLedgerLab.Tests.csproj --configuration Release --no-restore --no-build
```

## Planned releases

### `reports` — planned

Outcome: Compare net worth and category spending across months.

Deliverable: Add two local charts and a print-ready report view.

Acceptance:

- Show monthly net worth from stored balances.
- Show category spending by month.
- Export the displayed report without changing ledger rows.

Verification: `dotnet test tests/PersonalLedgerLab.Tests/PersonalLedgerLab.Tests.csproj --filter Reports`

### `trial-balance` — planned

Outcome: Reconcile stored accounts against a supplied manual trial balance.

Deliverable: Import a synthetic trial balance and export explained differences.

Acceptance:

- Report exact matches.
- Report missing accounts.
- Report currency mismatches.
- Leave postings unchanged.

Verification: `dotnet test tests/PersonalLedgerLab.Tests/PersonalLedgerLab.Tests.csproj --filter TrialBalance`

### `fx-review` — planned

Outcome: Review exchange-rate corrections with visible evidence.

Deliverable: Preview rate adjustments and preserve the original import rate.

Acceptance:

- Show realized and unrealized correction examples.
- Keep original import evidence.
- Explain corrected balances in the UI and export.

Verification: `dotnet test tests/PersonalLedgerLab.Tests/PersonalLedgerLab.Tests.csproj --filter FxReview`

## Limitations

The repository does not connect to bank services. Planned integrations need a separate security review and user decision.
