# Pull request template

## Summary

Describe the change in one short paragraph. State what the pull request does and
why it is needed.

## Scope

List the files or modules this change touches. Mark any public API that changed.

## How to verify

List the commands a reviewer must run to confirm the change.

```pwsh
dotnet build PersonalLedgerLab.sln --configuration Release
dotnet test PersonalLedgerLab.sln --configuration Release --no-build
```

## Tests

State which tests cover the new behavior. State the test count before and after
the change.

## Checklist

- The build reports zero warnings.
- New tests are deterministic and run offline.
- The README and the roadmap reflect the change when user-visible.
- No secret, credentials, or local ledger file is committed.