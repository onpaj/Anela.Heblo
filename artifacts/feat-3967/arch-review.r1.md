# Architecture Review: DataQuality-Catalog Module Boundary Decoupling for ProductPairingDqtComparer

## Skip Design: true

Pure backend refactor — no controller, MediatR contract, frontend, or persisted-schema change. No new or changed UI surface of any kind.

## Architectural Fit Assessment

The spec's design is not a new pattern — it is the third application of an already-established, already-proven pattern in this exact module pair. I verified all three precedents in the codebase:

- **`ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter`** — the canonical example documented in `docs/architecture/development_guidelines.md` ("Cross-Module Communication Example").
- **`IStockOperationQuery` / `IStockTakingQuery` / `IMaterialLotStockQuery`** — DataQuality-owned contracts with DataQuality-owned snapshot DTOs (`StockOperationSnapshot`, etc., in `Application/Features/DataQuality/Contracts/`), implemented by `DataQualityStockOperationQueryAdapter` et al. in `Application/Features/Catalog/Infrastructure/`, registered in `CatalogModule.AddCatalogModule`. This is the closest and best precedent — I read `IStockOperationQuery.cs`, `StockOperationSnapshot.cs`, and `DataQualityStockOperationQueryAdapter.cs` in full; the proposed `IDqtEshopStockSource`/`IDqtErpStockSource` pair is structurally identical (one contract, one fully-owned snapshot class, one adapter, `AddScoped` registration in the same file, same "DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations" comment already present at `CatalogModule.cs:61`).
- **`IInvoiceShoptetSource` / `IInvoiceErpClient`** (Invoices) — a *partial* precedent worth flagging: I read both interfaces and found they still return `Domain.Features.Invoices` types (`IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`) directly rather than DataQuality-owned snapshot DTOs. The allowlist comment for `DataQualityInvoicesAllowlist` explicitly calls this out as unfinished ("Follow-up: extract a DataQuality-owned snapshot DTO and map in the adapters"). The spec correctly does **not** follow this weaker precedent and instead follows the fully-decoupled `IStockOperationQuery` shape. This is the right call — it should be stated explicitly as a deliberate divergence, not silently.

I confirmed the violation itself: `ProductPairingDqtComparer.cs` (lines 1–4) imports `Anela.Heblo.Domain.Features.Catalog` and `...Catalog.Stock`, injects `IEshopStockClient`/`IErpStockClient`, and its private `IsSellable(ErpStock)` (lines 132–134) does the exact `ProductTypeId == (int)ProductType.Goods || ... Product` comparison the spec describes. `ModuleBoundariesTests.cs` lines 128–144 carry exactly the four allowlist entries and the exact follow-up comment the spec cites, verbatim. The spec's factual claims about the existing code are all accurate — this review found no discrepancy requiring a spec correction on architectural fit.

**Integration points:** none beyond the two touched modules. `IEshopStockClient` (`AddHttpClient`, transient-by-default) and `IErpStockClient` (`AddSingleton`, `FlexiStockClient`) are unchanged; `IDqtResilienceService` (already a cross-module contract, `DataQualityResilienceAdapter`) is reused as-is. `DriftDqtJobRunner` calls `IDriftDqtComparer.CompareAsync` polymorphically and is untouched.

## Proposed Architecture

### Component Overview

```
DataQuality module                         Catalog module (provider)
───────────────────                        ──────────────────────────
Contracts/
  IDqtEshopStockSource ◄──────────┐         Infrastructure/
  IDqtErpStockSource   ◄──────┐   │           DataQualityEshopStockSourceAdapter ──implements──┐
  DqtEshopStockItem            │   │           DataQualityErpStockSourceAdapter  ──implements──┤
  DqtErpStockItem              │   │                    │                                      │
                                │   │                    ▼                                      ▼
Services/                      │   └────────────────────┴── IDqtEshopStockSource ◄─┘  (adapter implements
  ProductPairingDqtComparer ───┴── injects IDqtEshopStockSource, IDqtErpStockSource     the DataQuality-owned
    (no Catalog `using`s)                                                               interface)
                                          Domain.Features.Catalog.Stock
                                            IEshopStockClient / EshopStock  (wrapped, not exposed)
                                            IErpStockClient  / ErpStock, ProductType (wrapped, not exposed)

CatalogModule.AddCatalogModule registers both adapter bindings (provider owns DI wiring — ADR pattern,
confirmed at CatalogModule.cs:61-66 for the sibling IStockOperationQuery/IStockTakingQuery/IMaterialLotStockQuery/IDqtResilienceService group).
```

