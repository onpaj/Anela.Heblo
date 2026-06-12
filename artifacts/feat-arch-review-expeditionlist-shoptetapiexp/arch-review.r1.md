I have enough context. Now writing the review.

# Architecture Review: Refactor `ShoptetApiExpeditionListSource.CreatePickingList` — extract `FlushBatchAsync` local function

## Skip Design: true

## Architectural Fit Assessment

The proposal fits the codebase cleanly. The existing adapter (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/`) is the canonical home for Shoptet-specific picking-list logic, and extracting an `internal sealed PickingListBatchProcessor` into the same folder mirrors the file-per-responsibility pattern already used in `ExpeditionProtocolDocument.cs`, `ShippingMethodCatalog.cs`, etc.

Three integration points to validate against:

1. **DI registration unchanged** — `ShoptetApiAdapterServiceCollectionExtensions.cs:76` registers `IPickingListSource` only. The helper is *not* a DI participant; it is owned and instantiated by the source. This is consistent with how `ShoptetInvoiceMapper` and `BillingMethodMapper` are owned objects within the adapter, but here we go even further: no DI registration at all, since the helper is purely internal to one call site.
2. **Logger category stability** — `ShoptetApiExpeditionListSource` is registered as transient. Passing its `ILogger<ShoptetApiExpeditionListSource>` instance to the helper typed as `ILogger` preserves the `Microsoft.Extensions.Logging` category that operations alerting relies on, and the existing `Mock<ILogger<ShoptetApiExpeditionListSource>>` assertions at `ShoptetApiExpeditionListSource_CoolingMarkerTests.cs:266,278` continue to match without modification. This is a deliberate, correct choice.
3. **InternalsVisibleTo already wired** — `ShoptetApiExpeditionListSource.cs:13–14` exposes internals to both test assemblies. The `internal sealed` helper is reachable from `Anela.Heblo.Adapters.Shoptet.Tests` (helper-level tests) and `Anela.Heblo.Tests` (existing `ApplyEnrichment` tests). No new assembly attribute required.

The spec's separation of *per-run dependencies* (constructor) from *per-batch values* (method parameters) correctly avoids the closure capture anti-pattern that motivated the brief.

## Proposed Architecture

### Component Overview

```
ShoptetApiExpeditionListSource (transient, IPickingListSource)
│
├── owns ──> PickingListBatchProcessor (internal sealed, NEW)
│              ctor: ICatalogRepository, ShoptetOrderClient,
│                    Func<ExpeditionProtocolData, byte[]>, ILogger
│              ├── FlushAsync(batch, method, batchIndex, timestamp,
│              │                onBatchFilesReady, ct) -> Task<string>
│              ├── (optional) EnrichBatchAsync(batch, ct)
│              └── (optional) WriteCoolingMarkersAsync(batch, ct)
│
└── CreatePickingList(...) — driver
       1. FetchAllOrdersAsync → carrier groups
       2. Load cooling matrix + gift setting (once/run)
       3. Per carrier: greedy batch → processor.FlushAsync(...)
                                        → append returned path to exportedFiles
       4. Optional status update
       5. Return PrintPickingListResult
```

The processor instance is constructed once per `CreatePickingList` call and reused across all batches in that run. Lifetime matches the source's own per-request lifetime; the processor holds no mutable state across calls.

### Key Design Decisions

#### Decision 1: Direct instantiation, no DI registration
**Options considered:**
- (A) Register `PickingListBatchProcessor` as transient in DI; inject into `ShoptetApiExpeditionListSource`.
- (B) Direct `new` inside `ShoptetApiExpeditionListSource.CreatePickingList`.

**Chosen approach:** (B) — instantiate directly inside the source.

**Rationale:** The processor has one consumer, owns no public abstraction, and depends on the resolved `Func<ExpeditionProtocolData, byte[]>` that the source already resolves with a default fallback (`generateDocument ?? ExpeditionProtocolDocument.Generate`). Registering it in DI would either duplicate the resolution logic or require yet another factory registration, with no benefit. This also keeps the helper `internal sealed` and out of the adapter's DI surface, matching the spec's NFR-3.

#### Decision 2: Accept concrete `ShoptetOrderClient`, not `IEshopOrderClient`
**Options considered:**
- (A) Take `ShoptetOrderClient` (concrete).
- (B) Take `IEshopOrderClient` and downcast for `SetAdditionalFieldAsync`.
- (C) Introduce a new narrow interface `IOrderAdditionalFieldClient`.

**Chosen approach:** (A) — concrete class, as specified.

**Rationale:** `SetAdditionalFieldAsync` lives on the concrete `ShoptetOrderClient` only (not on `IEshopOrderClient`). Adding a new interface for the helper is YAGNI: a single consumer, tested via the existing HTTP handler mocks. Tests for the helper unit-test path use a `ShoptetOrderClient` constructed against a `Mock<HttpMessageHandler>` — the same pattern already in `ShoptetApiExpeditionListSource_CoolingMarkerTests.cs:118–169`.

#### Decision 3: Driver retains accumulation of `exportedFiles`
**Options considered:**
- (A) Helper appends to a list passed in by reference.
- (B) Helper returns the produced path; driver appends.

**Chosen approach:** (B) — as specified.

**Rationale:** Returning the path is the explicit data flow; mutation-by-reference is exactly the closure pattern we are removing. Keeping accumulation in the driver also keeps the helper stateless across calls, which is necessary for safe reuse of one instance across multiple batches.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/
├── ExpeditionProtocolData.cs              (unchanged)
├── ExpeditionProtocolDocument.cs          (unchanged)
├── ShippingMethod.cs                      (unchanged)
├── ShippingMethodCatalog.cs               (unchanged)
├── ShippingMethodRegistry.cs              (unchanged)
├── ShoptetApiExpeditionListSource.cs      (slim driver)
└── PickingListBatchProcessor.cs           (NEW, internal sealed, ≤ 400 lines)

backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/
├── ShoptetApiExpeditionListSource_CoolingMarkerTests.cs  (unchanged)
└── PickingListBatchProcessorTests.cs                     (NEW)
```

Test file name follows the existing convention (`{Class}Tests.cs`). New helper tests do **not** duplicate driver-level integration tests; they target the four responsibilities the spec calls out: catalog enrichment, PDF/file output, cooling-marker PATCH (success + failure), and callback semantics.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

internal sealed class PickingListBatchProcessor
{
    public PickingListBatchProcessor(
        ICatalogRepository catalog,
        ShoptetOrderClient client,
        Func<ExpeditionProtocolData, byte[]> generateDocument,
        ILogger logger);

    public Task<string> FlushAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        ShippingMethod method,
        int batchIndex,
        string timestamp,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken);
}
```

**Contract rules developers must follow:**
- Logger parameter type is **base `ILogger`**, never `ILogger<PickingListBatchProcessor>`. This is load-bearing for log-category continuity (FR-7) — do not change it during the refactor or any follow-up.
- `FlushAsync` must produce the file name `{timestamp}_{method.Name}_{batchIndex}.pdf` and write it to `Path.GetTempPath()`. Do not introduce a configurable output directory in this PR.
- PATCH failure handling: catch every exception except `OperationCanceledException`; log at `Warning` with the exact template `"Failed to set Shoptet additionalField[{Index}]={Value} for order {OrderCode}; PDF print continues."` Order code must appear in the formatted message (asserted by `CreatePickingList_PatchFails_PdfStillCompletes`).
- Constants `CoolingMarkerValue = "CHLAZENE"` and `CoolingAdditionalFieldIndex = 6` move with the PATCH logic to the helper, or stay on the source — either is fine; the spec does not constrain placement. Recommendation: **move them into `PickingListBatchProcessor`** since that is where they are used after the refactor. The source no longer references them.

### Data Flow

Per-run flow (unchanged externally; the only change is in step 4):

```
CreatePickingList(request, onBatchFilesReady, ct)
  ├── FetchAllOrdersAsync(statusId, ct)                       → List<OrderSummary>
  ├── Group by ShippingMethod, filter by request.Carriers     → Dictionary<ShippingMethod, List<...>>
  ├── Load cooling matrix + gift setting                      → (once)
  ├── Instantiate PickingListBatchProcessor                   → (once)
  └── For each (method, orders):
        ├── For each order: client.GetExpeditionOrderDetailAsync → MapToExpeditionOrder → resolve carrier cooling/gift
        └── Greedy batch loop:
              ├── On overflow:  path = await processor.FlushAsync(batch, method, batchIndex++, timestamp, cb, ct)
              │                 exportedFiles.Add(path)
              └── At end:       path = await processor.FlushAsync(remaining, method, batchIndex, timestamp, cb, ct)
                                exportedFiles.Add(path)
        Inside FlushAsync:
        ├── Distinct product codes → catalog.GetByIdAsync (N lookups)
        ├── ApplyEnrichment (existing internal static, unchanged)
        ├── Build ExpeditionProtocolData → generateDocument → byte[]
        ├── File.WriteAllBytesAsync to Path.GetTempPath()
        ├── For each cooled order: client.SetAdditionalFieldAsync (best-effort, warn-log on failure)
        ├── if (onBatchFilesReady != null) await onBatchFilesReady([path])
        └── return path
  ├── (Optional) UpdateStatusAsync for processedCodes
  └── return PrintPickingListResult { ExportedFiles = exportedFiles, TotalCount = processedCodes.Count }
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Subtle behavior drift in the cooling-marker PATCH loop during extraction (e.g., catch clause semantics, exception filter `is not OperationCanceledException`). | High | Run all 4 tests in `ShoptetApiExpeditionListSource_CoolingMarkerTests` *first* (red→green); a green run proves the PATCH semantics including the warn-log path. Add the FR-4 helper tests as additional coverage, not as the only safety net. |
| Closure-to-parameter pass-through order error (especially `batchIndex` increment, which the spec correctly moves to the driver). | Medium | The driver increments `batchIndex` *after* the overflow flush (mirroring the current code at line 204). The final-drain flush at line 216 uses the un-incremented value. The refactor must preserve both call sites exactly. Cover this with a test that creates ≥ 2 batches and asserts file names contain `_0.pdf` and `_1.pdf`. |
| Logger category regression if someone "cleans up" the `ILogger` parameter to `ILogger<PickingListBatchProcessor>` in a follow-up. | Medium | Add an XML doc `///` comment on the constructor parameter stating *why* it is the base interface (preserves ops/alert log category). This is one of the rare cases where a comment is justified — the rule is non-obvious and removing it would silently break dashboards. |
| Test author duplicates driver-level integration tests at the helper level, bloating the suite without adding signal. | Low | Spec Out-of-Scope explicitly forbids this. Helper tests should each touch exactly one of: enrichment, PDF output, PATCH success, PATCH failure, callback. Anything that requires HTTP `GetOrdersByStatusAsync` mocks belongs at the driver level. |
| `Path.GetTempPath()` and `File.WriteAllBytesAsync` make helper unit tests touch the real filesystem. | Low | Acceptable — same pattern as the existing driver tests. Test cleanup is not necessary because the temp files are timestamped and short-lived. Do not add a filesystem abstraction in this PR. |

