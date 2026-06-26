I have enough context. The codebase confirms: Vertical Slice + MediatR + EF Core, single `ApplicationDbContext`, manual migrations, existing precedent for "add-as-nullable → backfill → alter NOT NULL" (see `20251208184900_AddTransferIdColumnWithDataHandling`). The spec's proposal aligns with all existing patterns. Now I'll write the review.

```markdown
# Architecture Review: Persist Currency, Description, and RawData on Imported Marketing Transactions

## Skip Design: true

This is a backend-only data-layer change. No UI, no API surface change, no new visual components.

## Architectural Fit Assessment

The proposal aligns cleanly with established patterns in this codebase:

- **Vertical Slice + module boundary respected.** All touched files live inside the `MarketingInvoices` slice (Domain entity, Persistence configuration, Application service, Migrations). No cross-module reach (`development_guidelines.md` ADR-001, ADR-002, ADR-003).
- **Single-DbContext / shared migrations folder convention** is honored — the migration lives in `Anela.Heblo.Persistence/Migrations/` and updates `ApplicationDbContextModelSnapshot.cs` (Phase 1 ADR).
- **Add-nullable → backfill → alter-NOT-NULL** is an established migration pattern in this repo (see `20251208184900_AddTransferIdColumnWithDataHandling.cs:44-67`, which adds `TransferId` as nullable, runs `UPDATE … WHERE … IS NULL`, then `AlterColumn(nullable: false)`). The spec should follow this exact shape rather than the "ADD COLUMN … DEFAULT 'CZK' then drop default" variant it currently describes — the project's prior art uses the multi-step pattern, and it's safer because backfill is an explicit, reviewable SQL statement rather than an implicit side-effect of `DEFAULT`.
- **Repository pattern** is unaffected; `IImportedMarketingTransactionRepository` does not need changes (purely additive on the entity).
- **Domain entity is a `class`, not a `record`** — already correct per the project's "DTOs/entities are classes" gotcha (CLAUDE.md). Spec preserves this.
- **Adapters already populate all three fields** — `GoogleAdsTransactionSource.cs:42-44` and `MetaAdsTransactionSource.cs:71-73` are unchanged-by-design. Existing adapter contracts are honored.
- **Test seam is intact** — `MarketingInvoiceImportServiceTests.cs` already sets `Currency`/`Description` on its `MarketingTransaction` fixtures (lines 38-39, 74, 103-104, 139-140, 197-198), so existing tests will compile unchanged. New behavior gets new tests.

The only architectural friction is FR-6's "skip on empty Currency" — see Decision 2 below.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Application slice: MarketingInvoices                 │
│                                                                         │
│  ImportMarketingInvoicesHandler  ──►  MarketingInvoiceImportService     │
│  (MediatR, unchanged)                  (FR-5: extend object initializer,│
│                                         FR-6: guard empty Currency)     │
│                                              │                          │
│                                              ▼                          │
│                          IImportedMarketingTransactionRepository        │
│                          (unchanged contract)                           │
└────────────────────────────────────────────────┬────────────────────────┘
                                                 │
                                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Domain: MarketingInvoices                                              │
│                                                                         │
│  ImportedMarketingTransaction (entity, IEntity<int>)                    │
│  ── +Currency : string                                                  │
│  ── +Description : string?                                              │
│  ── +RawData : string?                                                  │
└────────────────────────────────────────────────┬────────────────────────┘
                                                 │
                                                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Persistence: MarketingInvoices                                         │
│                                                                         │
│  ImportedMarketingTransactionConfiguration                              │
│  ── +Property(Currency)    column varchar(3), NOT NULL                  │
│  ── +Property(Description) column varchar(500), nullable                │
│  ── +Property(RawData)     column text, nullable                        │
│                                                                         │
│  Migrations/{ts}_AddCurrencyDescriptionRawDataToImportedMarketing…cs    │
│  (add-nullable → backfill 'CZK' → alter NOT NULL → snapshot updated)    │
└─────────────────────────────────────────────────────────────────────────┘

Adapters (UNCHANGED, already producing all three fields):
  GoogleAdsTransactionSource  ──►  MarketingTransaction { Currency, Description, RawData, … }
  MetaAdsTransactionSource    ──►  MarketingTransaction { Currency, Description, RawData, … }
```

### Key Design Decisions

#### Decision 1: Migration mechanics — add-nullable + UPDATE + alter, not DEFAULT-then-drop-default