This is a pure dependency-inversion move: DataQuality already has zero knowledge of *how* eshop/ERP stock is fetched for the other three contracts in this same file; this closes the last gap.

### Key Design Decisions

#### Decision 1: Two focused contracts vs. one combined `IProductPairingQuery`

**Options considered:**
- (a) Single `IProductPairingQuery` returning a combined DTO, as the allowlist's follow-up comment literally suggests.
- (b) Two independent contracts (`IDqtEshopStockSource`, `IDqtErpStockSource`), mirroring `IInvoiceShoptetSource`/`IInvoiceErpClient`.

**Chosen approach:** (b), as the spec proposes.

**Rationale:** Confirmed against the existing pattern set — every other cross-module read contract in this DataQuality/Catalog boundary (`IStockOperationQuery`, `IStockTakingQuery`, `IMaterialLotStockQuery`, `IInvoiceShoptetSource`, `IInvoiceErpClient`) is single-source, single-responsibility. A combined contract would be the odd one out, would force one adapter class to depend on two unrelated Catalog clients, and would make the two comparer call-sites (which already independently `try`/`catch`/log per source, per lines 34–65 of the current file) awkward to keep separately resilient. The allowlist comment's suggested name is non-binding — it is a TODO note, not an accepted interface contract. Two contracts is the right call and should not be revisited.

#### Decision 2: Where the `ProductType`/sellability mapping lives

**Options considered:**
- (a) Keep a `ProductTypeId: int` (or similar) raw field on `DqtErpStockItem` and leave the `IsSellable` comparison in `ProductPairingDqtComparer`.
- (b) Collapse it to a `bool IsSellable` on `DqtErpStockItem`, computed in the Catalog-side adapter.

**Chosen approach:** (b), as the spec proposes.

**Rationale:** `ProductType` is a Catalog-owned enum (`Domain/Features/Catalog/ProductType.cs`, confirmed: `Goods = 1`, `Product = 8`, plus `Material`, `SemiProduct`, `Set`, `UNDEFINED`). Option (a) would still leak Catalog domain semantics (which numeric values mean "sellable") across the boundary — DataQuality would need to know Catalog's enum values without being allowed to reference the enum, which is worse than leaking the enum reference itself. Option (b) is the correct move: it also matches the general shape of `IStockOperationQuery`'s `StockOperationSnapshot`, which already asks its adapter to map a Catalog-side `StockUpOperationState` enum into a DataQuality-owned `StockOperationStateSnapshot` enum (see `MapState` in `DataQualityStockOperationQueryAdapter.cs`) rather than exposing the raw Catalog enum. `IsSellable` as a plain `bool` is the DataQuality-relevant abstraction; the enum comparison is Catalog-internal.

## Implementation Guidance

### Directory / Module Structure

No new directories — both target folders already exist and hold sibling files of the same kind:

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/` → add `IDqtEshopStockSource.cs`, `IDqtErpStockSource.cs`, `DqtEshopStockItem.cs`, `DqtErpStockItem.cs`. One type per file, matching every existing file in that folder (`IStockOperationQuery.cs`, `StockOperationSnapshot.cs`, etc. — confirmed one-type-per-file convention).
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/` → add `DataQualityEshopStockSourceAdapter.cs`, `DataQualityErpStockSourceAdapter.cs`, `internal sealed class`, matching `DataQualityStockOperationQueryAdapter` exactly (confirmed: that adapter is `internal sealed class ... : IStockOperationQuery`).
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` → add the two `AddScoped` lines immediately after line 64 (`IMaterialLotStockQuery`), inside the existing `// DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.` comment block (line 61) — do not create a new comment block, just extend the existing group of three registrations to five.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs` → edited in place per FR-4.

### Interfaces and Contracts

Confirmed exact target shape for the two source clients (so adapter mapping is unambiguous):

```csharp
// Domain.Features.Catalog.Stock (unchanged, wrapped only)
public interface IEshopStockClient { Task<List<EshopStock>> ListAsync(CancellationToken ct); ... }
public interface IErpStockClient   { Task<IReadOnlyList<ErpStock>> ListAsync(CancellationToken ct); ... }

// EshopStock: Code, PairCode, Name, Stock, NameSuffix, Location, DefaultImage, Image, Weight, ... (many more fields — comparer uses only Code/PairCode/Name)
// ErpStock:   ProductCode, ProductName, Stock, MOQ, ProductTypeId (int?), ProductId, HasExpiration, HasLots, Volume (comparer uses only ProductCode/ProductName/ProductTypeId)
```

