# Security policy

Personal Ledger Lab is a local-first finance tracker. The application does not
connect to the internet to send or receive account data. No statement file,
ledger row, or secret leaves the machine you run it on.

## Reporting a security issue

Report a real or suspected security issue by opening a private issue on this
repository, or contact the maintainers through the listed channels. Do not
publish exploit details until the maintainers acknowledge the report.

## Scope

The application stores all data in a SQLite file on the local file system.
Threats that come from an attacker who already has full read access to that file
are out of scope, because the data is plain local data. In-scope issues include
SQL injection, corrupted schema migrations, and any path that writes data
outside the configured database location.

## Supported versions

Only the latest release line receives fixes.