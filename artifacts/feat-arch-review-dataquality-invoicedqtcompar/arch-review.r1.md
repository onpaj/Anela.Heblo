I have enough context. Writing the architecture review now.

# Architecture Review: Decouple `InvoiceDqtComparer` from Invoices Module Interfaces

## Skip Design: true

Backend-only refactor — no UI, no API changes, no visual components. Pure module-boundary cleanup.

## Architectural Fit Assessment

This refactor aligns **perfectly** with the codebase's documented Cross-Module Communication pattern (`docs/architecture/development_guidelines.md` § lines 195–208). The project already enforces this pattern in three concrete ways:

1. **Documented precedent**: `ILeafletKnowledgeSource` (Leaflet-owned contract) + `KnowledgeBaseLeafletSourceAdapter` (provider adapter in `KnowledgeBase/Infrastructure/`).
2. **Existing twin inside the Invoices module itself**: `IInvoiceConsumptionSource` (PackingMaterials-owned) + `InvoiceConsumptionSourceAdapter` (Invoices/Infrastructure) — registered in `InvoicesModule.cs` line 24. The new adapters slot in next to this one with no structural novelty.
3. **Enforced by `ModuleBoundariesTests`**: a reflection-based architecture test catalogues forbidden cross-module references and supports per-pair allowlists. A `DataQuality → Invoices` rule must be added here (see Specification Amendments).

Two integration realities the spec under-specifies and that must shape implementation:

- **`IIssuedInvoiceSource` is registered in `Program.cs:119` as `Singleton`** (not in `InvoicesModule.cs`, not Scoped).
- **`IIssuedInvoiceClient` is registered in `FlexiAdapterServiceCollectionExtensions.cs:93` as `Scoped`**.

So the spec's "InvoicesModule.cs (or equivalent)" and "Assumed Scoped" assumptions are both wrong. See Decision 2 and Risks.

## Proposed Architecture

### Component Overview

```
DataQuality (consumer)                       Invoices (provider)
─────────────────────                        ──────────────────
Contracts/                                   Infrastructure/
  IInvoiceShoptetSource ◄────implements──────  InvoiceShoptetSourceAdapter
  IInvoiceErpClient     ◄────implements──────  InvoiceErpClientAdapter
                                                       │
Services/                                              │ delegates to
  InvoiceDqtComparer                                   ▼
    ├─ depends on IInvoiceShoptetSource    Anela.Heblo.Domain.Features.Invoices
    └─ depends on IInvoiceErpClient          IIssuedInvoiceSource  ─► ShoptetApiInvoiceSource (Singleton)
                                             IIssuedInvoiceClient  ─► FlexiIssuedInvoiceClient  (Scoped)

Shared (read-only) domain DTOs:
  Anela.Heblo.Domain.Features.Invoices.{IssuedInvoiceDetail, IssuedInvoiceDetailBatch,
                                        IssuedInvoiceSourceQuery, IssuedInvoiceDetailItem,
                                        InvoicePrice}
  → Referenced by DataQuality contracts as shared model types. Documented as a
    pragmatic leak in ModuleBoundariesTests' DataQualityInvoicesAllowlist.

DI binding lives in: Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs
  (alongside the existing IInvoiceConsumptionSource binding)
```

### Key Design Decisions

#### Decision 1: Contract location — `Application/Features/DataQuality/Contracts/`
**Options considered:**
- A. `Application/Features/DataQuality/Contracts/`
- B. `Domain/Features/DataQuality/Contracts/`

**Chosen approach:** A.
**Rationale:** Matches the precedent set by `ILeafletKnowledgeSource` (Application layer), `IInvoiceConsumptionSource` (Application layer), and `ICatalogManufactureSource` (Application layer). The existing `Application/Features/DataQuality/Contracts/` folder already holds DqtRunDto/DqtDriftResultDto/InvoiceDqtResultDto, so this is the natural home. Domain layer is reserved for entities and pure behavior — cross-module read contracts that expose Application-layer DTOs do not belong there. This also resolves Open Question 1.

#### Decision 2: Adapter lifetimes — **mirror the wrapped service exactly**
**Options considered:**
- A. Register both adapters as Scoped (spec assumption).
- B. Register each adapter at the lifetime of the service it wraps.

**Chosen approach:** B.
- `InvoiceShoptetSourceAdapter` → **Singleton** (wraps Singleton `IIssuedInvoiceSource`).
- `InvoiceErpClientAdapter` → **Scoped** (wraps Scoped `IIssuedInvoiceClient`).