Both source types carry many fields the comparer never touches — the spec's "exactly the fields consumed today" scoping for `DqtEshopStockItem`/`DqtErpStockItem` is correct and should be enforced strictly (no speculative fields), per the documented "no speculative methods" rule.

One correctness note for implementation: `IErpStockClient.ListAsync` returns `IReadOnlyList<ErpStock>`, but `IEshopStockClient.ListAsync` returns `List<EshopStock>` (not `IReadOnlyList`) — asymmetric today. The new `IDqtEshopStockSource`/`IDqtErpStockSource` contracts should both return `IReadOnlyList<...>` uniformly (as the spec's FR-1 already specifies), since DataQuality has no reason to inherit that asymmetry — this is a case where the adapter boundary is the right place to normalize an inconsistency in the underlying Catalog API, not propagate it.

### Data Flow

Confirmed unchanged shape — `DriftDqtJobRunner` → `ProductPairingDqtComparer.CompareAsync` → (per source) `IDqtResilienceService.ExecuteWithResilienceAsync` wrapping `IDqtXxxStockSource.ListAsync` → adapter → real Catalog client. The two `try`/`catch`/`LogWarning` blocks around each resilience call (lines 34–48, 50–65 of the current file) are preserved verbatim per FR-4 — only the types crossing the boundary change, not the control flow, not the operation-name strings passed to the resilience service (these appear in telemetry/logs and must not drift).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Mock-type churn in `ProductPairingDqtComparerTests.cs` silently changes test semantics (e.g. `ProductTypeId = 1` → `IsSellable = true` loses the "why 1 means sellable" documentation value inline in tests) | Low | Keep a `// Goods` / `// sellable` comment on the `IsSellable = true` literal in each rewritten test case, as the current tests already do (`ProductTypeId = 1`, // `Goods=1`, confirmed present at test line 54) |
| Adapter's `IsSellable` mapping (`ProductTypeId == Goods \|\| ProductTypeId == Product`) is duplicated logic if any other DataQuality comparer later needs the same sellability rule | Low | Out of scope per spec; if a second consumer appears, extract to a Catalog-internal helper reused by both adapters — not a DataQuality-visible helper. Not needed now (only one caller exists). |
| `IEshopStockClient.ListAsync` returns `List<T>` (mutable, non-readonly) while `IErpStockClient.ListAsync` returns `IReadOnlyList<T>` — an implementer could be tempted to keep this asymmetry in the new contracts for "consistency with the source" | Low | Both new contracts return `IReadOnlyList<T>` per spec FR-1; call this out explicitly during code review since it is an easy copy-paste-driven regression | 
| Removing the `DataQualityCatalogAllowlist` entries entirely could look like a no-op diff to a reviewer skimming `ModuleBoundariesTests.cs` and get missed | Low | FR-5 already specifies updating the comment to the "Empty — ..." style used elsewhere in the same file (`LeafletAllowlist`, `ArticleAllowlist`) — keep that comment style so the boundary test file self-documents the resolved violation the same way prior resolutions did |

No risk in this change rises above Low: it is a compile-time-checked, behavior-preserving refactor with full test coverage already existing and only needing rebinding, in a codebase with three prior successful applications of the identical pattern.

## Specification Amendments

The spec (spec.r1.md) is architecturally sound and requires no corrections to its factual claims — everything checked against the codebase (allowlist entries, comparer internals, sibling contract/adapter shapes, DI lifetimes, `IEshopStockClient`/`IErpStockClient` registrations) matched exactly. Two small additions, not corrections:

1. **FR-1**: Add explicitly that both `IDqtEshopStockSource.ListAsync` and `IDqtErpStockSource.ListAsync` must return `IReadOnlyList<T>` (not mirror `IEshopStockClient`'s `List<T>` return type) — see "Interfaces and Contracts" above. The spec's own code sample already does this correctly; this amendment just makes the rule explicit so an implementer doesn't "fix" it to match the source client's signature.
2. **FR-3 acceptance criteria**: Add "the two new registrations are added to the existing `// DataQuality owns the query contracts...` comment block at `CatalogModule.cs:61`, not a new comment block" — this is a two-line addition to an already-open group of five sibling contracts (three today, two more from this change), not a new architectural grouping.

Everything else in the spec — the exact file names, the `internal sealed class` adapter modifier, the DI lifetime justification (Scoped depending on Transient/Singleton is not a captive-dependency violation), the allowlist removal, and the test rebinding — should be implemented as written.

## Prerequisites

None. No migrations, no config, no infrastructure changes, no new NuGet packages. All dependencies (`IEshopStockClient`, `IErpStockClient`, `IDqtResilienceService`, their DI registrations) already exist and are unchanged by this work. Implementation can start immediately.
