# Specification: Remove Unused `Platform` Field from `MarketingTransaction`

## Summary
The `Platform` property on the domain entity `MarketingTransaction` is written by every source adapter but never read by any consumer. This specification removes the dead field from the domain contract and the corresponding initializer code in both adapters, eliminating a misleading affordance in the MarketingInvoices module.

## Background
`MarketingTransaction` (`backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`) is the domain record produced by source adapters (`MetaAdsTransactionSource`, `GoogleAdsTransactionSource`) and consumed by `MarketingInvoiceImportService.ImportAsync` to construct `ImportedMarketingTransaction` rows.

Audit findings:
- Both adapters set `transaction.Platform = Platform` (the adapter's own `Platform` property).
- `MarketingInvoiceImportService.ImportAsync` always uses `source.Platform` (the adapter-level platform) — line 70 in `Services/MarketingInvoiceImportService.cs` — to populate the imported row.
- No production code or test assertion reads `MarketingTransaction.Platform`. Test data populates it, but only as part of object construction.

The field is therefore dead weight in the domain contract. It implies semantics that do not exist: that a single fetch could mix platforms per transaction, or that the service reconciles a per-transaction platform with the source's platform. Neither is true — the platform is authoritative at the source level. Leaving this field invites a future contributor to write logic that depends on it and silently get no effect.

This is a YAGNI cleanup motivated by the daily architecture review routine on 2026-05-26.

## Functional Requirements

### FR-1: Remove `Platform` property from `MarketingTransaction`
Delete the `Platform` property from the `MarketingTransaction` class at `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`.

**Acceptance criteria:**
- `MarketingTransaction` no longer declares a `Platform` property.
- `dotnet build` succeeds for the whole solution without warnings related to this change.
- Grepping the repository for `MarketingTransaction.*Platform` (and `transaction.Platform` within MarketingInvoices contexts) returns no remaining references to the removed property.

### FR-2: Remove `Platform` initializer from `MetaAdsTransactionSource`
Remove the `Platform = Platform,` line in the `MarketingTransaction` initializer in `MetaAdsTransactionSource` (around `MetaAdsTransactionSource.cs:68`).

**Acceptance criteria:**
- Adapter no longer references the removed property.
- Adapter still compiles and continues to expose its own source-level `Platform` value (consumed by `MarketingInvoiceImportService` via `source.Platform`).
- Any existing tests for `MetaAdsTransactionSource` pass without modification — except for trivial removal of assertions/initializers referencing the removed field, if present.

### FR-3: Remove `Platform` initializer from `GoogleAdsTransactionSource`
Remove the `Platform = Platform,` line in the `MarketingTransaction` initializer in `GoogleAdsTransactionSource` (around `GoogleAdsTransactionSource.cs:39`).

**Acceptance criteria:**
- Adapter no longer references the removed property.
- Adapter still compiles and continues to expose its own source-level `Platform` value.
- Any existing tests for `GoogleAdsTransactionSource` pass without modification — except for trivial removal of assertions/initializers referencing the removed field, if present.

### FR-4: Clean up test data that sets the removed property
Any test fixtures, mocks, or in-test object initializers that set `MarketingTransaction.Platform` must be updated to remove the assignment. No assertions need to be added or modified because no assertion currently reads the field.

**Acceptance criteria:**
- `dotnet test` for all MarketingInvoices-related test projects passes.
- No test fixture references the removed property.

### FR-5: Preserve import behavior end-to-end
The behavior of `MarketingInvoiceImportService.ImportAsync` must be identical before and after the change. It already reads `source.Platform` (the adapter-level value), so the `ImportedMarketingTransaction.Platform` output must remain the same for every input.

**Acceptance criteria:**
- Existing import tests pass with no changes to their assertions on `ImportedMarketingTransaction.Platform`.
- Manual or automated regression of an end-to-end import flow (Meta Ads source → service → imported rows) yields the same `Platform` values as before.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected or required. The removal eliminates one string property assignment per transaction during import — a negligible improvement.

### NFR-2: Security
No security implications. The property holds no sensitive data and no access control depends on it.

### NFR-3: Maintainability
The change improves maintainability by removing a misleading affordance from the domain contract. The domain model now reflects the actual invariant: platform is a source-level attribute, not a per-transaction one.

### NFR-4: Backwards compatibility
`MarketingTransaction` is an internal domain entity, not part of any public API or persisted schema (it is mapped into `ImportedMarketingTransaction` for persistence). No database migration, API client regeneration, or external contract update is required. This should be verified during implementation — see Open Questions if any DTO inadvertently exposes the field.

## Data Model

**Before:**
```csharp
public class MarketingTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;   // removed
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
```

**After:**
```csharp
public class MarketingTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
```

`ImportedMarketingTransaction` (the persisted entity) is **unchanged** — it continues to carry `Platform`, populated from the adapter-level `source.Platform`.

## API / Interface Design

No public API changes. No HTTP endpoints, MediatR requests, MVC controllers, or OpenAPI clients are affected. The adapter contract (`IMarketingTransactionSource` or equivalent) retains its source-level `Platform` property, which is what `MarketingInvoiceImportService.ImportAsync` actually reads.

## Dependencies

- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs` — domain entity to modify
- `backend/src/Anela.Heblo.../MetaAdsTransactionSource.cs` — adapter to update
- `backend/src/Anela.Heblo.../GoogleAdsTransactionSource.cs` — adapter to update
- `backend/src/Anela.Heblo.../Services/MarketingInvoiceImportService.cs` — already uses `source.Platform`, no change required (but verify after refactor)
- Test projects covering MarketingInvoices (unit tests for adapters and the import service)

No external services, packages, or feature flags are involved.

## Out of Scope

- Refactoring the broader MarketingInvoices module or its data flow.
- Renaming or restructuring `MarketingTransaction` or `ImportedMarketingTransaction`.
- Changes to `source.Platform` or the adapter interface.
- Adding new tests beyond updating fixtures that set the removed property.
- Database migrations — `ImportedMarketingTransaction` schema is untouched.
- Any frontend changes — this is a purely server-side domain cleanup.

## Open Questions

None.

## Status: COMPLETE