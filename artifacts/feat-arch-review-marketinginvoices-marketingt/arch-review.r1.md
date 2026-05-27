```markdown
# Architecture Review: Remove Unused `Platform` Field from `MarketingTransaction`

## Skip Design: true

## Architectural Fit Assessment

The change aligns cleanly with the existing layering and reinforces the intended invariant of the MarketingInvoices module:

- **Adapter pattern with platform-at-source authority.** `IMarketingTransactionSource` (Domain) exposes a single `Platform` property at the *source* level. `MarketingInvoiceImportService.ImportAsync` (Application) reads `source.Platform` exclusively when persisting (`MarketingInvoiceImportService.cs:70`) and when logging/exists-checking. Per-transaction `Platform` is dead data that contradicts this invariant.
- **Layer separation preserved.** The removed field is internal to the Domain layer's in-memory transfer object; it never crosses an HTTP, MediatR, or OpenAPI contract. No client regeneration, no migration.
- **Persistence is unaffected.** `ImportedMarketingTransaction.Platform` (the persisted column with `IX_ImportedMarketingTransactions_Platform_TransactionId` index) remains, populated from `source.Platform` — that contract does not change.

Verdict: the refactor is purely a YAGNI cleanup that improves the contract honesty of the domain model. No architectural risks.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│  Domain  (Anela.Heblo.Domain.Features.MarketingInvoices)        │
│                                                                 │
│   IMarketingTransactionSource         MarketingTransaction      │
│     ├─ string Platform   (KEEP)         ├─ TransactionId        │
│     └─ GetTransactionsAsync()           ├─ Platform  ← REMOVE   │
│                                         ├─ Amount              │
│                                         ├─ TransactionDate     │
│                                         ├─ Description         │
│                                         ├─ Currency            │
│                                         └─ RawData             │
└─────────────────────────────────────────────────────────────────┘
        │ implements                              ▲ produces
        ▼                                         │
┌────────────────────────────┐         ┌──────────────────────────┐
│ Adapters (Meta / Google)   │────────▶│ Application Service      │
│  MetaAdsTransactionSource  │         │ MarketingInvoiceImport-  │
│  GoogleAdsTransactionSource│         │ Service.ImportAsync      │
│   (drop `Platform = ...`)  │         │  uses source.Platform    │
└────────────────────────────┘         │  to persist Imported-    │
                                       │  MarketingTransaction    │
                                       └──────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Remove the field outright (not deprecate)
**Options considered:**
- (A) Delete the `Platform` property and all initializers immediately.
- (B) Mark `[Obsolete]`, ship across releases, delete later.

**Chosen approach:** (A) Delete outright.

**Rationale:** `MarketingTransaction` is an internal Domain DTO with zero external consumers (no API contract, no persisted column, no OpenAPI client). It is not a `record` so OpenAPI parameter ordering is not a concern (per the project's DTO rules). The codebase is a solo-dev workspace with no downstream binary consumers. Obsolete annotations add noise for no benefit here.

#### Decision 2: Do not change `IMarketingTransactionSource.Platform`
**Options considered:**
- (A) Keep the source-level `Platform` on the interface.
- (B) Remove it and have the service receive `Platform` as a separate argument.

**Chosen approach:** (A) Keep it.

**Rationale:** The service reads `source.Platform` for logging, the duplicate-staging guard, the `ExistsAsync` query, and the persisted entity. It is also referenced by the MediatR handler (`ImportMarketingInvoicesHandler.cs:29` matches sources by platform). Touching this surface is out of scope for the cleanup and would broaden the blast radius unnecessarily.

#### Decision 3: Update adapter unit-test assertions on `tx.Platform`
**Options considered:**
- (A) Delete the two `tx.Platform.Should().Be(...)` assertions in `MetaAdsTransactionSourceTests` and `GoogleAdsTransactionSourceTests`.
- (B) Replace them with assertions on `source.Platform`.

**Chosen approach:** (A) Delete.

**Rationale:** `source.Platform` is a trivial constant (`PlatformName`) and already exercised implicitly via the import-service tests and adapter wiring tests. Asserting a constant returned by a property getter adds no coverage. The spec already permits trivial assertion removal.

## Implementation Guidance

### Directory / Module Structure

No new files. Edit only the following:

| Path | Change |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs` | Delete the `Platform` property. |
| `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs` | Remove `Platform = Platform,` initializer at line 68. |
| `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` | Remove `Platform = Platform,` initializer at line 39. |
| `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | Remove all `Platform = "TestPlatform",` initializer entries in `MarketingTransaction` object initializers (≈10 sites at lines 38–39, 74, 103–104, 139–140, 197–198, 237, 289, 342). |
| `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs` | Delete the `tx.Platform.Should().Be("MetaAds");` assertion at line 72. |
| `backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs` | Delete the `tx.Platform.Should().Be("GoogleAds");` assertion at line 28. |

Do **not** touch:
- `IMarketingTransactionSource` interface — `Platform` property stays.
- `ImportedMarketingTransaction` entity and its `ImportedMarketingTransactionConfiguration` — DB column and index unchanged.
- `MarketingInvoiceImportService.cs` — already uses `source.Platform`; verify by reading after edit but no edit required.
- Adapter `Platform => PlatformName` properties on `MetaAdsTransactionSource` / `GoogleAdsTransactionSource` — these implement the interface and remain.

### Interfaces and Contracts

- `IMarketingTransactionSource.Platform` — unchanged.
- `MarketingTransaction` — narrowed (one fewer property). This is a Domain-internal POCO; not a wire contract, not persisted, not exposed via OpenAPI. No DTO/contract rules violated.
- `ImportedMarketingTransaction` — unchanged (entity + EF configuration + index unchanged).
- `MarketingImportResult`, `ImportMarketingInvoicesRequest/Response` — unchanged.

### Data Flow

For each platform import (Meta or Google), unchanged in observable behavior:

```
Job → Handler → Service.ImportAsync(source, from, to)
   1. transactions = source.GetTransactionsAsync(from, to)
        // adapter constructs MarketingTransaction WITHOUT Platform
   2. for each tx:
        skip if Currency empty
        skip if (source.Platform, tx.TransactionId) already staged or persisted
        new ImportedMarketingTransaction { Platform = source.Platform, ... }
        repo.AddAsync(entity)
   3. repo.SaveChangesAsync()
