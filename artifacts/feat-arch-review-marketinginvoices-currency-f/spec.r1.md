# Specification: Persist Currency, Description, and RawData on Imported Marketing Transactions

## Summary
The `MarketingInvoiceImportService` discards three fields (`Currency`, `Description`, `RawData`) that are populated by every existing `IMarketingTransactionSource` adapter (Google Ads, Meta Ads). The most critical of these is `Currency`: storing `Amount` as `numeric(18,2)` without a currency code creates a silent multi-currency bug the moment any ad account bills in a non-default currency. This spec persists all three fields end-to-end, adds a migration, and backfills existing rows with the assumed default currency code so monetary data carries explicit context.

## Background
`MarketingTransaction` (domain value object) declares seven properties; the import path only maps four of them onto the persisted entity `ImportedMarketingTransaction`:

| Property        | Populated by adapters? | Persisted? |
|-----------------|------------------------|------------|
| `TransactionId` | Yes                    | Yes        |
| `Platform`      | Yes (from source)      | Yes        |
| `Amount`        | Yes                    | Yes        |
| `TransactionDate` | Yes                  | Yes        |
| `Description`   | **Yes**                | **No**     |
| `Currency`      | **Yes**                | **No**     |
| `RawData`       | **Yes**                | **No**     |

Adapter evidence (already in the codebase):
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs:42-44` sets `Currency = r.CurrencyCode`, `Description = r.Name ?? "Google Ads billing period"`, `RawData = JsonSerializer.Serialize(r)`.
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs:71-73` sets `Currency = item.Currency`, `Description = item.PaymentType`, `RawData = JsonSerializer.Serialize(item, JsonOptions)`.

The mapping in `MarketingInvoiceImportService.cs:58-65` then throws this data away. Because adapters actually provide currency values today (Google Ads returns ISO 4217 in `CurrencyCode`; the Meta Ads Graph API returns it in the `currency` field), the brief's option **A — persist currency** is the right path: removing the fields would mean deleting real upstream signal. Description and RawData are persisted alongside Currency because they share the same pipeline, are already populated, and provide audit/diagnostic value at trivial storage cost.

## Functional Requirements

### FR-1: Add `Currency` column to `ImportedMarketingTransaction`
Add a non-nullable `Currency` property (ISO 4217 alphabetic code, 3 characters) to the entity and its EF configuration. The import service must map `MarketingTransaction.Currency` into this column on insert.

**Acceptance criteria:**
- `ImportedMarketingTransaction.Currency` exists as `string` (non-nullable, default `string.Empty`).
- `ImportedMarketingTransactionConfiguration` registers the column as `character varying(3)`, `IsRequired()`, column name `Currency`.
- `MarketingInvoiceImportService.ImportAsync` copies `transaction.Currency` into the new entity at line 58-65.
- Unit test in `MarketingInvoiceImportServiceTests` verifies an imported entity carries the source transaction's currency.

### FR-2: Add `Description` column to `ImportedMarketingTransaction`
Add a nullable `Description` property to the entity and its EF configuration; map it in the import service.

**Acceptance criteria:**
- `ImportedMarketingTransaction.Description` exists as `string?`.
- EF configuration registers column `Description` as `character varying(500)`, nullable.
- Import service maps `transaction.Description` (passing through empty strings as-is — adapters guarantee non-null).
- Unit test asserts description round-trips through import.

### FR-3: Add `RawData` column to `ImportedMarketingTransaction`
Add a nullable `RawData` property to the entity and its EF configuration; map it in the import service.

**Acceptance criteria:**
- `ImportedMarketingTransaction.RawData` exists as `string?`.
- EF configuration registers column `RawData` as `text`, nullable.
- Import service maps `transaction.RawData`.
- Unit test asserts raw-data payload round-trips through import.

### FR-4: Database migration with backfill
Create a new EF Core migration that adds the three columns and backfills existing rows. Existing rows are assumed to be CZK (see Open Questions / Assumptions).

**Acceptance criteria:**
- New migration file `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs` exists.
- Migration `Up` adds three columns to `public."ImportedMarketingTransactions"`:
  - `Currency character varying(3) NOT NULL DEFAULT 'CZK'` (default exists only to satisfy NOT NULL during backfill; remove default after column is added).
  - `Description character varying(500) NULL`.
  - `RawData text NULL`.
- After the column is created with the default, the migration removes the default constraint so new inserts must supply a value explicitly (`AlterColumn` without `DefaultValue`).
- Migration `Down` drops all three columns.
- `ApplicationDbContextModelSnapshot.cs` updated accordingly.
- `dotnet ef database update` runs cleanly against a fresh dev database.

### FR-5: Import service maps all three fields
The construction of `ImportedMarketingTransaction` inside `MarketingInvoiceImportService.ImportAsync` must include `Currency`, `Description`, and `RawData`. No other behavior changes.

**Acceptance criteria:**
- The object initializer at `MarketingInvoiceImportService.cs:58-65` includes the three additional properties.
- All existing tests in `MarketingInvoiceImportServiceTests` continue to pass.
- A new test verifies that when the source transaction has `Currency = "EUR"`, the persisted entity has `Currency = "EUR"` (i.e. it is not normalized or coerced to CZK).

