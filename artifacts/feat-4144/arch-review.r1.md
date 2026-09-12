# Architecture Review: Move MarketingImportResult to Contracts/ (MarketingInvoices)

## Skip Design: true

## Architectural Fit Assessment

This is a pure file-location/namespace fix, not a design change. `MarketingInvoices` is a complex feature (`UseCases/`, `Services/`, `Contracts/` all already present, per `docs/architecture/filesystem.md`), and `MarketingImportResult` is exactly the kind of type `Contracts/` exists for: a result DTO consumed across a `Services/` interface and a `UseCases/` handler. Its current location at the feature root is the only deviation from convention in this module — verified directly:

- `MarketingImportResult.cs` sits at `Features/MarketingInvoices/` root, namespace `...MarketingInvoices` — confirmed byte-for-byte matches the spec's quoted content.
- `Contracts/` already holds `IMarketingTransactionSource.cs` and `MarketingTransaction.cs` — confirmed.
- `IMarketingInvoiceImportService.cs` and `MarketingInvoiceImportService.cs` (in `Services/`) both already have `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` — confirmed; no using change needed in either.
- `ImportMarketingInvoicesHandler.cs` never names `MarketingImportResult` (consumes it via `var`) and already imports both `...Contracts` and `...Services` — confirmed; no using change needed.
- `ImportMarketingInvoicesHandlerTests.cs` has both `using Anela.Heblo.Application.Features.MarketingInvoices;` (feature-root, resolving the type today) and `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` already — confirmed. After the move, the first becomes an unused using and must be dropped.
- `MarketingInvoiceImportServiceTests.cs` does not reference `MarketingImportResult` by name — confirmed; no change needed.
- Repo-wide grep for `MarketingImportResult` across `*.cs` returns exactly these 4 files (the type definition + 3 consumers/test), matching the spec's enumerated list with no surprises.

The spec is fully accurate. Nothing in `development_guidelines.md` (Contracts/DTO rules, module-boundary rules) is implicated beyond confirming that `Contracts/` is the right home for a shared, cross-slice DTO. There is no module-boundary crossing (everything stays inside `MarketingInvoices`), no persistence/DI/API-surface impact, and no OpenAPI regeneration trigger (this type never crosses the MediatR/API boundary — `ImportMarketingInvoicesResponse` does).

## Proposed Architecture

### Component Overview

No new components. One existing type (`MarketingImportResult`) relocates within the same feature, from feature-root to `Contracts/`, matching its two siblings already there.

### Key Design Decisions

- **Target folder: `Contracts/`, not `Services/` or a new folder.** `MarketingImportResult` is consumed by both `Services/` (produces it) and `UseCases/` (consumes it) — exactly the "shared across use cases/services" case `Contracts/` is defined for. Putting it in `Services/` would couple the use-case layer to a services-internal folder; a new folder would fragment an already-established `Contracts/` convention for this feature.
- **Namespace becomes `...MarketingInvoices.Contracts`, matching physical path.** Consistent with `MarketingTransaction` and `IMarketingTransactionSource`, which already use this namespace.
- **No behavioral change, no interface change.** `IMarketingInvoiceImportService.ImportAsync`'s return type keeps the same simple name (`MarketingImportResult`); only its fully-qualified namespace changes, invisible to any call site that doesn't need a new `using`.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/MarketingInvoices/
├── Contracts/
│   ├── IMarketingTransactionSource.cs
│   ├── MarketingTransaction.cs
│   └── MarketingImportResult.cs        # moved here (git mv), namespace updated
├── Services/
│   ├── IMarketingInvoiceImportService.cs   # unchanged — already imports Contracts
│   └── MarketingInvoiceImportService.cs    # unchanged — already imports Contracts
├── UseCases/ImportMarketingInvoices/
│   └── ImportMarketingInvoicesHandler.cs   # unchanged — already imports Contracts
└── MarketingInvoicesModule.cs
```

Use `git mv` to preserve file history, per the brief/spec's suggested fix.

### Interfaces and Contracts

No interface signatures change. Only the namespace declaration inside the moved file changes:

```csharp
// before
namespace Anela.Heblo.Application.Features.MarketingInvoices;

// after
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
```

Class body (three `int` auto-properties: `Imported`, `Skipped`, `Failed`) stays byte-for-byte identical.

### Data Flow

Unchanged. `MarketingInvoiceImportService.ImportAsync` still builds and returns a `MarketingImportResult`; `ImportMarketingInvoicesHandler` still consumes it via `var` and maps its three counters onto `ImportMarketingInvoicesResponse`. No data ever crosses this type at the API boundary.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Leaving the now-unused `using Anela.Heblo.Application.Features.MarketingInvoices;` in `ImportMarketingInvoicesHandlerTests.cs` triggers an unused-using warning/analyzer failure | Low | Delete that line as part of the same change (FR-3); the file's other `using ...Contracts;` already resolves the relocated type |
| Incomplete grep before the move misses a consumer | Low | Already verified in this review: repo-wide search for `MarketingImportResult` across all `*.cs` returns exactly the 4 files the spec names — no others exist |
| `dotnet format`/build not run after the move | Low | Standard `dotnet build` + `dotnet format` validation per CLAUDE.md before declaring done |

## Specification Amendments

None. The spec's claims about file contents, existing `using` statements, and the full consumer list were independently re-verified against the repo and are accurate as written. Implementation can proceed exactly per spec.r1.md FR-1 through FR-5.

## Prerequisites

None. Self-contained, single-module, compile-time-only change.
