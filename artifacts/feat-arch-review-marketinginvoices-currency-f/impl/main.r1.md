---

# Implementation: Persist Currency, Description, and RawData on Imported Marketing Transactions

## What was implemented

Added `Currency` (ISO 4217, NOT NULL), `Description` (nullable varchar(500)), and `RawData` (nullable text) to the `ImportedMarketingTransaction` entity and the `public."ImportedMarketingTransactions"` table. The import service now maps all three fields from `MarketingTransaction`, and rejects any transaction with an empty/whitespace `Currency` (increments `Failed`, logs a Warning, skips without any DB round-trips). A backfill migration sets all existing rows to `'CZK'`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` — three new properties: `Currency` (string), `Description` (string?), `RawData` (string?)
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` — three new `builder.Property(...)` mappings
- `backend/src/Anela.Heblo.Persistence/Migrations/20260525152436_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs` — three-step migration: add nullable → SQL backfill 'CZK' → ALTER NOT NULL; Description and RawData as plain nullable adds
- `backend/src/Anela.Heblo.Persistence/Migrations/20260525152436_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.Designer.cs` — auto-generated designer
- `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` — auto-regenerated snapshot
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — empty-Currency guard at top of loop + three new fields in object initializer
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — 3 new tests
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` — regression fix: added `Currency = "CZK"` to handler test fixture

## Tests

`MarketingInvoiceImportServiceTests.cs` — 9 tests total (6 pre-existing + 3 new):
- `ImportAsync_NewTransaction_PersistsCurrencyDescriptionAndRawData` — verifies EUR currency is NOT coerced to CZK; Description and RawData round-trip verbatim
- `ImportAsync_EmptyCurrency_Skips_CountsFailed_DoesNotCallExistsOrAdd` — empty string → `Failed=1`, no DB calls, warning logged with TransactionId+Platform
- `ImportAsync_WhitespaceCurrency_TreatedAsEmpty_CountsFailed` — whitespace → same outcome as empty; pins `IsNullOrWhiteSpace` behavior

## How to verify

```bash
# From the repo root
dotnet build backend/Anela.Heblo.sln          # 0 errors
dotnet format backend/Anela.Heblo.sln         # no changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
# Expected: 9 MarketingInvoiceImportServiceTests pass; 3947/3950 total pass

# Before applying migration to production, run on prod DB:
# SELECT "Platform", COUNT(*) FROM public."ImportedMarketingTransactions" GROUP BY "Platform";
# Confirm all platforms are CZK-billed. If not, amend the backfill SQL before deploying.
```

## Notes

- Migration uses the project's established three-step pattern (not DEFAULT+drop-default) — matches prior art in `20251208184900_AddTransferIdColumnWithDataHandling.cs`
- Currency is stored verbatim (no ISO-4217 validation, no `ToUpperInvariant()` — out of scope per spec)
- Description empty string is stored as `""` not null (EF/Npgsql does not coerce; spec says verbatim mapping)
- Pre-deploy CZK verification is a manual step documented in the migration comment

## PR Summary

Persists the `Currency`, `Description`, and `RawData` fields that every marketing transaction adapter (Google Ads, Meta Ads) has always populated but the import service was silently discarding. The most critical fix is `Currency`: storing monetary amounts without a currency code creates a silent multi-currency bug whenever any ad account bills outside CZK.

The change is purely additive — three new columns, one guard, no public API or interface changes. Existing rows are backfilled to `'CZK'` using the repo's established add-nullable→UPDATE→ALTER migration pattern. Any inbound transaction with a missing currency is rejected early (before any DB round-trips), counted as `Failed`, and logged with full diagnostic context.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` — three new POCO properties
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` — EF column mappings for the three new properties
- `backend/src/Anela.Heblo.Persistence/Migrations/20260525152436_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs` — migration with three-step Currency backfill + nullable adds for Description/RawData
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — empty-Currency guard + all three fields in object initializer
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — 3 new tests (round-trip, empty-currency, whitespace-currency)
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` — regression fix: handler test fixture now supplies `Currency = "CZK"`

## Status

DONE