**Options considered:**
- **(A) Spec's current approach:** `ADD COLUMN Currency varchar(3) NOT NULL DEFAULT 'CZK'`, then `ALTER COLUMN … DROP DEFAULT` (encoded as `AlterColumn` without `DefaultValue`).
- **(B) Three-step pattern used elsewhere in this repo:** `AddColumn(nullable: true)` → `migrationBuilder.Sql("UPDATE … SET Currency = 'CZK' WHERE Currency IS NULL")` → `AlterColumn(nullable: false)`.

**Chosen approach:** (B). The migration should follow the same shape as `20251208184900_AddTransferIdColumnWithDataHandling.cs`.

**Rationale:**
- Backfill becomes an explicit, reviewable SQL statement. A future reader reading the migration sees the assumption `'CZK'` on its own line, not implied via a transient `DEFAULT` clause.
- Generating this from EF tooling is cleaner: `dotnet ef migrations add …` does not natively emit "add with default then drop default" — you'd hand-edit the generated file. The three-step pattern can be expressed with a one-line `migrationBuilder.Sql(…)` inserted between two generated calls.
- `AlterColumn` to change a NOT NULL constraint after backfill is exactly what the prior migration does; reviewers will recognize the pattern.

#### Decision 2: Empty-Currency policy — fail-loud with explicit per-source override, or always-skip?

**Options considered:**
- **(A) Spec's FR-6:** Silently skip and count under `result.Failed` with a warning log.
- **(B) Reject loudly:** Treat empty Currency as a programming error in the adapter, throw a domain exception, fail the whole import batch.
- **(C) Skip + log, AND emit a metric / structured event so ops sees it.**

**Chosen approach:** (A) as specified, with one tightening — see Spec Amendment 1.

**Rationale:**
- (B) is too aggressive: a transient bad row from an upstream API would poison the whole batch. The existing per-transaction `try/catch` in `MarketingInvoiceImportService.cs:37-79` already establishes "isolate per-row failures, keep going" as the project's import philosophy.
- (A) is consistent with that philosophy. It does need to be counted as `Failed` (not `Skipped`), because `Skipped` in the existing service means "duplicate / already-imported" — a benign condition. Currency missing is a real defect, and conflating it with duplicates would hide it. The spec already gets this right.
- (C) is YAGNI today — the warning log with structured `TransactionId` + `Platform` properties is already pickup-able by whatever log aggregator exists, and there is no metrics infrastructure in scope here.

**Important nuance the spec doesn't state explicitly:** FR-6's guard must run **before** the duplicate-check (`ExistsAsync`) and before staging. Otherwise a malformed row would still consume a DB round-trip for `ExistsAsync`. See Spec Amendment 2.

#### Decision 3: Column types and sizes

**Options considered:**
- **Currency:** `varchar(3)` vs `char(3)` vs unbounded `text`.
- **Description:** `varchar(500)` vs `text`.
- **RawData:** `text` vs `jsonb`.

**Chosen approach:** Spec is correct as-is: `varchar(3)` / `varchar(500)` / `text`.

**Rationale:**
- `varchar(3)` matches ISO 4217 alphabetic codes and provides a cheap, defense-in-depth check against an upstream adapter accidentally writing `"EURO"`. `char(3)` would pad with spaces in some Postgres clients — undesirable.
- `varchar(500)` is generous for `Description` (Google Ads campaign names cap well under that; Meta `payment_type` is a short enum string).
- `text` for `RawData` is correct: payload size is unbounded and `jsonb` would force Postgres to validate JSON on every write, which the spec correctly rejects in "Out of Scope" (no validation of source payloads). Stay with `text`.

## Implementation Guidance

### Directory / Module Structure

All changes are edits to existing files plus one new migration. No new directories.

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` | Add three properties: `Currency` (non-nullable, default `string.Empty`), `Description` (`string?`), `RawData` (`string?`). |
| `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` | Add three `builder.Property(…)` calls matching the existing style (explicit `HasColumnName`, `HasColumnType`, `IsRequired()` for Currency only). |
| `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` | Extend the object initializer at lines 58-66 with the three fields. Insert the empty-Currency guard **before** the duplicate-check at line 39 (see Spec Amendment 2). |
| `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddCurrencyDescriptionRawDataToImportedMarketingTransactions.cs` | New file. Three-step `Currency` migration (add-nullable, backfill `'CZK'`, alter NOT NULL); plain add-nullable for `Description` and `RawData`. |
| `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` | Regenerated automatically by `dotnet ef migrations add`. |
| `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | Add three new test cases: (1) Currency round-trips non-CZK value, (2) Description+RawData round-trip, (3) empty Currency increments `Failed`, logs warning, does not call `AddAsync`. |

