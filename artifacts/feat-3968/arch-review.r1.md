# Architecture Review: DataQuality-owned invoice snapshot contracts (decouple from Invoices domain types)

## Skip Design: true

This is a pure backend contract/DTO refactor — three new internal C# types, two interface signature
changes, a mapping relocation into existing provider adapters, and an architecture-test allowlist
edit. No controller, no HTTP surface, no MediatR request/response, no frontend code, and no visual
component is touched anywhere in the spec (confirmed: "No HTTP/controller surface changes" in the
spec's own API/Interface Design section, and `InvoiceDqtJobRunner`'s scheduling/reporting surface is
explicitly out of scope). Nothing here warrants a design pass.

## Architectural Fit Assessment

This spec is not proposing a new pattern — it is finishing an already-half-applied one. Verified
directly in the code:

- `IInvoiceShoptetSource.cs` and `IInvoiceErpClient.cs` (both in
  `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/`) are correctly *located* per
  the consumer-owns-the-contract pattern (`development_guidelines.md`, "Cross-Module Communication
  Example: ILeafletKnowledgeSource"), but both still `using Anela.Heblo.Domain.Features.Invoices;` and
  return/accept Invoices domain types (`IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`,
  `IssuedInvoiceDetail`) verbatim.
- The provider side is already built correctly: `InvoiceShoptetSourceAdapter` and
  `InvoiceErpClientAdapter` (`Features/Invoices/Infrastructure/`) are `internal sealed`, delegate to
  `IIssuedInvoiceSource`/`IIssuedInvoiceClient`, and are registered in `InvoicesModule.cs` with
  lifetimes that deliberately mirror their wrapped services (Singleton for the Shoptet source, Scoped
  for the ERP client — confirmed at `InvoicesModule.cs:55-56` with an explicit comment explaining why).
  This spec does not need to touch DI lifetimes at all.
- The codebase already has two *complete* examples of this exact target shape one folder over:
  `PackingMaterials.Contracts.IInvoiceConsumptionSource` / `InvoiceConsumptionHeader` and
  `Analytics.IInvoiceImportStatisticsSource` / `DailyInvoiceCount`, both with adapters in
  `Features/Invoices/Infrastructure/` and adapter-level tests already living in
  `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/`
  (`InvoiceConsumptionSourceAdapterTests.cs`, `InvoiceImportStatisticsSourceAdapterTests.cs`). FR-4/FR-7
  should follow that exact directory and test-class placement, not invent a new location.
- `ModuleBoundariesTests.cs` already scaffolds this fix: `DataQualityInvoicesAllowlist` (line ~151) has
  7 entries and a comment reading *"Follow-up: extract a DataQuality-owned snapshot DTO and map in the
  adapters"* — this is that follow-up, and the empty-allowlist end-state for `LeafletAllowlist`,
  `ArticleAllowlist`, `SmartsuppKnowledgeBaseAllowlist` (lines 24, 27, 32) is the literal template to
  copy for `DataQualityInvoicesAllowlist`.

One deviation from an existing codebase convention is worth flagging, not correcting: `PackingMaterials`'
own consumer-owned type, `InvoiceConsumptionHeader`, is a `sealed record` (`Contracts/InvoiceConsumptionHeader.cs:8`) —
this violates the "DTOs are classes, never records" rule already. The spec correctly does **not**
propagate that mistake — FR-1 explicitly mandates classes for `DqtInvoiceSnapshot`/`DqtInvoiceItem`/
`DqtInvoiceSourceQuery`. Do not use `InvoiceConsumptionHeader` as a copy-paste template for the type
declaration itself; use it only for adapter/DI/test placement.

Overall fit: high. This is a mechanical, well-scoped closure of a documented, tracked violation, using
a pattern with two other live precedents in the same module. No new architectural concept is
introduced.

## Proposed Architecture

### Component Overview

```
DataQuality module                              Invoices module
───────────────────                              ───────────────
InvoiceDqtJobRunner
    │
    ▼
InvoiceDqtComparer  ───consumes───►  IInvoiceShoptetSource  ◄──implements──  InvoiceShoptetSourceAdapter
    │                                (DataQuality.Contracts)                 (Invoices.Infrastructure)
    │                                   returns/accepts                          wraps
    │                                   DqtInvoiceSnapshot                   IIssuedInvoiceSource
    │                                   DqtInvoiceSourceQuery                    │
    │                                                                            ▼
    │                                                                     IssuedInvoiceDetailBatch
    │                                                                     (flatten + map here)
    │
    └─────────────────────────────►  IInvoiceErpClient      ◄──implements──  InvoiceErpClientAdapter
                                     (DataQuality.Contracts)                  (Invoices.Infrastructure)
                                        returns                                   wraps
                                        DqtInvoiceSnapshot                    IIssuedInvoiceClient
                                                                                   │
                                                                                   ▼
                                                                            IssuedInvoiceDetail
                                                                            (map here)

DI registration (unchanged, InvoicesModule.AddInvoicesModule):
  AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>()
  AddScoped  <IInvoiceErpClient,      InvoiceErpClientAdapter>()

Compile-time dependency direction after the fix:
  DataQuality.Contracts  →  (nothing in Invoices)
  Invoices.Infrastructure → DataQuality.Contracts  (adapters implement DataQuality's interfaces)
  Invoices.Infrastructure → Invoices.Domain          (adapters read Invoices' own types to map from)
```

The arrow that currently runs `DataQuality.Contracts → Invoices.Domain` (via the `using` in the two
interface files, and via `InvoiceDqtComparer`'s own `using`) is deleted. The only cross-module arrow
that survives is `Invoices.Infrastructure → DataQuality.Contracts`, which is the correct direction:
provider implements consumer's interface, never the reverse.

### Key Design Decisions

#### Decision 1: Where the flatten (`IssuedInvoiceDetailBatch` → `List<IssuedInvoiceDetail>`) happens
**Options considered:**
(a) Keep `IInvoiceShoptetSource.GetAllAsync` returning a DataQuality-owned batch wrapper
    (e.g. `DqtInvoiceBatch { List<DqtInvoiceSnapshot> Invoices }`) to mirror `IssuedInvoiceDetailBatch`
    structurally.
(b) Drop the batch/grouping concept from the contract entirely; `IInvoiceShoptetSource.GetAllAsync`
    returns `List<DqtInvoiceSnapshot>` directly, flattening happens inside `InvoiceShoptetSourceAdapter`.

**Chosen approach:** (b), per FR-2. Confirmed by reading `InvoiceDqtComparer.cs:29-30`: the comparer's
very first act on the batch result is `shoptetBatches.SelectMany(b => b.Invoices).ToList()` —
`BatchId` is never read anywhere in the comparer. A consumer contract must "expose only the
operations it actually consumes" (`development_guidelines.md`); a grouping type the consumer
immediately discards is speculative surface, not a consumed operation.

**Rationale:** This also makes `IInvoiceShoptetSource` and `IInvoiceErpClient` return the *identical*
shape (`Task<List<DqtInvoiceSnapshot>>`), which simplifies `InvoiceDqtComparer` (no more asymmetry
between "list of invoices" and "batch of batches of invoices") and removes one intermediate type from
the DataQuality-owned model entirely — fewer types to keep in sync with future comparer field usage.

#### Decision 2: Where the `DateOnly ↔ DateTime` and default-field mapping for the query type happens
**Options considered:**
(a) Keep `DqtInvoiceSourceQuery.DateFrom`/`DateTo` as `DateTime?` (matching `IssuedInvoiceSourceQuery`
    verbatim) so the adapter can pass the query straight through.
(b) Make `DqtInvoiceSourceQuery` use `DateOnly` (matching `CompareAsync(DateOnly from, DateOnly to,
    ...)`'s own signature) and push the `.ToDateTime(TimeOnly.MinValue)` conversion, plus the
    `InvoiceId = null` / `Currency = "CZK"` defaulting, into `InvoiceShoptetSourceAdapter`.

**Chosen approach:** (b), per FR-1/FR-4. Confirmed in `InvoiceDqtComparer.cs:22-27` today: the comparer
builds `IssuedInvoiceSourceQuery` itself and performs `from.ToDateTime(TimeOnly.MinValue)` purely to
satisfy the Invoices-side type's `DateTime?` field — this conversion exists only because the contract
currently forces an Invoices type on the consumer. `DqtInvoiceSourceQuery` should carry the type the
consumer naturally has (`DateOnly`, matching `CompareAsync`'s own parameters), and the adapter — which
already must know about `IssuedInvoiceSourceQuery`'s shape to call `IIssuedInvoiceSource` — is the
correct, and only, place that should also know about its `DateTime?`/`InvoiceId`/`Currency` defaulting
quirks.

**Rationale:** This is exactly the "provider owns the mapping" half of the inverted-dependency pattern:
the adapter already depends on both namespaces (it's `internal sealed` inside `Features/Invoices/`), so
it is the correct — and only legal, post-fix — place for a type-shape adaptation to live. Leaving it in
the comparer would leave a residual, purely cosmetic asymmetry with no boundary benefit.

#### Decision 3: One shared mapping helper vs. duplicated mapping in each adapter
**Options considered:**
(a) Duplicate the `IssuedInvoiceDetail → DqtInvoiceSnapshot` / `IssuedInvoiceDetailItem →
    DqtInvoiceItem` mapping logic inline in both `InvoiceShoptetSourceAdapter.GetAllAsync` and
    `InvoiceErpClientAdapter.GetAllAsync`.
(b) Factor it once as a private static method or internal extension method inside
    `Anela.Heblo.Application.Features.Invoices.Infrastructure`, reused by both adapters.

**Chosen approach:** (b), per FR-4. A single 4-field mapping used identically by two call sites is the
textbook case for extraction; duplicating it risks the two adapters drifting (e.g. one adapter fixed
for a null `Items` list, the other not) with no compensating benefit.

**Rationale/placement constraint:** The spec is explicit — and correctly so — that this mapping code
"must not live in `Contracts/` or in DataQuality's `Services/` folder." It is provider-owned mapping
(Invoices → DataQuality shape), so it belongs beside the two adapters it serves:
`Features/Invoices/Infrastructure/`. Recommend a small `internal static class InvoiceDqtSnapshotMapper`
(or `internal static` extension methods `ToDqtSnapshot()` / `ToDqtItem()`) in a new file
`Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs` — do not bolt it onto either adapter
class as a private method duplicated twice, since "factor it once" is an explicit acceptance criterion.

## Implementation Guidance

### Directory / Module Structure

New file:
```
backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs
    — public class DqtInvoiceSourceQuery
    — public class DqtInvoiceSnapshot
    — public class DqtInvoiceItem
```
(Spec FR-1 allows one file for all three; that matches this codebase's convention of small,
closely-related contract types sharing a file, e.g. `InvoiceConsumptionHeader.cs`. Do not create three
separate files unless you have a specific reason — it adds no boundary value.)

New file (mapping helper, Decision 3):
```
backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs
    — internal static class InvoiceDqtSnapshotMapper
        — ToDqtSnapshot(this IssuedInvoiceDetail) : DqtInvoiceSnapshot
        — ToDqtItem(this IssuedInvoiceDetailItem) : DqtInvoiceItem
```

Modified files (no relocation, only signature/body edits):
```
backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs
backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs
backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs
backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs
backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs
```

New test file (placement matches the two existing sibling adapter test classes exactly):
```
backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapterTests.cs
backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceErpClientAdapterTests.cs
```
(Namespace `Anela.Heblo.Tests.Features.Invoices.Infrastructure`, mocking `IIssuedInvoiceSource`/
`IIssuedInvoiceClient` the same way `InvoiceConsumptionSourceAdapterTests.cs` mocks
`IIssuedInvoiceRepository` — see that file for the exact xUnit/Moq/FluentAssertions style already in
use here.)

### Interfaces and Contracts

```csharp
// DataQuality/Contracts/DqtInvoiceSnapshot.cs
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtInvoiceSourceQuery
{
    public string RequestId { get; set; } = string.Empty;
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
}

public class DqtInvoiceSnapshot
{
    public string Code { get; set; } = string.Empty;
    public decimal TotalWithVat { get; set; }
    public decimal TotalWithoutVat { get; set; }
    public List<DqtInvoiceItem> Items { get; set; } = new();
}

public class DqtInvoiceItem
{
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal WithVat { get; set; }
    public decimal WithoutVat { get; set; }
}
```

```csharp
// DataQuality/Contracts/IInvoiceShoptetSource.cs — signature only, keep existing doc comment
public interface IInvoiceShoptetSource
{
    Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DqtInvoiceSourceQuery query,
        CancellationToken ct = default);
}

// DataQuality/Contracts/IInvoiceErpClient.cs — signature only
public interface IInvoiceErpClient
{
    Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
```

Hard boundary rule (enforced by FR-6, zero-tolerance from this point on): no file under
`Anela.Heblo.Application.Features.DataQuality.*` may contain `using Anela.Heblo.Domain.Features.Invoices;`
or a fully-qualified reference to it, ever again — the allowlist escape hatch is closed after this
change, matching `LeafletAllowlist`/`ArticleAllowlist`/`SmartsuppKnowledgeBaseAllowlist`.

### Data Flow

```
InvoiceDqtJobRunner
  → InvoiceDqtComparer.CompareAsync(DateOnly from, DateOnly to, ct)
      builds DqtInvoiceSourceQuery { RequestId, DateFrom = from, DateTo = to }   (DateOnly, no conversion)
      → IInvoiceShoptetSource.GetAllAsync(DqtInvoiceSourceQuery, ct)
          → InvoiceShoptetSourceAdapter:
              maps DqtInvoiceSourceQuery → IssuedInvoiceSourceQuery
                (RequestId passthrough; DateFrom/DateTo via .ToDateTime(TimeOnly.MinValue);
                 InvoiceId = null; Currency = "CZK")
              → IIssuedInvoiceSource.GetAllAsync(IssuedInvoiceSourceQuery, ct) : List<IssuedInvoiceDetailBatch>
              → SelectMany(b => b.Invoices) → List<IssuedInvoiceDetail>
              → .Select(ToDqtSnapshot) → List<DqtInvoiceSnapshot>
      → IInvoiceErpClient.GetAllAsync(from, to, ct)
          → InvoiceErpClientAdapter:
              → IIssuedInvoiceClient.GetAllAsync(from, to, ct) : List<IssuedInvoiceDetail>   (unchanged call)
              → .Select(ToDqtSnapshot) → List<DqtInvoiceSnapshot>
      → compare List<DqtInvoiceSnapshot> vs List<DqtInvoiceSnapshot>  (tolerance/dup/mismatch logic unchanged)
      → InvoiceDqtComparisonResult
```

Nothing about `InvoiceDqtJobRunner`'s scheduling or `InvoiceDqtComparisonResult`'s shape changes — the
data flow change is entirely contained between `InvoiceDqtComparer` and the two adapters.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent behavior drift in `InvoiceDqtComparer` while retyping every reference from Invoices types to Dqt types (e.g. missing a field, or comparing the wrong nested property) | Medium | FR-5's acceptance criteria pin exact behavior preservation (tolerance `0.02m`, dup-grouping, message formats). Run the existing 14 `InvoiceDqtComparerTests.cs` cases with only fixture-construction types changed — a same-assertions diff is the verification signal, not a rewrite. |
| `DateOnly → DateTime` conversion moved from comparer to adapter introduces an off-by-one or timezone bug not present today | Low | The conversion (`.ToDateTime(TimeOnly.MinValue)`) is copied verbatim, only relocated — no new logic. Cover it with the new `InvoiceShoptetSourceAdapterTests.cs` (FR-4's acceptance criteria already require an adapter-level mapping test). |
| Two mapping call sites (`InvoiceShoptetSourceAdapter`, `InvoiceErpClientAdapter`) drift if the shared mapper (Decision 3) isn't actually shared | Low | Enforce via code review, not tooling — a single `internal static class InvoiceDqtSnapshotMapper` referenced by both adapters is a one-file diff to verify. |
| `ModuleBoundariesTests.cs`'s reflection-based scan has some sharp edge (e.g. it also inspects test assemblies, or a leftover XML-doc `<see cref>` on the interfaces resolves to a type reference) that trips the newly-empty allowlist even after the code fix lands | Low | Run the specific `DataQuality -> Invoices` `ModuleBoundaryTests` test method locally before declaring FR-6 done — do not assume emptying the `HashSet` is sufficient without running the scan. |
| `DqtInvoiceItem` uses de-nested field names (`WithVat`/`WithoutVat` instead of `ItemPrice.WithVat`/`ItemPrice.WithoutVat`) — easy to typo-swap `WithVat`↔`WithoutVat` during the FR-4 mapping since both are `decimal` and the compiler won't catch a swap | Low | The adapter-level test in FR-4's acceptance criteria must assert both fields against distinct, non-symmetric sample values (e.g. `WithVat = 121m, WithoutVat = 100m`, not `100m`/`100m`) so a swap actually fails the test. |

## Specification Amendments

None required — the spec is implementation-ready as written. Every interface signature, field mapping,
file location, and DI/lifetime claim in spec.r1.md was checked against the current code in this review
and matches exactly (confirmed: `IInvoiceShoptetSource.cs`, `IInvoiceErpClient.cs`,
`InvoiceDqtComparer.cs`, `InvoiceShoptetSourceAdapter.cs`, `InvoiceErpClientAdapter.cs`,
`IssuedInvoiceDetail.cs`, `IssuedInvoiceDetailItem.cs`, `InvoicePrice.cs`, `IssuedInvoiceSourceQuery.cs`,
`IssuedInvoiceDetailBatch.cs`, `InvoicesModule.cs` DI lines 55-56, and `ModuleBoundariesTests.cs`'s
`DataQualityInvoicesAllowlist`/sibling-allowlist comment style at lines 22-32 and 148-154). Two
implementation-guidance additions (not spec corrections, since the spec leaves both open at the "or
equivalent" / "e.g." level):

1. **File placement for the new adapter test classes** (spec FR-7 leaves this as "or wherever
   `InvoiceShoptetSourceAdapter`/`InvoiceErpClientAdapter` tests currently live, if any exist"): none
   exist yet, so create
   `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapterTests.cs`
   and `InvoiceErpClientAdapterTests.cs`, matching the two sibling adapter test classes already in that
   exact folder.
2. **Shared mapper file placement** (spec FR-4 leaves this as "e.g. a private static method or an
   internal extension method"): use a dedicated file,
   `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs`,
   rather than a private method living inside one of the two adapter classes — see Decision 3.

## Prerequisites

None. No migration, no config, no feature flag, no infrastructure change. All types this spec depends
on (`IssuedInvoiceDetail`, `IssuedInvoiceDetailItem`, `InvoicePrice`, `IssuedInvoiceSourceQuery`,
`IssuedInvoiceDetailBatch`, `IIssuedInvoiceSource`, `IIssuedInvoiceClient`) already exist unmodified;
`InvoicesModule.AddInvoicesModule`'s DI registrations for the two adapters already exist and are
explicitly out of scope for change. Implementation can start immediately.
