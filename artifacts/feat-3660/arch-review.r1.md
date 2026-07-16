# Architecture Review: Relocate `IMarketingTransactionSource` / `MarketingTransaction` from Domain to Application/Contracts

## Skip Design: true

This is a pure backend namespace/project-placement refactor with no HTTP surface, MediatR contract, or UI impact. No design work applies.

## Architectural Fit Assessment

The finding is correct and the fix is the standard, already-precedented pattern in this codebase. `IMarketingTransactionSource` is an outbound port for external ad-platform adapters (`MetaAdsTransactionSource`, `GoogleAdsTransactionSource`), and `MarketingTransaction` is its transient DTO (carries `RawData`, raw third-party JSON — never persisted, no invariants). Both currently sit in `Anela.Heblo.Domain.Features.MarketingInvoices`, alongside the two types that *do* belong there: `ImportedMarketingTransaction` (EF entity) and `IImportedMarketingTransactionRepository` (repository contract).

This is exactly the shape `docs/architecture/development_guidelines.md` calls "Contracts" and `docs/architecture/filesystem.md` places at `Application/Features/{Feature}/Contracts/`. The codebase already has a direct precedent for an outbound-port interface living there: `Anela.Heblo.Application.Features.Catalog.Contracts.ICatalogTransportSource` — "Catalog-owned read abstraction over Logistics transport-box state. Implemented by the Logistics module via an adapter." `IMarketingTransactionSource` is structurally identical: an Application-owned port implemented by adapter projects.

Confirmed from source:
- `Anela.Heblo.Adapters.MetaAds.csproj` and `Anela.Heblo.Adapters.GoogleAds.csproj` **already** carry `<ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />` and **no** reference to `Anela.Heblo.Domain`. Today they only compile because project references are transitive (Application → Domain), so the adapters can see `Anela.Heblo.Domain.Features.MarketingInvoices` through Application without declaring it directly. After the move, the adapters resolve the types directly from Application — no `.csproj` change is needed, confirming FR-4's expectation.
- `MarketingInvoicesModule.cs` (`Application/Features/MarketingInvoices/MarketingInvoicesModule.cs`) only touches `IImportedMarketingTransactionRepository` / `IMarketingInvoiceImportService` — it does not reference `IMarketingTransactionSource` or `MarketingTransaction` and needs no change. (The two adapter DI extension methods — `MetaAdsAdapterServiceCollectionExtensions.cs`, `GoogleAdsAdapterServiceCollectionExtensions.cs` — register `IMarketingTransactionSource` themselves and are in the FR-3 file list.)
- The `using Anela.Heblo.Domain.Features.MarketingInvoices;` in `ImportMarketingInvoicesHandler.cs`, `MarketingInvoiceImportService.cs`, and `IMarketingInvoiceImportService.cs` is used *only* for the two moved types in the first two files, and *only* for `IImportedMarketingTransactionRepository` in `MarketingInvoiceImportService.cs` (which also needs `IMarketingTransactionSource` as a method parameter type) — so that file keeps a Domain using but changes its meaning (only `IImportedMarketingTransactionRepository` remains from Domain), while gaining a new Application.Contracts using.

No open design questions, no new dependencies, no data model change. This is a mechanical two-file move plus reference fix-ups.

## Proposed Architecture

### Component Overview

```
Before:
  Anela.Heblo.Adapters.MetaAds  ──┐
  Anela.Heblo.Adapters.GoogleAds ─┼──► Anela.Heblo.Domain.Features.MarketingInvoices
                                  │      • IMarketingTransactionSource   (adapter port — wrong layer)
                                  │      • MarketingTransaction          (DTO — wrong layer)
                                  │      • ImportedMarketingTransaction  (entity — correct)
                                  │      • IImportedMarketingTransactionRepository (correct)
  Application (Handler, Service) ─┘

After:
  Anela.Heblo.Adapters.MetaAds  ──┐
  Anela.Heblo.Adapters.GoogleAds ─┼──► Anela.Heblo.Application.Features.MarketingInvoices.Contracts
                                  │      • IMarketingTransactionSource   (moved here)
                                  │      • MarketingTransaction          (moved here)
  Application (Handler, Service) ─┘
                                       Anela.Heblo.Domain.Features.MarketingInvoices
                                         • ImportedMarketingTransaction  (unchanged)
                                         • IImportedMarketingTransactionRepository (unchanged)
```