```

The Platform written into `ImportedMarketingTransaction` is byte-identical before and after the change because `source.Platform` (interface getter returning `PlatformName` constant) was already the authoritative source.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A consumer outside the explored set reads `MarketingTransaction.Platform`. | Low | Grep verified zero non-initializer reads. Compiler will catch any miss when the property is deleted — fail-fast. |
| Adapter unit tests that assert `tx.Platform` fail after delete. | Low (known) | Drop the two assertions in `MetaAdsTransactionSourceTests` and `GoogleAdsTransactionSourceTests` listed above. |
| EF Core entity `ImportedMarketingTransaction` accidentally edited and triggers a migration. | Low | Limit edits to the files listed in the table. Run `dotnet ef migrations list` after build to confirm no model snapshot change is required (none should be — only Domain DTO is touched). |
| Hidden serialization of `MarketingTransaction` to JSON (e.g., diagnostics) drops the field silently. | Low | None found via grep (`MarketingTransaction` is never serialized — only `RawData` payloads are). Acceptable. |

## Specification Amendments

The spec is correct as written. Two small clarifications worth recording in the implementation commit message:

1. **FR-4 scope is concrete.** The only test project with `MarketingTransaction { Platform = ... }` initializers is `MarketingInvoiceImportServiceTests.cs` (≈10 sites). All others reference `Platform` on different types (`ImportMarketingInvoicesRequest`, `ImportMarketingInvoicesResponse`, `ImportedMarketingTransaction`) and are out of scope.
2. **Two adapter-test assertions exist** on `tx.Platform` (`MetaAdsTransactionSourceTests.cs:72`, `GoogleAdsTransactionSourceTests.cs:28`). Delete them — they assert a property that no longer exists. The spec's FR-2/FR-3 escape hatch ("trivial removal of assertions … if present") covers this; calling it out explicitly avoids ambiguity during review.

## Prerequisites

None. No migrations, no config, no infrastructure changes, no feature flags. The change can be implemented and validated entirely with:

- `dotnet build` on the solution
- `dotnet test` on `Anela.Heblo.Tests`
- `dotnet format` on the touched files
```