**Rationale:** The DI container will inject a Singleton adapter that holds a captive reference to a Scoped service if you register the adapter as Singleton against a Scoped wrapped service — that's a known captive-dependency bug. The reverse (Scoped adapter wrapping Singleton) is legal but creates a per-request allocation for an effectively stateless wrapper. The simple rule "mirror the lifetime" sidesteps both. Since `InvoiceDqtComparer` itself is registered as Scoped (`DataQualityModule.cs:15`), Scoped resolves a Singleton just fine.

This is a non-trivial correction to the spec — the spec's Open Question 2 must be answered with the lifetimes above before implementation begins.

#### Decision 3: DI binding home — `InvoicesModule.cs`
**Options considered:**
- A. Add bindings to `InvoicesModule.cs` (consistent with PackingMaterials adapter pattern).
- B. Add bindings to `Program.cs` (where `IIssuedInvoiceSource` is currently bound).

**Chosen approach:** A.
**Rationale:** The cross-module adapter pattern docs explicitly say "Provider (B) registers the DI binding" in its module class. `InvoicesModule.cs` already hosts `IInvoiceConsumptionSource → InvoiceConsumptionSourceAdapter`. Putting the new bindings here keeps the pattern uniform and lifts visibility for future readers. The existing Program.cs binding of `IIssuedInvoiceSource` is a separate concern and remains untouched.

#### Decision 4: Domain DTO reuse vs. dedicated contracts
**Options considered:**
- A. DataQuality contracts reference existing `Anela.Heblo.Domain.Features.Invoices` DTOs directly.
- B. DataQuality declares its own `DqtInvoiceSnapshot` / `DqtInvoiceItem` DTOs; adapters map.

**Chosen approach:** A.
**Rationale:** Matches `Catalog → Manufacture` precedent (`ManufactureHistoryRecord` leak — see `ModuleBoundariesTests.cs:137–146`) where shared *data* types are allowed across module boundaries, but *behavior* contracts are not. The spec's actual ISP violation is about unused `CommitAsync`/`FailAsync`/`SaveAsync`/`GetAsync` methods — that is fully resolved by narrowing the interface. Introducing a parallel DTO hierarchy would triple the diff with no proportional architecture benefit. Resolves Open Question 3.

#### Decision 5: Naming — `IInvoiceShoptetSource` and `IInvoiceErpClient`
**Options considered:** `IInvoiceDataSource` vs `IInvoiceShoptetSource`; `IInvoiceErpClient` vs `IInvoiceFlexiClient`.
**Chosen approach:** `IInvoiceShoptetSource` and `IInvoiceErpClient` (matches spec).
**Rationale:** "Shoptet" names the source-of-truth system explicitly, matching how the field is named in `InvoiceDqtComparer` (`_shoptetSource`). "Erp" is one level of indirection above "Flexi" — appropriate because Abra Flexi is the ERP and we may later swap ERPs. Resolves Open Question 4.

## Implementation Guidance

### Directory / Module Structure

**New files (DataQuality side):**
```
backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/
├── IInvoiceShoptetSource.cs   (new)
└── IInvoiceErpClient.cs       (new)
```

**New files (Invoices side):**
```
backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/
├── InvoiceShoptetSourceAdapter.cs   (new, internal sealed)
└── InvoiceErpClientAdapter.cs       (new, internal sealed)
```

**Modified files:**
```
backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs   (add 2 DI bindings)
backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs
  (replace ctor parameter types + `using Anela.Heblo.Domain.Features.Invoices;` stays for DTOs)
backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs
  (Mock<IIssuedInvoiceSource/Client> → Mock<IInvoiceShoptetSource/ErpClient>; behavior preserved)
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
  (add "DataQuality -> Invoices" rule + allowlist for shared domain DTOs)
```

### Interfaces and Contracts

The two contracts are exactly as the spec defines them. Mark them `public` so the Invoices module's `internal` adapters can satisfy them. Both adapters must be `internal sealed` and live in the Invoices namespace — matches the existing `InvoiceConsumptionSourceAdapter` and `KnowledgeBaseLeafletSourceAdapter` style.

`InvoiceDqtComparer.cs` import policy after the change:
- `using Anela.Heblo.Application.Features.DataQuality.Contracts;` — **add** for the two new interfaces.
- `using Anela.Heblo.Domain.Features.Invoices;` — **keep** (still needed for `IssuedInvoiceDetail`, `IssuedInvoiceDetailItem`, `InvoicePrice`, `IssuedInvoiceSourceQuery` used in the body).

### Data Flow

Single comparison invocation, unchanged semantically:

```
InvoiceDqtComparer.CompareAsync(from, to, ct)
  │
  ├──► IInvoiceShoptetSource.GetAllAsync(query, ct)
  │       └──► InvoiceShoptetSourceAdapter.GetAllAsync
  │              └──► IIssuedInvoiceSource.GetAllAsync (Singleton)
  │                     └──► ShoptetApiInvoiceSource
  │
  └──► IInvoiceErpClient.GetAllAsync(from, to, ct)
          └──► InvoiceErpClientAdapter.GetAllAsync
                 └──► IIssuedInvoiceClient.GetAllAsync (Scoped)
                        └──► FlexiIssuedInvoiceClient
```

Number of round-trips, exception flow, cancellation behavior — all unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Wrong adapter lifetime → captive Singleton holding Scoped service | High | Mirror wrapped lifetime exactly (Decision 2). Verify at PR time by reading both existing registrations. |
| Implementer adds DI binding in `Program.cs` because that's where `IIssuedInvoiceSource` lives today | Medium | Specification Amendment 1 below pins binding location to `InvoicesModule.cs`. |
| `DataQuality → Invoices` architecture test rule not added → regression hides | Medium | Specification Amendment 2 — add rule + allowlist as part of this work item. |
| Allowlist entries become permanent without follow-up to extract DataQuality-owned DTOs | Low | Add a TODO comment on the allowlist block referencing a follow-up issue. Same pattern as the `Catalog → Manufacture` block. |
| Adapter classes hidden behind `internal` make future test mocking awkward | Low | Tests mock the **contract**, not the adapter — `internal sealed` is correct and matches existing precedent. |
| Existing `InvoiceDqtComparerTests` rely on `IIssuedInvoiceSource`/`IIssuedInvoiceClient` mocks | Low | Mechanical rename in the test file; FR-5 covers it. Validate by running the test class after the rename — same assertions, same setups. |

## Specification Amendments

1. **Pin DI binding location.** Add the two new `services.AddScoped/AddSingleton` lines to `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`, immediately below the existing `IInvoiceConsumptionSource` registration. Do **not** add them to `Program.cs`, even though that's where the underlying `IIssuedInvoiceSource` lives.

2. **Pin adapter lifetimes** (overrides spec § Open Question 2):
   - `services.AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>();`
   - `services.AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>();`

3. **Add a new boundary rule to `ModuleBoundariesTests.cs`.** A new rule entry of the form below must be added as part of this work item. Without it, the spec's NFR-3 ("DataQuality must compile without referencing Invoices-module behavior interfaces") has no CI enforcement.

   ```csharp
   new ModuleBoundaryRule(
       Name: "DataQuality -> Invoices",
       InspectedNamespacePrefix: "Anela.Heblo.Application.Features.DataQuality",
       ForbiddenNamespacePrefixes: new[]
       {
           "Anela.Heblo.Domain.Features.Invoices",
           "Anela.Heblo.Application.Features.Invoices",
           "Anela.Heblo.Persistence.Invoices",
       },
       Allowlist: DataQualityInvoicesAllowlist),
   ```

   With `DataQualityInvoicesAllowlist` populated with entries for each DataQuality type that legitimately references the shared domain DTOs (`IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`, `IssuedInvoiceDetailItem`, `InvoicePrice`). Each entry needs a comment per the file's convention. The expected set is:
   - `IInvoiceShoptetSource → IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`
   - `IInvoiceErpClient → IssuedInvoiceDetail`
   - `InvoiceDqtComparer → IssuedInvoiceDetail`, `IssuedInvoiceDetailItem`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`, `InvoicePrice`

   The exact list will be revealed by running the test and copying violations; follow the established pattern in the file.

4. **Update test mock types.** Spec FR-5 should explicitly call out `Mock<IIssuedInvoiceSource>` / `Mock<IIssuedInvoiceClient>` rename to `Mock<IInvoiceShoptetSource>` / `Mock<IInvoiceErpClient>` in `InvoiceDqtComparerTests.cs:10-11,17-19,25,31`. The test file already exists (counter to spec's FR-5 hedge "If no unit tests currently exist").

5. **Status update.** Open Questions 1–4 are answered above. Open Question 5 (mismatched answer revisions) is moot — those revisions can be ignored. The spec status can move from `HAS_QUESTIONS` to `READY` once these amendments are folded in.

## Prerequisites

None. All required infrastructure exists:
- DataQuality `Contracts/` folder exists and already holds files.
- Invoices `Infrastructure/` folder exists and already hosts one adapter (`InvoiceConsumptionSourceAdapter`).
- `InvoicesModule.cs` already registers a cross-module adapter (line 24) — same registration pattern applies.
- `ModuleBoundariesTests.cs` has 12 existing rules following the same shape; adding a 13th is purely additive.
- No migrations, config, secrets, or feature flags involved.

The work can begin immediately once the Specification Amendments are accepted.