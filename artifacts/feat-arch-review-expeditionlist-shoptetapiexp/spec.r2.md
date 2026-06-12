# Specification: Refactor `ShoptetApiExpeditionListSource.CreatePickingList` — extract `FlushBatchAsync` local function

## Summary
Refactor the 183-line `CreatePickingList` method in `ShoptetApiExpeditionListSource` by extracting its nested `FlushBatchAsync` local function (lines 124–194, ~70 lines, closes over 8 outer-scope variables) into a separate, dependency-injectable helper. The refactor preserves all existing behavior (batching, PDF generation, cooling-marker writes, file paths, callbacks, error handling) while unblocking isolated unit tests of the per-batch flush logic — especially the cooling-marker Shoptet PATCH that is currently only reachable through full integration paths.

## Background
`ShoptetApiExpeditionListSource.CreatePickingList` (backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs:49–232) drives picking-list creation for the Shoptet adapter. The method:

1. Fetches orders, filters/groups by carrier.
2. Loads cooling matrices and gift settings once per run.
3. For each carrier, fetches order details and greedily batches by item count.
4. For each batch, runs `FlushBatchAsync` (lines 124–194), which:
   - **Catalog enrichment** — looks up stock, location, cooling, and price by product code.
   - **PDF generation** — composes `ExpeditionProtocolData` and invokes `_generateDocument`.
   - **File output** — writes the PDF to `Path.GetTempPath()` and tracks the path.
   - **Shoptet side-effect** — for each cooled order, PATCHes Shoptet `additionalField[6]="CHLAZENE"` via `SetAdditionalFieldAsync`; warning-logs and swallows non-cancellation exceptions.
   - **Callback** — invokes `onBatchFilesReady` with the single new file.

`FlushBatchAsync` closes over `_catalog`, `_client`, `batchIndex`, `method`, `carrierDisplayName`, `exportedFiles`, `cancellationToken`, and `onBatchFilesReady`. As a local function (not a private method), it cannot be invoked directly from tests. Existing coverage (`ShoptetApiExpeditionListSource_CoolingMarkerTests`, `ShoptetApiExpeditionListSourceTests`) exercises it only through full `CreatePickingList` HTTP-mocked integration paths. This makes targeted assertions on cooling-marker behavior, enrichment edge cases, or callback semantics expensive to set up and brittle.

This refactor was filed by the daily architecture-review routine on 2026-06-06. It exceeds the 50-line method guideline and the deep closure makes incremental changes risky.

## Functional Requirements

### FR-1: Preserve `CreatePickingList` observable behavior
The refactor is behavior-preserving. After the refactor, `CreatePickingList` must produce identical results — same file paths, same PDF bytes (given identical `_generateDocument`), same Shoptet PATCH calls, same `onBatchFilesReady` invocations, same `PrintPickingListResult` — for every input that currently works.

**Acceptance criteria:**
- All four existing tests in `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` pass unchanged.
- All existing tests in `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs` pass unchanged.
- The full `dotnet test` suite passes.

### FR-2: Extract a `PickingListBatchProcessor` helper
Introduce a dedicated helper that owns the per-batch flush responsibility. Each piece of context that was previously captured from the outer scope is passed in explicitly, either via constructor (long-lived dependencies) or method parameter (per-call values).

Constructor dependencies (long-lived):
- `ICatalogRepository`
- `ShoptetOrderClient` (for the cooling PATCH)
- `Func<ExpeditionProtocolData, byte[]>` (PDF generator)
- `ILogger` — the existing `ILogger<ShoptetApiExpeditionListSource>` is passed through as the base `ILogger` interface (see FR-7).

`FlushAsync` parameters (per-call values):
- `IReadOnlyList<ExpeditionOrder> batch`
- `ShippingMethod method` (provides `Name` and `DisplayName`)
- `int batchIndex`
- `string timestamp`
- `Func<IList<string>, Task>? onBatchFilesReady`
- `CancellationToken cancellationToken`

`FlushAsync` returns the produced file path (e.g. `Task<string>`) so the driver can append it to the run-level `exportedFiles` list — keeping accumulation responsibility in the driver, not in the flush helper.

**Acceptance criteria:**
- A new helper type (default name: `PickingListBatchProcessor`) lives in the same project under `Expedition/`.
- The helper has no `static` mutable state and no captured outer variables.
- `CreatePickingList` constructs one instance per run and calls `FlushAsync` once per batch.
- `FlushAsync` produces the same file name pattern: `{timestamp}_{method.Name}_{batchIndex}.pdf`.
- `FlushAsync` returns the file path; the caller appends to `exportedFiles`.