### Interfaces and Contracts

**No public interface changes.** Specifically unchanged:

- `IMarketingTransactionSource` — adapters already populate the fields; no contract amendment.
- `IImportedMarketingTransactionRepository` — purely additive entity changes are transparent to the repository surface.
- `ImportMarketingInvoicesRequest` / `Response` MediatR DTOs — no new fields.

**Internal signature change inside `MarketingInvoiceImportService`:** none — the existing `ImportAsync(source, from, to, ct)` signature stays. Only the body changes.

### Data Flow

For a single transaction during import:

```
1. ImportMarketingInvoicesHandler.Handle
     resolves IMarketingTransactionSource by Platform
2. MarketingInvoiceImportService.ImportAsync
     source.GetTransactionsAsync → List<MarketingTransaction>   (already carries Currency/Desc/RawData)
3. For each MarketingTransaction:
   a. [NEW — Spec Amendment 2] if string.IsNullOrWhiteSpace(transaction.Currency)
        → _logger.LogWarning(TransactionId, Platform, "missing currency, skipping")
        → result.Failed++
        → continue
   b. if stagedIds.Contains(TransactionId)  → Skipped++, continue   (unchanged)
   c. if await _repository.ExistsAsync(…)   → Skipped++, continue   (unchanged)
   d. Construct ImportedMarketingTransaction with all 7 mapped fields incl. Currency/Desc/RawData
   e. await _repository.AddAsync(entity, ct)
4. await _repository.SaveChangesAsync(ct)                          (single flush, unchanged)
```

The flush remains a single `SaveChangesAsync` call — NFR-1 holds because nothing new touches the DB per-row.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Existing production rows are not 100% CZK; backfill mis-labels them. | High | Per the spec's open question: run `SELECT DISTINCT Platform, COUNT(*) FROM "ImportedMarketingTransactions"` on staging/prod and cross-check with the source ad accounts' billing currencies **before** running the migration in prod. If any non-CZK accounts have imported historically, change the backfill SQL to a per-Platform `CASE WHEN Platform = 'GoogleAds' THEN 'EUR' …` mapping, or null-out the column and only enforce NOT NULL on rows newer than `now()`. Solo-dev project + manual migrations (CLAUDE.md project facts) makes this verification easy. |
| Currency string from upstream isn't a valid ISO 4217 code (typo, "eur" lowercase, "EURO"). | Medium | Out of scope per spec, but flag: if Meta Ads ever returns `"eur"` lowercased, downstream queries like `WHERE Currency = 'EUR'` will silently miss it. Acceptable for now; consider a normalization (`ToUpperInvariant()`) in a follow-up. Do **not** add ISO-4217 enum validation now — adapter responsibility per spec. |
| FR-6 skip-on-empty changes the meaning of `result.Failed` — callers (Hangfire job, monitoring) may interpret a non-zero `Failed` differently when its causes broaden. | Low | The existing per-transaction `catch` in `MarketingInvoiceImportService.cs:72-79` already increments `Failed` for unexpected exceptions, so "Failed includes non-DB errors" is already true. The empty-Currency case slots into existing semantics. No caller change needed; verify via the existing tests at lines 95-128. |
| RawData column holds raw API payloads that may contain secrets (access tokens, account IDs). | Medium | Inspect what `JsonSerializer.Serialize(item)` actually emits for both adapters — `MetaAdsTransactionSource.cs:113` uses `access_token` in URLs but not in the `item` model fields (which are `id`, `time`, `amount`, `currency`, `payment_type`). Google Ads `r` should be inspected too. If any token/secret appears in serialized payloads, redact in the adapter **before** assigning `RawData`. Out of scope for this spec, but call out in code review. |
| `Description` truncation to 500 chars silently drops data. | Low | Google Ads `Name` is well under 500; Meta `PaymentType` is a short enum. Acceptable. If truncation ever happens, EF Core will throw at `SaveChangesAsync` (Npgsql will reject), surfacing as a Failed row — fail-loud is correct here. |
| Migration runs on prod before staging verification of CZK assumption. | High | Manual migrations are the project norm (CLAUDE.md project facts). Add a one-line note in the migration's XML comment: `// Backfills existing rows to 'CZK'; verify SELECT DISTINCT Platform first.` Solo-developer environment makes this sufficient. |

## Specification Amendments

### Amendment 1: Migration shape — switch to add-nullable + UPDATE + alter
Replace FR-4's "ADD COLUMN … NOT NULL DEFAULT 'CZK' then remove default" mechanic with the project's established three-step pattern (see Decision 1, prior art `20251208184900_AddTransferIdColumnWithDataHandling.cs:44-67`):

