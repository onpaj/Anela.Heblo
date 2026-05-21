I have enough context to write the review. The spec is well-grounded and matches both prior precedents (Leaflet→KnowledgeBase, Logistics→Manufacture). A few specifics to call out: adapter naming, the `IEnumerable→IReadOnlyList` materialization detail, and the ordering of the architecture test entry.

```markdown
# Architecture Review: Decouple PackingMaterials.ConsumptionCalculationService from Invoices domain

## Skip Design: true

This is a backend-only structural refactor (DI shape, namespace boundaries, architecture test, test doubles). No UI, no MediatR/HTTP contract, no new visual components.

## Architectural Fit Assessment

The spec aligns perfectly with two established and validated precedents in this codebase:

1. **Leaflet → KnowledgeBase** (2026-05-15) — `ILeafletKnowledgeSource` + `KnowledgeBaseLeafletSourceAdapter`, registered in `KnowledgeBaseModule.cs:38`, regression-protected via `ModuleBoundariesTests`.
2. **Logistics → Manufacture** (2026-05-16) — `IInventoryReservationService` + `ManufactureInventoryReservationAdapter`, same pattern.

The "Cross-Module Communication Example: ILeafletKnowledgeSource" section in `docs/architecture/development_guidelines.md:194-207` is the binding spec for this pattern, and the proposal follows it line-by-line:

- Consumer owns the contract (`Application/Features/PackingMaterials/Contracts/`).
- Provider owns the adapter (`Application/Features/Invoices/Infrastructure/`).
- Provider's `Module.cs` registers the DI binding.
- Architecture test enforces the boundary on every build.

**Integration points (verified by reading the code):**
- `ConsumptionCalculationService.cs:37` — only call site of `IIssuedInvoiceRepository` inside PackingMaterials.
- `ConsumptionCalculationService.cs:94-116` (`BuildFactRows`) — only consumer of `IssuedInvoice` fields, and it touches strictly `inv.Id` and `inv.ItemsCount`.
- `PackingMaterialsModule.cs:18` — the apologetic comment that must be removed.
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` and `MockIssuedInvoiceRepository.cs` — the only PackingMaterials test surface touching `IIssuedInvoiceRepository` / `IssuedInvoice`.
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/*` — verified to have **zero** references to Invoices namespaces (grep confirmed). FR-8's audit is expected to be a no-op there.

**Naming-collision finding (already captured in the spec) is correct.** `Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceSource` already exists (Shoptet ingestion side). Renaming the new contract to `IInvoiceConsumptionSource` is the right call. The matching value type `InvoiceConsumptionHeader` parallels `KnowledgeSearchResult` in shape (small, immutable, projection-only).

## Proposed Architecture

### Component Overview

```
backend/src/Anela.Heblo.Application/
├── Features/
│   ├── PackingMaterials/                                 (CONSUMER)
│   │   ├── Contracts/
│   │   │   ├── IInvoiceConsumptionSource.cs              (NEW — consumer-owned)
│   │   │   └── InvoiceConsumptionHeader.cs               (NEW — consumer-owned)
│   │   ├── Services/
│   │   │   └── ConsumptionCalculationService.cs          (MODIFIED — depends on contract)
│   │   └── PackingMaterialsModule.cs                     (MODIFIED — comment removed)
│   │
│   └── Invoices/                                          (PROVIDER)
│       ├── Infrastructure/
│       │   └── InvoiceConsumptionSourceAdapter.cs        (NEW — internal sealed)
│       └── InvoicesModule.cs                             (MODIFIED — DI registration)
│
└── backend/test/Anela.Heblo.Tests/
    ├── Architecture/
    │   └── ModuleBoundariesTests.cs                      (MODIFIED — +rule, new allowlist)
    └── Features/PackingMaterials/
        ├── ConsumptionCalculationServiceTests.cs         (MODIFIED — no Invoices using)
        ├── MockInvoiceConsumptionSource.cs               (NEW — replaces mock below)
        └── MockIssuedInvoiceRepository.cs                (DELETED)

Runtime dependency graph (post-change):

  ConsumptionCalculationService ──depends on──> IInvoiceConsumptionSource
                                                       ▲
                                                       │ implements
                                                       │
                                       InvoiceConsumptionSourceAdapter
                                                       │
                                                       ▼ delegates to
                                              IIssuedInvoiceRepository
                                              (Invoices.Domain, unchanged)
```

### Key Design Decisions

#### Decision 1: Adapter name

**Options considered:**
- `IssuedInvoiceConsumptionSourceAdapter` — names the underlying entity it adapts from.
- `InvoiceConsumptionSourceAdapter` — names what it implements (the contract minus the `I`).
- `PackingMaterialsInvoiceConsumptionAdapter` — encodes both consumer and provider.

**Chosen approach:** `InvoiceConsumptionSourceAdapter.cs`.

**Rationale:** Mirrors the established `KnowledgeBaseLeafletSourceAdapter` shape (provider-side noun + role + `Adapter`), drops the redundant `Issued` prefix that the contract intentionally avoids, and stays within the file-name length other adapters use. Place it in `Anela.Heblo.Application.Features.Invoices.Infrastructure` exactly as `KnowledgeBaseLeafletSourceAdapter` lives in `…KnowledgeBase.Infrastructure`.

#### Decision 2: Return type — `IReadOnlyList<T>` vs. `IEnumerable<T>`

**Options considered:**
- `IEnumerable<InvoiceConsumptionHeader>` — matches the underlying `IIssuedInvoiceRepository.GetHeadersByDateAsync` signature.
- `IReadOnlyList<InvoiceConsumptionHeader>` — matches the `ILeafletKnowledgeSource` precedent.

**Chosen approach:** `IReadOnlyList<InvoiceConsumptionHeader>` (already specified).

**Rationale:** Consistency with the existing decoupling precedent; communicates "fully-materialized snapshot, safe to enumerate twice"; the service already calls `.ToList()` at line 37 today, so this is a net wash on allocations and lets the service drop that `.ToList()` call.

#### Decision 3: Adapter visibility — `internal sealed`

**Options considered:**
- `public sealed` — accessible for friend assemblies / external composition.
- `internal sealed` — only InvoicesModule.cs can see and register it.

**Chosen approach:** `internal sealed` (matches `KnowledgeBaseLeafletSourceAdapter`).

**Rationale:** The adapter is an implementation detail of the binding. Hiding it prevents accidental direct usage anywhere outside the registering module.

#### Decision 4: Adapter behavior — projection only, no extra I/O

**Chosen approach:** `Select(inv => new InvoiceConsumptionHeader(inv.Id, inv.ItemsCount)).ToList()` over the result of `_repository.GetHeadersByDateAsync(date, ct)`. No filtering, no enrichment, no retry, no caching.

**Rationale:** Adapter must be a pure pass-through to preserve current semantics. Any policy change belongs in either the repository or the consumer, not the wire.

#### Decision 5: Don't touch the EF marker-write subtlety

`ConsumptionCalculationService.cs:69-74` writes a no-op `UpdateQuantity` on the first material when `processedCount == 0`, depending on EF change tracking and `GetAllWithAllocationsAsync` not using `AsNoTracking`. The spec correctly lists this as out of scope. **Confirmed — do not touch.** This block must be preserved verbatim; the only mechanical change is `List<IssuedInvoice>` → `IReadOnlyList<InvoiceConsumptionHeader>` in the `BuildFactRows` signature and call site.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/
├── IInvoiceConsumptionSource.cs               NEW
└── InvoiceConsumptionHeader.cs                NEW

backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/
└── InvoiceConsumptionSourceAdapter.cs         NEW

backend/test/Anela.Heblo.Tests/Features/PackingMaterials/
├── MockInvoiceConsumptionSource.cs            NEW (~25 lines, single SetHeaders + GetHeadersByDateAsync)
└── MockIssuedInvoiceRepository.cs             DELETE

backend/test/Anela.Heblo.Tests/Features/Invoices/
└── InvoiceConsumptionSourceAdapterTests.cs    NEW (NFR-4: covers projection + token passthrough)
```

Modified files:
- `Features/PackingMaterials/Services/ConsumptionCalculationService.cs`
- `Features/PackingMaterials/PackingMaterialsModule.cs` (remove comment)
- `Features/Invoices/InvoicesModule.cs` (add DI line + cross-module comment)
- `test/.../Architecture/ModuleBoundariesTests.cs` (third theory row + (empty) allowlist)
- `test/.../Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` (replace `MakeInvoice`/`MockIssuedInvoiceRepository`)

### Interfaces and Contracts

**`IInvoiceConsumptionSource`** — exactly the shape in the spec. No `using Anela.Heblo.Domain.Features.Invoices;`.

**`InvoiceConsumptionHeader`** — `sealed record InvoiceConsumptionHeader(string Id, int ItemsCount)`. `string` matches `IssuedInvoice.Id` (verified: `IssuedInvoice` declares `Id` as `string`, `IEntity<string>`). `int` matches `IssuedInvoice.ItemsCount`. The two fields are sufficient for every `BuildFactRows` branch (verified by reading `ConsumptionCalculationService.cs:94-116`).

**`InvoiceConsumptionSourceAdapter`** — body:

```
internal sealed class InvoiceConsumptionSourceAdapter : IInvoiceConsumptionSource
{
    private readonly IIssuedInvoiceRepository _repository;

    public InvoiceConsumptionSourceAdapter(IIssuedInvoiceRepository repository)
        => _repository = repository;

    public async Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        var invoices = await _repository.GetHeadersByDateAsync(date, cancellationToken);
        return invoices.Select(i => new InvoiceConsumptionHeader(i.Id, i.ItemsCount)).ToList();
    }
}
```

The underlying repository returns `IEnumerable<IssuedInvoice>` (verified at `IIssuedInvoiceRepository.cs:81`), so the `.ToList()` materializes once at the boundary.

**`InvoicesModule` registration** — add after the existing repository registration at `InvoicesModule.cs:19`:

```
// Cross-module contract: Invoices implements PackingMaterials' IInvoiceConsumptionSource
// via an adapter. DI registration owned by provider (Invoices), not consumer
// (PackingMaterials) — keeps the dependency direction inverted properly.
services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();
```

**`ModuleBoundariesTests` new theory row** — append after the Logistics row:

```
new ModuleBoundaryRule(
    Name: "PackingMaterials -> Invoices",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.PackingMaterials",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Invoices",
        "Anela.Heblo.Application.Features.Invoices",
        "Anela.Heblo.Persistence.Invoices",
    },
    Allowlist: new HashSet<string>(StringComparer.Ordinal)),  // empty — clean cutover
```

No `PackingMaterialsAllowlist` constant is needed (unlike Leaflet/Logistics which carry pre-existing exceptions). Inline the empty set to signal "no known violations" explicitly.

### Data Flow

**`ProcessDailyConsumptionAsync(date)`:**

1. `ConsumptionCalculationService` resolves `IInvoiceConsumptionSource` (was: `IIssuedInvoiceRepository`).
2. Calls `source.GetHeadersByDateAsync(date, ct)`.
3. **Boundary:** DI returns `InvoiceConsumptionSourceAdapter` instance.
4. Adapter calls `IIssuedInvoiceRepository.GetHeadersByDateAsync(date, ct)` — the only place `IssuedInvoice` is touched in this call path.
5. Adapter projects each `IssuedInvoice → InvoiceConsumptionHeader(Id, ItemsCount)`, materializes once.
6. Service iterates materials; for each material, `BuildFactRows(material, IReadOnlyList<InvoiceConsumptionHeader>, date)` produces 0/1/N `PackingMaterialConsumption` fact rows depending on `ConsumptionType`.
7. Aggregation / decrement / marker-write / persistence logic at lines 39-79 is **unchanged**.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden compiler-generated reference to `IssuedInvoice` (async state machine, closure capture) keeps the boundary test red after the refactor | Medium | The reflection-based test already handles `DisplayClass` and `<…>d__N` declaring types (see `ModuleBoundariesTests.cs:118-124`). If a violation appears, fix the source (don't allowlist) — the goal is a clean cutover. |
| Test ordering: tests fail before the adapter is registered, masking real issues | Low | Land the changes as one PR: contract + adapter + registration + service refactor + tests together. No half-state in CI. |
| Someone reintroduces an `IssuedInvoice` field later by extending `InvoiceConsumptionHeader` to leak more domain shape | Low | The narrow record is intentional. Reviewers should reject additions to `InvoiceConsumptionHeader` that are not strictly required by `BuildFactRows`. |
| The adapter masks N+1 / pagination concerns from PackingMaterials | Low | The underlying repository call is unchanged — no perf regression. Listed in the spec under NFR-1. |
| `IIssuedInvoiceRepository.GetHeadersByDateAsync` returns `IEnumerable<IssuedInvoice>`; if the EF query is deferred and the adapter is called inside a different scope than the service, enumeration could fail | Low | Adapter calls `.ToList()` *inside* the same scope before returning. Materialization is explicit; deferred-enumeration risk eliminated. |
| Adapter's `internal` visibility blocks the adapter test from a different assembly | Low | `InvoiceConsumptionSourceAdapterTests` lives in `Anela.Heblo.Tests`; add `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]` to `Anela.Heblo.Application` **only if not already present**. Check first — the Leaflet/Logistics adapter tests already work; either the attribute exists, or those adapters are tested through DI not directly. Prefer matching whatever the existing precedent does. |
| New test `MockInvoiceConsumptionSource` accidentally re-imports `Anela.Heblo.Domain.Features.Invoices` (e.g. via a leftover `using`) | Low | FR-7 acceptance and the FR-8 grep cover this. Run `grep -r "Anela.Heblo.*Features.Invoices" backend/test/.../PackingMaterials` as the final gate before commit. |

## Specification Amendments

The spec is implementable as written. Minor refinements to capture:

1. **Adapter file name** — spec leaves this open (`<AdapterName>.cs`). Pin it to `InvoiceConsumptionSourceAdapter.cs` for consistency with `KnowledgeBaseLeafletSourceAdapter.cs`.
2. **`PackingMaterialsAllowlist` style** — spec says "Allowlist = empty set." The existing pattern declares allowlists as `private static readonly HashSet<string>` constants. Since this allowlist is *intentionally* empty, **do not** declare a named constant; pass `new HashSet<string>(StringComparer.Ordinal)` inline at the rule-construction site. A named empty constant invites future drift ("just add one entry here, it's already a list").
3. **Drop the redundant `.ToList()`** — `ConsumptionCalculationService.cs:37` currently calls `(await _invoiceRepository.GetHeadersByDateAsync(...)).ToList()`. After the refactor, the contract returns `IReadOnlyList<…>` directly; the `.ToList()` becomes dead and should be removed. This is a one-line cleanup but worth being explicit about.
4. **`BuildFactRows` parameter type** — spec says `IReadOnlyList<InvoiceConsumptionHeader>`. The method does not need random-access; `IReadOnlyCollection<T>` would also work. Stay with `IReadOnlyList<T>` for symmetry with the contract return type — no two materially-different collection types in one signature.
5. **Test file relocation for the adapter test** — `InvoiceConsumptionSourceAdapterTests` belongs under `backend/test/Anela.Heblo.Tests/Features/Invoices/` (not under `…/PackingMaterials/`). The mirror-source-structure convention applies: the adapter lives in `Features/Invoices/Infrastructure/`, so the test lives in `Features/Invoices/`.

## Prerequisites

None. This refactor stands on infrastructure that already exists:

- `Anela.Heblo.Application` already references itself; no new project references needed.
- `IIssuedInvoiceRepository` and `IssuedInvoice` are stable.
- The architecture-test infrastructure (`ModuleBoundariesTests` theory + `EnumerateReferencedTypes` helper) is already in place; only a new rule entry is added.
- No DB migration, no config change, no infra change, no new NuGet package.

Land the entire change in a single PR so the architecture test and the service refactor go green together.
```