### FR-3: Reduce `CreatePickingList` to a driver under 50 lines
After extraction, `CreatePickingList` is a loop-and-dispatch driver. It still owns: fetching orders, building per-carrier groupings, loading cooling/gift settings once per run, iterating carriers, greedy batch accumulation, dispatching to `FlushAsync`, status updates, and assembling `PrintPickingListResult`.

**Acceptance criteria:**
- `CreatePickingList` body is ≤ 50 non-blank lines, or — if the orchestration is genuinely larger — split into named private methods that are each ≤ 50 lines (e.g., `BuildPerCarrierOrdersAsync`, `BatchAndFlushAsync`).
- No nested local functions remain inside `CreatePickingList`.

### FR-4: Cooling-marker PATCH semantics are preserved
The current cooling-marker logic:
- Iterates batch orders, skips those where `IsCooled == false`.
- Calls `_client.SetAdditionalFieldAsync(order.Code, 6, "CHLAZENE", cancellationToken)`.
- Catches every exception except `OperationCanceledException`, logs a warning with the order code and field metadata, and continues.

This must remain identical, including:
- The constants `CoolingMarkerValue = "CHLAZENE"` and `CoolingAdditionalFieldIndex = 6`.
- `OperationCanceledException` propagates (PATCH errors do not).
- The log message format and severity (`LogLevel.Warning`) — verified by `CreatePickingList_PatchFails_PdfStillCompletes`.

**Acceptance criteria:**
- `CreatePickingList_PatchFails_PdfStillCompletes` still passes without modification.
- A new isolated unit test on the helper asserts that PATCH is invoked once per cooled order and zero times per non-cooled order.
- A new isolated unit test asserts that a thrown `HttpRequestException` from PATCH is logged at `Warning` and the flush returns normally.

### FR-5: Catalog enrichment is independently testable
The catalog-enrichment block (lines 126–151) — looking up stock, location, cooling, and price by product code, then invoking `ApplyEnrichment` — must be exercisable without HTTP mocks.

**Acceptance criteria:**
- At least one new unit test invokes `FlushAsync` directly with a mock `ICatalogRepository` and asserts that the resulting `ExpeditionProtocolData.Orders[].Items` reflect the enrichment.
- `ApplyEnrichment` (existing `internal static`) remains unchanged and continues to be the single point where item mutation happens.

### FR-6: Callback contract is preserved
`onBatchFilesReady` is invoked once per flushed batch with a single-element list containing the new file path. The callback awaits before `FlushAsync` returns.

**Acceptance criteria:**
- A new unit test asserts that `onBatchFilesReady` is invoked exactly once per `FlushAsync` call with a 1-element list.
- A null `onBatchFilesReady` is handled without throwing (matches current behavior).

### FR-7: Logger category stability
The helper receives the existing `ILogger<ShoptetApiExpeditionListSource>` instance typed as the base `ILogger` interface. The helper does not declare its own `ILogger<PickingListBatchProcessor>` and is not registered in DI. This preserves the log category that ops/alerting filter on and keeps existing logger-mock assertions matching unchanged.

**Acceptance criteria:**
- `PickingListBatchProcessor` constructor parameter is typed `ILogger` (not `ILogger<PickingListBatchProcessor>`).
- `ShoptetApiExpeditionListSource` passes its own `_logger` field through when constructing the helper.
- No new DI registration is added for `PickingListBatchProcessor`.
- The existing `Mock<ILogger<ShoptetApiExpeditionListSource>>`-based assertions at `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs:266` and `:278` continue to pass without modification.

## Non-Functional Requirements

### NFR-1: Code quality
- Method size: `CreatePickingList` body ≤ 50 lines; `FlushAsync` ≤ 50 lines (split further if needed).
- File size: any new file ≤ 400 lines.
- No `static` mutable state.
- Nullable reference types respected throughout.
- `dotnet format` passes.
- `dotnet build` passes with zero new warnings.

### NFR-2: Testability
- The new helper is reachable from `Anela.Heblo.Adapters.Shoptet.Tests` via the existing `InternalsVisibleTo` (line 13). Prefer `internal sealed` to keep the public surface unchanged.
- Tests for the new helper do not depend on `HttpClient` mocks — they use `Mock<ICatalogRepository>` plus a fake/spy for the Shoptet PATCH side-effect.

### NFR-3: Backward compatibility
- Public API of `ShoptetApiExpeditionListSource` (constructor signature, `CreatePickingList` signature, `IPickingListSource` contract) is unchanged.
- DI registration (wherever the source is registered) does not change. `PickingListBatchProcessor` is instantiated directly inside `ShoptetApiExpeditionListSource` using its existing constructor dependencies — no new DI registration.