### FR-6: Empty / missing Currency defensive handling
If an adapter ever returns a `MarketingTransaction` with an empty or whitespace `Currency`, the import service must log a warning and skip the transaction (counting it under `result.Failed`). Storing a transaction with an unknown currency is worse than skipping it, because downstream consumers cannot disambiguate.

**Acceptance criteria:**
- Empty/whitespace `transaction.Currency` ⇒ transaction not persisted, `result.Failed++`, warning logged with `TransactionId` and `Platform`.
- Unit test covers this path.
- Description and RawData are NOT subject to this check (they are optional/diagnostic).

## Non-Functional Requirements

### NFR-1: Performance
No regression to import throughput. The added columns are simple scalar writes; no extra round-trips or queries.

**Acceptance criteria:**
- No new database calls introduced in `ImportAsync`.
- Existing batch behavior (single `SaveChangesAsync` at the end) is preserved.

### NFR-2: Data integrity
Currency must always be present on newly-imported rows. Existing rows are explicitly backfilled to a known value rather than left null, so every row in the table has an interpretable currency.

**Acceptance criteria:**
- `Currency` column is `NOT NULL` after migration.
- No code path can insert a row with `Currency = null` or `Currency = ""` (FR-6 enforces this).
- Migration backfill is deterministic and idempotent.

### NFR-3: Backward compatibility
This change is purely additive at the data layer. Existing callers of `IImportedMarketingTransactionRepository` keep working because no fields were removed or renamed.

**Acceptance criteria:**
- No existing test fails on the change set unrelated to the new behavior.
- No public method signatures change.

### NFR-4: Observability
Failed imports caused by missing currency are visible in logs with enough context to diagnose which adapter produced bad data.

**Acceptance criteria:**
- Warning log includes structured properties `TransactionId`, `Platform`, and a clear message about the missing currency.

## Data Model

### `ImportedMarketingTransaction` entity (after change)
```csharp
public class ImportedMarketingTransaction : IEntity<int>
{
    public int Id { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty; // new — ISO 4217
    public DateTime TransactionDate { get; set; }
    public DateTime ImportedAt { get; set; }
    public bool IsSynced { get; set; } = false;
    public string? ErrorMessage { get; set; }
    public string? Description { get; set; } // new
    public string? RawData { get; set; }     // new
}
```

### Table `public."ImportedMarketingTransactions"` (after migration)
| Column            | Type                     | Null | Notes                              |
|-------------------|--------------------------|------|------------------------------------|
| Id                | integer                  | NO   | identity                           |
| TransactionId     | character varying(255)   | NO   | unique with Platform               |
| Platform          | character varying(50)    | NO   | unique with TransactionId          |
| Amount            | numeric(18,2)            | NO   |                                    |
| **Currency**      | **character varying(3)** | **NO** | **NEW; ISO 4217**                |
| TransactionDate   | timestamp                | NO   |                                    |
| ImportedAt        | timestamp                | NO   |                                    |
| IsSynced          | boolean                  | NO   | default false                      |
| ErrorMessage      | text                     | YES  |                                    |
| **Description**   | **character varying(500)** | **YES** | **NEW**                       |
| **RawData**       | **text**                 | **YES** | **NEW; serialized source payload** |

Unique index `IX_ImportedMarketingTransactions_Platform_TransactionId` is unchanged.

## API / Interface Design
No public API changes. The change is internal to the marketing-invoices import pipeline.

Internal seams touched:
- `Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` — three new properties.
- `Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` — three new property configurations.
- `Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs:58-65` — extend object initializer, add empty-currency guard.
- `Anela.Heblo.Persistence/Migrations/{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs` — new migration.

## Dependencies
- EF Core (already in use) for migration tooling.
- xUnit + FluentAssertions + NSubstitute (already used by `MarketingInvoiceImportServiceTests`).
- Postgres dev/staging database for migration verification (database migrations are manual per CLAUDE.md project facts).

No new packages, no new external services.

## Out of Scope
- Currency conversion at import time (amounts are stored verbatim in source currency; conversion to a reporting currency is a downstream concern).
- Backfilling historical `Description` or `RawData` for existing rows — those columns stay null for pre-migration data because the original payloads were not retained.
- Adding aggregate/reporting views that group by currency.
- Validating currency codes against an ISO 4217 enumeration. The column accepts any 3-character string; validation is upstream's responsibility.
- Renaming or otherwise restructuring `MarketingTransaction` or `ImportedMarketingTransaction`.
- Changes to `IsSynced` / downstream sync behavior (e.g. propagating currency to whatever consumes `IsSynced = false` rows).
- Frontend or API surface changes — there is no consumer of these columns outside the import pipeline today.

## Open Questions

### Assumed and decided (please flag if wrong)
1. **Backfill currency for existing rows = `"CZK"`.** The brief states "the rest of the system assumes CZK"; this is the assumed default for pre-migration data. If existing rows include any non-CZK ad accounts, the migration would mislabel them. Recommended verification: run `SELECT DISTINCT Platform FROM "ImportedMarketingTransactions"` on staging and confirm all imports to date were from CZK-billed accounts before applying the migration to production.

### Genuinely open
*(none — proceeding on the assumption above; flag if it doesn't hold and the migration backfill logic will be revised before production rollout.)*

## Status: COMPLETE