Dependency direction after the change: `Adapters.MetaAds/GoogleAds → Application → Domain`, and separately `Application → Domain` for the entity/repository contract. Domain no longer has any awareness of ad-platform integration semantics or raw external payloads. This mirrors the `ICatalogTransportSource` precedent exactly.

### Key Design Decisions

#### Decision 1: Target namespace and folder for the two moved types

**Options considered:**
- (a) `Application/Features/MarketingInvoices/Contracts/` — flat contracts folder, matching `ICatalogTransportSource`'s placement in `Catalog/Contracts/`.
- (b) `Application/Features/MarketingInvoices/Services/` alongside `IMarketingInvoiceImportService` — keeps port near its only consumer.
- (c) A new `Adapters/Shared` project — avoids touching Application at all.

**Chosen approach:** (a), exactly as the spec and issue specify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs` and `.../Contracts/MarketingTransaction.cs`, namespace `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`.

**Rationale:** `Contracts/` is the documented, precedented location for "Shared DTOs across use cases" and outbound port interfaces (`filesystem.md` §Application Layer; `ICatalogTransportSource`, `IArticleKnowledgeSource`, `ICatalogPurchaseSource` all live in feature `Contracts/` folders). Option (b) would bury an adapter-facing port inside a folder documented for concrete services. Option (c) invents a new project for a problem the existing layering already solves — Domain was only ever a workaround for the adapters' visibility, and that workaround is exactly what this refactor removes.

#### Decision 2: Namespace suffix — add `.Contracts` or keep `.MarketingInvoices`

**Options considered:**
- (a) `Anela.Heblo.Application.Features.MarketingInvoices.Contracts` (namespace matches folder path, C# convention in this repo).
- (b) `Anela.Heblo.Application.Features.MarketingInvoices` (flat, matching `MarketingImportResult.cs`'s existing namespace at the feature root, avoiding one extra `using` in consumers).

**Chosen approach:** (a) — matches folder-to-namespace convention used everywhere else in `Application/Features/*/Contracts/` (e.g. `Anela.Heblo.Application.Features.Catalog.Contracts`). This is also what the spec's acceptance criteria mandate verbatim.

**Rationale:** Consistency with the rest of the codebase's `Contracts/` folders outweighs saving one `using` line. Every consumer file already needs a `using` edit regardless (they're currently pointed at `Domain.Features.MarketingInvoices`), so there's no marginal cost to using the fully-qualified `.Contracts` namespace.

## Implementation Guidance

### Directory / Module Structure

Create:
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs`

Delete:
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IMarketingTransactionSource.cs`
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`

Do not touch (stay in Domain, unchanged):
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs`
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`

No new `.csproj` edits are expected (verified above — both adapter projects already reference `Anela.Heblo.Application` and not `Anela.Heblo.Domain`).

### Interfaces and Contracts

`IMarketingTransactionSource` — member signatures unchanged, only namespace changes:
```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public interface IMarketingTransactionSource
{
    string Platform { get; }
    Task<List<MarketingTransaction>> GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct);
}
```

`MarketingTransaction` — all six properties unchanged, remains a plain class (not a record, per this repo's DTO convention):
```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

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

### Files requiring `using`/namespace edits (verified against current source)

| File | Change |
|---|---|
| `Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs` | Replace `using Anela.Heblo.Domain.Features.MarketingInvoices;` → `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` (sole reason for the using) |
| `Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` | Same `using` swap. Keep `using Anela.Heblo.Domain.Features.BackgroundJobs;` (unrelated, for `IRecurringJob`) |
| `Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` | Same `using` swap |
| `Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` | Same `using` swap; keep `BackgroundJobs` using |
| `Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs` | Replace the Domain using with the Contracts using (sole reason for the using — file only references `IMarketingTransactionSource`) |
| `Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs` | Replace the Domain using with the Contracts using (sole reference) |
| `Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` | **Add** `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` (for `IMarketingTransactionSource` parameter) while **keeping** `using Anela.Heblo.Domain.Features.MarketingInvoices;` (still needed for `IImportedMarketingTransactionRepository` and `ImportedMarketingTransaction`) |
| `test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` | Add the Contracts using; the existing `using Anela.Heblo.Domain.Features.MarketingInvoices;` becomes dead (file only uses `IMarketingTransactionSource` from that namespace) and should be removed |
| `test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | **Add** the Contracts using (for `IMarketingTransactionSource`); **keep** the Domain using (still needed for `IImportedMarketingTransactionRepository`) |

`MarketingInvoicesModule.cs` requires **no change** — verified it only references `IImportedMarketingTransactionRepository` / `IMarketingInvoiceImportService`, neither of which moves.

A repo-wide `git grep` for `IMarketingTransactionSource` and `MarketingTransaction\b` (excluding `ImportedMarketingTransaction`) should be re-run at implementation time to catch anything missed — the search performed for this review found exactly the file set enumerated in the spec plus `MarketingInvoicesModule.cs` (which needs no edit) and the EF migration snapshot/designer files (which reference `ImportedMarketingTransaction` only, out of scope).

### Data Flow

Unchanged. `ImportMarketingInvoicesHandler` selects the matching `IMarketingTransactionSource` by `Platform`, delegates to `MarketingInvoiceImportService.ImportAsync`, which calls `source.GetTransactionsAsync(...)`, maps each `MarketingTransaction` to an `ImportedMarketingTransaction` entity, and persists via `IImportedMarketingTransactionRepository`. Only the compile-time namespace of two types in that flow changes; no method signature, call order, or runtime behavior changes.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missing a reference not enumerated in the spec's file list, causing a build break | Low | Re-run `git grep -n "IMarketingTransactionSource\|MarketingTransaction\b"` (excluding `ImportedMarketingTransaction`) after the move, before considering the task done; `dotnet build` will also surface any miss immediately |
| Accidentally removing the `Anela.Heblo.Domain.Features.MarketingInvoices` using from a file where it's still needed (e.g. `MarketingInvoiceImportService.cs`, `MarketingInvoiceImportServiceTests.cs` still need `IImportedMarketingTransactionRepository`) | Low | Per-file check noted in the table above; `dotnet build` fails loudly (CS0246) if a using is wrongly dropped |
| Test project or adapter `.csproj` turns out to need a reference change after all (contradicting FR-4's expectation) | Very low | Verified in this review: both adapter `.csproj` files already reference only `Anela.Heblo.Application`, not `Anela.Heblo.Domain`; no change expected. Confirm with `dotnet build` regardless |

No risk warrants a mitigation beyond "build and run the existing test suite" — this is as low-risk as a C# refactor gets.

## Specification Amendments

None required. The spec (`spec.r1.md`) is accurate and complete against the actual source: FR-1 through FR-5's acceptance criteria match what was found in the code. One clarification worth calling out for the implementer (not a spec change, just explicit confirmation): `MarketingInvoiceImportService.cs` and its test `MarketingInvoiceImportServiceTests.cs` must **retain** their `using Anela.Heblo.Domain.Features.MarketingInvoices;` (for `IImportedMarketingTransactionRepository`/`ImportedMarketingTransaction`) while **adding** the new Contracts using — these are the two files in the touched set that end up with *both* usings, not a simple swap. `MarketingInvoicesModule.cs` was checked and requires no change at all (not in the spec's list, and this review confirms that's correct — it doesn't reference either moved type).

## Prerequisites

None. No migrations, no config, no infrastructure changes — this is a self-contained code move gated only on `dotnet build` and the existing unit test suite (`ImportMarketingInvoicesHandlerTests`, `MarketingInvoiceImportServiceTests`) passing unmodified in behavior.