### NFR-4: Performance
No regression. The helper does the same work in the same order; per-batch overhead is bounded by one allocation of the helper instance per run (or per call, if instantiated per batch — either is acceptable).

### NFR-5: Logging
The warning-log shape on PATCH failure must match the existing template exactly:
`"Failed to set Shoptet additionalField[{Index}]={Value} for order {OrderCode}; PDF print continues."`
The `CreatePickingList_PatchFails_PdfStillCompletes` test asserts the order code appears in the message — this must continue to hold. The log category remains `ShoptetApiExpeditionListSource` (see FR-7).

## Data Model
No data-model changes. Types used:
- `ExpeditionOrder`, `ExpeditionOrderItem`, `ExpeditionProtocolData` — unchanged.
- `ShippingMethod` (provides `Name`, `DisplayName`, `MaxItems`, `MaxOrders`, `Carrier`) — unchanged.
- `Cooling`, `Carriers`, `DeliveryHandling` enums — unchanged.
- `PrintPickingListResult` — unchanged.

## API / Interface Design

### Proposed helper shape

```csharp
internal sealed class PickingListBatchProcessor
{
    private readonly ICatalogRepository _catalog;
    private readonly ShoptetOrderClient _client;
    private readonly Func<ExpeditionProtocolData, byte[]> _generateDocument;
    private readonly ILogger _logger;

    public PickingListBatchProcessor(
        ICatalogRepository catalog,
        ShoptetOrderClient client,
        Func<ExpeditionProtocolData, byte[]> generateDocument,
        ILogger logger)
    {
        _catalog = catalog;
        _client = client;
        _generateDocument = generateDocument;
        _logger = logger;
    }

    public async Task<string> FlushAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        ShippingMethod method,
        int batchIndex,
        string timestamp,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken)
    {
        // 1. Enrich items from catalog
        // 2. Build ExpeditionProtocolData and generate PDF
        // 3. Write PDF to temp path
        // 4. PATCH cooling marker for each cooled order (best-effort, warn-log on failure)
        // 5. Invoke onBatchFilesReady with [filePath]
        // 6. Return filePath
    }
}
```

The constructor takes the base `ILogger` interface (not the generic `ILogger<T>`) — `ShoptetApiExpeditionListSource` passes its own `ILogger<ShoptetApiExpeditionListSource>` instance through, preserving the log category (see FR-7).

Optional internal sub-methods within the helper (split for the 50-line limit):
- `EnrichBatchAsync(batch, ct)` — catalog lookups + `ApplyEnrichment`.
- `WriteCoolingMarkersAsync(batch, ct)` — PATCH loop with warning-log on failure.

### Driver shape (post-refactor)

`CreatePickingList` constructs one `PickingListBatchProcessor` per run, passing its `_logger` field (the existing `ILogger<ShoptetApiExpeditionListSource>`) as the `ILogger` parameter. The driver calls `processor.FlushAsync(...)` from both flush sites (mid-loop on overflow and end-of-loop drain) and appends the returned path to `exportedFiles`.

No public API changes. No new DI registrations required.

## Dependencies
- `ICatalogRepository` — already injected.
- `ShoptetOrderClient` — already injected.
- `Func<ExpeditionProtocolData, byte[]>` — already injected with a default.
- `ILogger<ShoptetApiExpeditionListSource>` — already injected; passed through to the helper typed as `ILogger`.

No new NuGet packages. No new external services. No new DI registrations.

## Out of Scope
- Changing the cooling-marker semantics (field index, value, error handling).
- Changing the batching algorithm (greedy by `MaxItems` / `MaxOrders`).
- Changing PDF generation, file naming, or temp-file location.
- Changing the `IPickingListSource` contract or the `CreatePickingList` signature.
- Promoting `MapToExpeditionOrder`, `ApplyEnrichment`, `ResolveCarrierCooling`, `ResolveCarrierCoolingText`, or `ResolveGiftBadge` to a different visibility or location.
- Adding retry logic to the cooling PATCH.
- Replacing the `Func<ExpeditionProtocolData, byte[]>` delegate with a typed interface.
- Refactoring `FetchAllOrdersAsync` or the per-carrier detail-fetch loop.
- Adding new caching, concurrency, or parallelism.
- Test coverage for orchestration paths already covered by existing tests (we add helper-level tests, not duplicate driver-level tests).
- Introducing a `PickingListBatchProcessor`-typed logger category or DI registration for the helper.

## Open Questions
None.

## Status: COMPLETE