## Specification Amendments

The spec is mature and complete. Minor refinements:

1. **Constants placement (clarification, not a change).** The spec leaves `CoolingMarkerValue` / `CoolingAdditionalFieldIndex` placement implicit. Recommend moving them into `PickingListBatchProcessor` since the source no longer references them after the refactor. This is a behavior-neutral cleanup that keeps the constants co-located with their only user.

2. **Add one explicit driver test (FR-3 addendum).** When the spec calls for `CreatePickingList` ≤ 50 lines, the assertion is structural, not behavioral. Recommend adding one test that constructs `≥ maxItems + 1` items across `≥ 2` orders to force a mid-loop flush, verifying both batches produce files named `_0.pdf` and `_1.pdf`. This locks in the `batchIndex` pass-through behavior identified in the risk table above.

3. **No further amendments.** Do not introduce `IOrderAdditionalFieldClient`, `IFileSystem`, or any other "future-proofing" interface in this PR — YAGNI, and the spec's Out of Scope section already forbids it.

## Prerequisites

None. All required infrastructure exists:

- `InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")` is already declared at `ShoptetApiExpeditionListSource.cs:13`.
- `ICatalogRepository`, `ShoptetOrderClient`, `Func<ExpeditionProtocolData, byte[]>`, and `ILogger<ShoptetApiExpeditionListSource>` are already injected into the source.
- Existing `Mock<HttpMessageHandler>` + `ShoptetOrderClient` pattern in the cooling-marker tests is directly reusable for helper-level PATCH tests.
- No DB migrations, config changes, or new Key Vault secrets required.

Implementation can begin immediately.