```csharp
// 1. Add Currency as nullable
migrationBuilder.AddColumn<string>(
    name: "Currency",
    schema: "public",
    table: "ImportedMarketingTransactions",
    type: "character varying(3)",
    maxLength: 3,
    nullable: true);

// 2. Backfill (assumes all historical rows are CZK — verify on staging)
migrationBuilder.Sql(
    "UPDATE public.\"ImportedMarketingTransactions\" SET \"Currency\" = 'CZK' WHERE \"Currency\" IS NULL;");

// 3. Enforce NOT NULL
migrationBuilder.AlterColumn<string>(
    name: "Currency",
    schema: "public",
    table: "ImportedMarketingTransactions",
    type: "character varying(3)",
    maxLength: 3,
    nullable: false,
    oldClrType: typeof(string),
    oldType: "character varying(3)",
    oldMaxLength: 3,
    oldNullable: true);

// Description and RawData: simple AddColumn(nullable: true), no backfill, no alter.
```

`Down` reverses with three `DropColumn` calls.

### Amendment 2: Empty-Currency guard runs first, before duplicate-check
FR-6's "skip on empty Currency" guard must be placed **at the top of the per-transaction loop body, before** the `stagedIds.Contains` and `ExistsAsync` checks (lines 39 and 48 of the current service). Otherwise an empty-Currency row consumes a database round-trip and may be miscounted under `Skipped` if it happens to duplicate a prior valid row.

Updated acceptance criterion for FR-6:
> The guard appears at the top of the loop body, before the duplicate-check at line 39. Test asserts `ExistsAsync` is **never called** for an empty-Currency transaction.

### Amendment 3: Test additions
The existing test class already constructs fixtures with `Currency = "CZK"` everywhere (lines 38-39, 74, 103-104, 139-140, 197-198), so existing tests will keep passing without code change. Add explicitly:

1. **`ImportAsync_NewTransaction_PersistsCurrency_DescriptionAndRawData`** — single transaction with `Currency = "EUR"`, `Description = "campaign X"`, `RawData = "{\"foo\":1}"`. Capture the entity passed to `AddAsync` via `Moq.Callback` and assert all three round-trip verbatim (Currency must NOT be coerced to CZK).
2. **`ImportAsync_EmptyCurrency_Skips_CountsFailed_LogsWarning`** — `Currency = ""` ⇒ `result.Failed == 1`, `result.Imported == 0`, `_mockRepository.Verify(AddAsync, Never)`, `_mockRepository.Verify(ExistsAsync, Never)`, verify warning log with `TransactionId` and `Platform` structured properties.
3. **`ImportAsync_WhitespaceCurrency_TreatedAsEmpty`** — `Currency = "   "` ⇒ same outcome as case 2 (covers the `IsNullOrWhiteSpace` branch).

### Amendment 4: Document the CZK-assumption verification step
The spec's "Open Questions" section currently notes the CZK backfill assumption but doesn't tie it to a concrete pre-deploy check. Add to FR-4 acceptance criteria:
> Before running the migration against production, the developer runs `SELECT DISTINCT "Platform", COUNT(*) FROM public."ImportedMarketingTransactions" GROUP BY "Platform";` on prod, manually cross-checks expected billing currency per platform/account against the connected ad-account configuration, and confirms 100% CZK. If any platform's billing currency is not CZK, the migration's backfill SQL is amended to a per-Platform `CASE` expression before deploy.

## Prerequisites

- **None at the infrastructure / config / package level.** No new dependencies, no new DI registrations, no new secrets, no new feature flag.
- **Operational prerequisite (one-time, manual):** the CZK-assumption query in Amendment 4 must be run against production before `dotnet ef database update` is invoked there. This is consistent with "Database migrations are manual (not automated in deployment)" per CLAUDE.md.
- **Build prerequisite:** after editing `ImportedMarketingTransaction` and the configuration, run `dotnet ef migrations add AddCurrencyDescriptionRawDataToImportedMarketingTransactions --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`, then hand-edit the generated migration to insert the `Sql("UPDATE …")` between the `AddColumn` and `AlterColumn` calls per Amendment 1.
- **Verification before declaring done** (per CLAUDE.md): `dotnet build` + `dotnet format` clean; `dotnet test` on `Anela.Heblo.Tests` passes (existing 6 tests + 3 new); `dotnet ef database update` runs cleanly against a fresh local Postgres dev database; spot-check via `psql` that all three new columns exist with correct types and that the backfill populated `Currency = 'CZK'` for any pre-existing rows.
```