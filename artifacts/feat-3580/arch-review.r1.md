# Architecture Review: Deduplicate PurchaseOrderHistory → PurchaseOrderHistoryDto Mapping

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal refactor confined to a single vertical slice (`Purchase`) and does not cross module boundaries, change any contract shape, or touch persistence. It fits the existing codebase conventions exactly:

- `PurchaseOrderHistoryDto.cs` (`backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderHistoryDto.cs`) is currently a plain class with only auto-properties — no mapping logic at all.
- The sibling DTO in the same folder, `PurchaseOrderLineDto.cs`, already implements exactly the pattern this spec proposes: a `public static PurchaseOrderLineDto FromLine(PurchaseOrderLine line, string? catalogNote = null)` single-expression factory using an object initializer.
- The same "static `FromX` factory on the DTO" pattern also exists outside Purchase (`Features/Marketing/Contracts/MarketingActionDto.cs`), confirming this is an established, repeated convention in the codebase rather than a one-off — so introducing `FromDomain` on `PurchaseOrderHistoryDto` raises the module's internal consistency rather than inventing something new.
- Per `docs/architecture/development_guidelines.md` ("📬 Contracts and DTOs Rules"), DTOs live in each module's own `Contracts/` folder and are not shared globally — the proposed change keeps the factory local to `PurchaseOrderHistoryDto` in `Features/Purchase/Contracts/`, so it does not violate module ownership.
- Per project-wide rule (CLAUDE.md / dev guidelines): DTOs must remain plain C# classes, never `record`s, because of OpenAPI client generation quirks. `PurchaseOrderHistoryDto` is already a class and stays a class — the factory is an ordinary static method, not a change of type kind. No risk here, just confirming the constraint isn't violated.

No new component, dependency, or interface is introduced at the module level. This is a leaf-level code-quality fix.

## Proposed Architecture

### Component Overview
No structural/component change. Within the existing `Purchase` module:

```
Features/Purchase/
├── Contracts/
│   ├── PurchaseOrderHistoryDto.cs   <- gains FromDomain(PurchaseOrderHistory) static factory
│   └── PurchaseOrderLineDto.cs      <- existing FromLine(...) factory (reference pattern)
└── UseCases/
    ├── CreatePurchaseOrder/CreatePurchaseOrderHandler.cs        <- call site, updated
    ├── GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs      <- call site, updated
    └── GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs <- call site, updated
```

Data flow direction is unchanged: `PurchaseOrderHistory` (domain, `Domain/Features/Purchase/PurchaseOrderHistory.cs`) → `PurchaseOrderHistoryDto` (contract). The only change is *where* the mapping expression is defined (once, on the DTO) versus *where it's invoked* (three call sites, now via method-group reference).

### Key Design Decisions

#### Decision 1: Static factory on the DTO vs. alternatives
**Options considered:**
1. Static factory method `FromDomain` on `PurchaseOrderHistoryDto` (mirrors `PurchaseOrderLineDto.FromLine`).
2. A dedicated mapper/extension class (e.g. `PurchaseOrderHistoryMapper.ToDto(...)`).
3. AutoMapper profile for `PurchaseOrderHistory → PurchaseOrderHistoryDto`.

**Chosen approach:** Option 1, exactly as specified.

**Rationale:** The module already has a working precedent (`FromLine`) one file away; introducing a second mapping mechanism (extension class or AutoMapper) for the same module would create inconsistency rather than resolve it. AutoMapper is listed in the dev guidelines as "optional, for complex mappings" — this is a flat six-field 1:1 copy, the simplest case, so a hand-written factory is proportionate. Matching the established local convention is the deciding factor, not a general preference for one mapping style.

#### Decision 2: Method-group syntax at call sites
**Options considered:** `.Select(h => PurchaseOrderHistoryDto.FromDomain(h))` (lambda wrapping) vs. `.Select(PurchaseOrderHistoryDto.FromDomain)` (method-group reference).

**Chosen approach:** Method-group reference, as specified in brief/spec.

**Rationale:** `PurchaseOrderLineDto.FromLine` is called via lambda at its two call sites only because it takes a second parameter (`catalogNote`) that varies per site — that's not the case here. Since `FromDomain` takes exactly one argument matching `Select`'s delegate signature, the method-group form is shorter and equally clear. No functional difference; purely a style choice consistent with idiomatic C#.

## Implementation Guidance

### Directory / Module Structure
No new files or folders. Modify in place:
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderHistoryDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs`

### Interfaces and Contracts
```csharp
// PurchaseOrderHistoryDto.cs
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public class PurchaseOrderHistoryDto
{
    public int Id { get; set; }
    public string Action { get; set; } = null!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = null!;

    public static PurchaseOrderHistoryDto FromDomain(PurchaseOrderHistory h) =>
        new()
        {
            Id = h.Id,
            Action = h.Action,
            OldValue = h.OldValue,
            NewValue = h.NewValue,
            ChangedAt = h.ChangedAt,
            ChangedBy = h.ChangedBy
        };
}
```
No public interface signatures change elsewhere — `FromDomain` is additive. The three handlers' `Handle(...)` signatures, response DTOs, and MediatR contracts are untouched.

### Data Flow
Unchanged end-to-end: repository loads `PurchaseOrder`/`PurchaseOrderHistory` entities → handler maps each entry via `PurchaseOrderHistoryDto.FromDomain` → resulting list is assigned to the response DTO's `History`/`Items` property, with `GetPurchaseOrderByIdHandler` additionally applying `.OrderByDescending(h => h.ChangedAt)` *after* the `Select`, on the DTO — this ordering step must remain exactly where it is (post-mapping), since moving it before mapping or dropping it would be a behavior change outside this refactor's scope.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Reordering `.OrderByDescending` relative to `.Select` in `GetPurchaseOrderByIdHandler` changes result order or introduces a subtle bug | Low | Keep `.Select(PurchaseOrderHistoryDto.FromDomain).OrderByDescending(h => h.ChangedAt)` in that exact sequence — spec already pins this down explicitly; implementer should not "simplify" further |
| Missed call site leaves an inconsistent third mapping in place | Low | Acceptance criterion in spec (`grep for "new PurchaseOrderHistoryDto"` under `UseCases/` returns nothing) is a cheap, mechanical verification step — run it before marking done |
| Existing unit tests (`CreatePurchaseOrderHandlerTests.cs`, `GetPurchaseOrderHistoryHandlerTests.cs`, and `GetPurchaseOrderById` tests if present) don't cover all six fields, masking a copy-paste mistake in the new factory | Low | Tests already assert on `History`/`Items` output today, per spec's own acceptance criteria (must pass unmodified) — no new test is strictly required, but a quick manual diff of the six fields against the domain entity is worth the 30 seconds |

None of these rise above "low" — the change has no runtime behavior surface beyond what's already covered by existing tests.

## Specification Amendments
None. The spec is complete, correctly scoped, and consistent with the codebase's actual conventions as verified above (the `FromLine` reference pattern, the DTO location/ownership rule, and the class-not-record constraint all line up with what's proposed). No additions needed.

## Prerequisites
None. No migrations, config, or infrastructure changes — this can be implemented immediately against the current `main`/feature branch state.
