# Extract `PickingListBatchProcessor` from `ShoptetApiExpeditionListSource.CreatePickingList` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 70-line `FlushBatchAsync` local function inside `ShoptetApiExpeditionListSource.CreatePickingList` with a dedicated `internal sealed PickingListBatchProcessor` helper, so the per-batch flush logic (catalog enrichment, PDF generation, file write, Shoptet cooling-marker PATCH, callback) becomes directly unit-testable without HTTP-mocked integration paths.

**Architecture:** Behavior-preserving extraction. The driver (`CreatePickingList`) keeps run-level orchestration (fetch, group, load matrices, batch-accumulate, status update). One processor instance is constructed per call and reused across batches. The processor is `internal sealed`, owned by the source, never registered in DI, and accepts the base `ILogger` interface (not `ILogger<PickingListBatchProcessor>`) so the existing log category `ShoptetApiExpeditionListSource` is preserved for ops/alerting. The constants `CoolingMarkerValue = "CHLAZENE"` and `CoolingAdditionalFieldIndex = 6` move with the PATCH logic into the helper. New helper-level tests construct the processor directly with a `Mock<ICatalogRepository>` and a `ShoptetOrderClient` over a `Mock<HttpMessageHandler>` — same pattern as the existing cooling-marker tests, no driver-level mocking required.

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq (including `Moq.Protected` for `HttpMessageHandler`).

---

## Files

**Create:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/PickingListBatchProcessor.cs` — new `internal sealed` helper class.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs` — new helper-level test fixture.

**Modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — remove `FlushBatchAsync` local function, remove the two `CoolingMarker*` constants, instantiate `PickingListBatchProcessor` once per call, replace both flush sites with `processor.FlushAsync(...)`.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` — add **one** new driver-level test (`CreatePickingList_MultipleBatches_FilenamesContainSequentialBatchIndex`) that forces two batches and asserts `_0.pdf` and `_1.pdf` filenames. Existing four tests are not touched.

**Untouched (verified after refactor):**
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs` — must still pass without modification.

---

## Task 1: Baseline — verify all existing tests pass before any change

**Files:**
- Read: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
- Read: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs`

- [ ] **Step 1: Build the backend solution**

Run from repo root:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` (or unchanged warning count from main).

- [ ] **Step 2: Run the targeted cooling-marker tests**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiExpeditionListSource_CoolingMarkerTests" \
  --no-build
```
Expected: 4 tests pass: `CreatePickingList_CooledOrder_PatchesShoptetAdditionalField`, `CreatePickingList_NonCooledOrder_DoesNotPatchAdditionalField`, `CreatePickingList_PatchFails_PdfStillCompletes`, `CreatePickingList_CooledOrder_UsesCustomCoolingTextFromSetting`.

- [ ] **Step 3: Run the other existing source tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests" \
  --no-build
```
Expected: all tests in `ShoptetApiExpeditionListSourceTests` pass.

- [ ] **Step 4: No commit** — this is a baseline check only.

---

## Task 2: Add `PickingListBatchProcessor` skeleton (compiles, throws `NotImplementedException`)

This task lands a compiling, non-functional helper file so subsequent tasks (extraction, wiring, tests) can edit one file at a time without leaving the tree broken.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/PickingListBatchProcessor.cs`

- [ ] **Step 1: Create the file with the constructor, properties, and `FlushAsync` signature**

```csharp
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

internal sealed class PickingListBatchProcessor
{
    internal const string CoolingMarkerValue = "CHLAZENE";
    internal const int CoolingAdditionalFieldIndex = 6;

    private readonly ICatalogRepository _catalog;
    private readonly ShoptetOrderClient _client;
    private readonly Func<ExpeditionProtocolData, byte[]> _generateDocument;
    // Logger parameter is intentionally typed as the base ILogger (not ILogger<PickingListBatchProcessor>)
    // so the log category remains "ShoptetApiExpeditionListSource". Ops alerting filters on that category;
    // changing it would silently break dashboards. Do not "clean up" to ILogger<T>.
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

    public Task<string> FlushAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        ShippingMethod method,
        int batchIndex,
        string timestamp,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Build to confirm compilation**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build succeeds with no new warnings.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/PickingListBatchProcessor.cs
git commit -m "refactor: add PickingListBatchProcessor skeleton"
```

---

## Task 3: Move flush logic from local function into `PickingListBatchProcessor.FlushAsync`

This is the behavior-preserving extraction. Copy the body of `FlushBatchAsync` from `ShoptetApiExpeditionListSource.cs:124-194` into `FlushAsync`, replacing closed-over identifiers with constructor fields / method parameters, and return `filePath` instead of appending to a captured list.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/PickingListBatchProcessor.cs`

- [ ] **Step 1: Replace the `FlushAsync` body with the extracted implementation**

Replace the entire `FlushAsync` method (currently just throws `NotImplementedException`) with:

```csharp
    public async Task<string> FlushAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        ShippingMethod method,
        int batchIndex,
        string timestamp,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken)
    {
        await EnrichBatchAsync(batch, cancellationToken);

        var fileName = $"{timestamp}_{method.Name}_{batchIndex}.pdf";
        var listId = Path.GetFileNameWithoutExtension(fileName);

        var data = new ExpeditionProtocolData
        {
            CarrierDisplayName = method.DisplayName,
            ListId = listId,
            Orders = batch.ToList(),
        };

        var pdfBytes = _generateDocument(data);
        var filePath = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);

        await WriteCoolingMarkersAsync(batch, cancellationToken);

        if (onBatchFilesReady != null)
            await onBatchFilesReady(new List<string> { filePath });

        return filePath;
    }

    private async Task EnrichBatchAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        CancellationToken cancellationToken)
    {
        // Enrich with stock counts, warehouse positions, and cooling from catalog.
        // Positions are only applied where the Shoptet API left them blank (set components).
        var productCodes = batch.SelectMany(o => o.Items).Select(i => i.ProductCode).Distinct();
        var stockByCode = new Dictionary<string, decimal>();
        var locationByCode = new Dictionary<string, string>();
        var coolingByCode = new Dictionary<string, Domain.Shared.Cooling>();
        var priceByCode = new Dictionary<string, decimal>();
        foreach (var productCode in productCodes)
        {
            var entry = await _catalog.GetByIdAsync(productCode, cancellationToken);
            if (entry != null)
            {
                stockByCode[productCode] = entry.Stock.Eshop;
                if (!string.IsNullOrEmpty(entry.Location))
                    locationByCode[productCode] = entry.Location;
                coolingByCode[productCode] = entry.Properties.Cooling;
                if (entry.PriceWithVat is > 0)
                    priceByCode[productCode] = entry.PriceWithVat.Value;
            }
        }
        ShoptetApiExpeditionListSource.ApplyEnrichment(
            batch.SelectMany(o => o.Items),
            stockByCode,
            locationByCode,
            coolingByCode,
            priceByCode);
    }

    private async Task WriteCoolingMarkersAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        CancellationToken cancellationToken)
    {
        foreach (var order in batch)
        {
            if (!order.IsCooled)
                continue;

            try
            {
                await _client.SetAdditionalFieldAsync(
                    order.Code,
                    CoolingAdditionalFieldIndex,
                    CoolingMarkerValue,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to set Shoptet additionalField[{Index}]={Value} for order {OrderCode}; PDF print continues.",
                    CoolingAdditionalFieldIndex,
                    CoolingMarkerValue,
                    order.Code);
            }
        }
    }
```

Notes for the implementer:
- The `using` directives at the top of the file already cover `ICatalogRepository`, `ShoptetOrderClient`, and `ILogger`. The `Domain.Shared.Cooling` enum is referenced via its full namespace inline above to avoid adding another `using` for a single use; if you prefer, add `using Anela.Heblo.Domain.Shared;` at the top and write `Cooling` unqualified.
- `ApplyEnrichment` stays as the existing `internal static` on `ShoptetApiExpeditionListSource` and is called through that class name (`ShoptetApiExpeditionListSource.ApplyEnrichment(...)`). Do not duplicate or move it.
- `batch.ToList()` is used when constructing `ExpeditionProtocolData.Orders` because that property is `List<ExpeditionOrder>`. This is a copy of references, not a deep clone — same semantics as the original code which passed the same list reference.

- [ ] **Step 2: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: succeeds with no new warnings.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/PickingListBatchProcessor.cs
git commit -m "refactor: implement PickingListBatchProcessor.FlushAsync"
```

---

## Task 4: Wire `PickingListBatchProcessor` into `CreatePickingList` and remove the local function

This task replaces the in-method `FlushBatchAsync` closure with a per-run helper instance, removes the two `CoolingMarker*` constants from the source (they now live on the helper), and updates both flush sites (mid-loop overflow at the original line 203 and end-of-loop drain at the original line 216) to use `processor.FlushAsync(...)`.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`

- [ ] **Step 1: Remove the two cooling-marker constants from the source**

Delete lines 28-29 of `ShoptetApiExpeditionListSource.cs`:
```csharp
    private const string CoolingMarkerValue = "CHLAZENE";
    private const int CoolingAdditionalFieldIndex = 6;
```
These constants now live on `PickingListBatchProcessor`. The source no longer references them.

- [ ] **Step 2: Instantiate the processor once at the start of `CreatePickingList` (after the gift setting load)**

Inside `CreatePickingList`, immediately after the line `var giftSetting = await _giftSettings.GetAsync(cancellationToken);` (currently line 94) and before the `foreach (var (method, orders) in ordersByMethod)` loop, add:

```csharp
        var processor = new PickingListBatchProcessor(_catalog, _client, _generateDocument, _logger);
```

- [ ] **Step 3: Remove the `FlushBatchAsync` local function and replace both call sites**

Delete the entire `async Task FlushBatchAsync(List<ExpeditionOrder> batch) { ... }` local function (currently lines 124-194).

At the mid-loop overflow flush (currently line 203):
```csharp
                    // Flush current batch before starting a new one
                    await FlushBatchAsync(currentBatch);
                    batchIndex++;
```
Replace with:
```csharp
                    // Flush current batch before starting a new one
                    var overflowPath = await processor.FlushAsync(
                        currentBatch, method, batchIndex, timestamp, onBatchFilesReady, cancellationToken);
                    exportedFiles.Add(overflowPath);
                    batchIndex++;
```

At the end-of-loop drain flush (currently line 216):
```csharp
            // Flush any remaining orders
            if (currentBatch.Count > 0)
            {
                await FlushBatchAsync(currentBatch);
            }
```
Replace with:
```csharp
            // Flush any remaining orders
            if (currentBatch.Count > 0)
            {
                var finalPath = await processor.FlushAsync(
                    currentBatch, method, batchIndex, timestamp, onBatchFilesReady, cancellationToken);
                exportedFiles.Add(finalPath);
            }
```

Note: `batchIndex` is incremented *after* the overflow flush and *before* a new batch starts (preserves the original semantics). The final-drain flush uses the un-incremented `batchIndex` — same as the original code.

- [ ] **Step 4: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: succeeds. Zero new warnings.

- [ ] **Step 5: Run all four existing cooling-marker tests**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiExpeditionListSource_CoolingMarkerTests" \
  --no-build
```
Expected: all 4 tests pass. If any fails, the extraction has drifted — diff against the original `FlushBatchAsync` and reconcile.

- [ ] **Step 6: Run the broader source tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests" \
  --no-build
```
Expected: all pass.

- [ ] **Step 7: Run `dotnet format`**

```bash
dotnet format backend/Anela.Heblo.sln --no-restore
```
Expected: no formatting changes, or only whitespace adjustments — commit any changes made by the formatter as part of the next step.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
git commit -m "refactor: replace FlushBatchAsync local function with PickingListBatchProcessor"
```

---

## Task 5: New helper test — callback semantics (FR-6)

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs`

- [ ] **Step 1: Create the test file with a shared builder and the first test (callback invoked once with single-element list)**

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class PickingListBatchProcessorTests
{
    private const string CooledProductCode = "PROD-COOL";
    private const string NormalProductCode = "PROD-NORMAL";
    private const string CooledOrderCode = "ORDER-COOL";
    private const string NormalOrderCode = "ORDER-NORMAL";

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static Mock<HttpMessageHandler> BuildHandler(bool patchShouldThrow = false)
    {
        var handler = new Mock<HttpMessageHandler>();

        if (patchShouldThrow)
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Patch),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Simulated Shoptet PATCH failure"));
        }
        else
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Patch),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(OkJson("""{"data":null,"errors":null}"""));
        }

        return handler;
    }

    private static ShoptetOrderClient BuildClient(Mock<HttpMessageHandler> handler)
    {
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        return new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()));
    }

    private static Mock<ICatalogRepository> BuildCatalog()
    {
        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(CooledProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = CooledProductCode,
                ProductName = "Cooled Product",
                Location = "A-1",
                Properties = new CatalogProperties { Cooling = Cooling.L1 },
            });
        catalog.Setup(x => x.GetByIdAsync(NormalProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = NormalProductCode,
                ProductName = "Normal Product",
                Location = "B-2",
                Properties = new CatalogProperties { Cooling = Cooling.None },
            });
        return catalog;
    }

    private static ShippingMethod BuildMethod() => new()
    {
        Carrier = Carriers.Zasilkovna,
        Id = 1,
        Name = "zas",
        DisplayName = "Zásilkovna",
        MaxItems = 100,
        MaxOrders = 100,
        Guids = ["guid-1"],
    };

    private static ExpeditionOrder BuildCooledOrder() => new()
    {
        Code = CooledOrderCode,
        CustomerName = "Cooled Customer",
        Address = "Chladna 1, 10000 Praha",
        Phone = "+420111222333",
        CarrierCooling = Cooling.L1,
        Items =
        {
            new ExpeditionOrderItem
            {
                ProductCode = CooledProductCode,
                Name = "Cooled Product",
                Variant = string.Empty,
                WarehousePosition = string.Empty,
                Quantity = 1,
                Unit = "ks",
                Cooling = Cooling.L1,
            },
        },
    };

    private static ExpeditionOrder BuildNormalOrder() => new()
    {
        Code = NormalOrderCode,
        CustomerName = "Normal Customer",
        Address = "Normalni 2, 60200 Brno",
        Phone = "+420444555666",
        CarrierCooling = Cooling.None,
        Items =
        {
            new ExpeditionOrderItem
            {
                ProductCode = NormalProductCode,
                Name = "Normal Product",
                Variant = string.Empty,
                WarehousePosition = string.Empty,
                Quantity = 1,
                Unit = "ks",
                Cooling = Cooling.None,
            },
        },
    };

    private static PickingListBatchProcessor BuildProcessor(
        Mock<HttpMessageHandler> handler,
        Mock<ICatalogRepository> catalog,
        ILogger? logger = null,
        Func<ExpeditionProtocolData, byte[]>? generate = null) =>
        new(
            catalog.Object,
            BuildClient(handler),
            generate ?? (_ => new byte[] { 0x25, 0x50, 0x44, 0x46 }),
            logger ?? Mock.Of<ILogger<ShoptetApiExpeditionListSource>>());

    [Fact]
    public async Task FlushAsync_InvokesCallbackOnceWithSingleElementList()
    {
        var handler = BuildHandler();
        var catalog = BuildCatalog();
        var processor = BuildProcessor(handler, catalog);

        var callbackInvocations = new List<IList<string>>();
        Func<IList<string>, Task> callback = paths =>
        {
            callbackInvocations.Add(paths);
            return Task.CompletedTask;
        };

        var path = await processor.FlushAsync(
            new[] { BuildNormalOrder() },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: callback,
            cancellationToken: CancellationToken.None);

        callbackInvocations.Should().HaveCount(1);
        callbackInvocations[0].Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public async Task FlushAsync_DoesNotThrow_WhenCallbackIsNull()
    {
        var handler = BuildHandler();
        var catalog = BuildCatalog();
        var processor = BuildProcessor(handler, catalog);

        var act = async () => await processor.FlushAsync(
            new[] { BuildNormalOrder() },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: null,
            cancellationToken: CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
```

Notes for the implementer:
- Inspect `CatalogAggregate` (under `Anela.Heblo.Domain.Features.Catalog`) before running the test. If the type does not expose the literal property names used here (`ProductCode`, `ProductName`, `Location`, `Properties`, `Stock`, `PriceWithVat`), adjust the builder so it compiles — the existing `BuildSource` helper in `ShoptetApiExpeditionListSource_CoolingMarkerTests.cs:181-194` is a known-good reference. Same for `CatalogProperties` and `Cooling`.
- `ShippingMethod` is a mutable POCO with `init`-less setters — the object initializer above is correct (see `Expedition/ShippingMethod.cs`).
- `ExpeditionOrderItem.Items` is a `List<ExpeditionOrderItem>` initialized to `new()`, so the inline collection initializer works.

- [ ] **Step 2: Build & run the new tests**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~PickingListBatchProcessorTests" \
  --no-build
```
Expected: 2 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs
git commit -m "test: cover PickingListBatchProcessor callback semantics"
```

---

## Task 6: New helper test — catalog enrichment (FR-5)

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs` (append test)

- [ ] **Step 1: Append the enrichment test**

Add the following `[Fact]` to the existing `PickingListBatchProcessorTests` class:

```csharp
    [Fact]
    public async Task FlushAsync_AppliesCatalogEnrichmentToBatchItems()
    {
        ExpeditionProtocolData? captured = null;
        var handler = BuildHandler();
        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(NormalProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = NormalProductCode,
                ProductName = "Normal Product",
                Location = "AISLE-7",
                Properties = new CatalogProperties { Cooling = Cooling.L2 },
            });

        var processor = BuildProcessor(
            handler,
            catalog,
            generate: data =>
            {
                captured = data;
                return new byte[] { 0x25, 0x50, 0x44, 0x46 };
            });

        var orderWithBlankPosition = new ExpeditionOrder
        {
            Code = "ORDER-ENRICH",
            CustomerName = "Customer",
            Address = "Addr",
            Phone = "+420",
            Items =
            {
                new ExpeditionOrderItem
                {
                    ProductCode = NormalProductCode,
                    Name = "Normal Product",
                    Variant = string.Empty,
                    WarehousePosition = string.Empty, // blank — enrichment should fill in
                    Quantity = 1,
                    Unit = "ks",
                },
            },
        };

        await processor.FlushAsync(
            new[] { orderWithBlankPosition },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: null,
            cancellationToken: CancellationToken.None);

        captured.Should().NotBeNull();
        var item = captured!.Orders.Single().Items.Single();
        item.WarehousePosition.Should().Be("AISLE-7");
        item.Cooling.Should().Be(Cooling.L2);
    }
```

- [ ] **Step 2: Build & run**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~PickingListBatchProcessorTests" \
  --no-build
```
Expected: 3 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs
git commit -m "test: cover PickingListBatchProcessor catalog enrichment"
```

---

## Task 7: New helper test — cooling-marker PATCH success path (FR-4)

Asserts the helper PATCHes once per cooled order and zero times per non-cooled order — the FR-4 isolated test that the spec calls out.

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs` (append test)

- [ ] **Step 1: Append the PATCH success test**

Add the following `[Fact]` to the existing `PickingListBatchProcessorTests` class:

```csharp
    [Fact]
    public async Task FlushAsync_PatchesEachCooledOrderOnce_AndSkipsNonCooled()
    {
        var handler = BuildHandler();
        var catalog = BuildCatalog();
        var processor = BuildProcessor(handler, catalog);

        await processor.FlushAsync(
            new[] { BuildCooledOrder(), BuildNormalOrder() },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: null,
            cancellationToken: CancellationToken.None);

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{CooledOrderCode}/notes"),
            ItExpr.IsAny<CancellationToken>());

        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{NormalOrderCode}/notes"),
            ItExpr.IsAny<CancellationToken>());
    }
```

- [ ] **Step 2: Build & run**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~PickingListBatchProcessorTests" \
  --no-build
```
Expected: 4 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs
git commit -m "test: cover PickingListBatchProcessor cooling-marker PATCH success"
```

---

## Task 8: New helper test — cooling-marker PATCH failure logs warning, flush completes (FR-4 + NFR-5)

Asserts that `HttpRequestException` from PATCH is logged at `Warning` (order code present in formatted message) and `FlushAsync` returns normally.

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs` (append test)

- [ ] **Step 1: Append the PATCH failure test**

Add the following `[Fact]` to the existing `PickingListBatchProcessorTests` class:

```csharp
    [Fact]
    public async Task FlushAsync_PatchFailure_LogsWarning_AndCompletesNormally()
    {
        var logger = new Mock<ILogger<ShoptetApiExpeditionListSource>>();
        var handler = BuildHandler(patchShouldThrow: true);
        var catalog = BuildCatalog();
        var processor = BuildProcessor(handler, catalog, logger: logger.Object);

        var path = await processor.FlushAsync(
            new[] { BuildCooledOrder() },
            BuildMethod(),
            batchIndex: 0,
            timestamp: "20260609_120000",
            onBatchFilesReady: null,
            cancellationToken: CancellationToken.None);

        path.Should().NotBeNullOrEmpty();
        File.Exists(path).Should().BeTrue("PDF must be written even when PATCH fails");

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(CooledOrderCode)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Build & run**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~PickingListBatchProcessorTests" \
  --no-build
```
Expected: 5 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/PickingListBatchProcessorTests.cs
git commit -m "test: cover PickingListBatchProcessor PATCH failure warning log"
```

---

## Task 9: New driver test — batchIndex pass-through across two batches (Risk mitigation)

Forces a mid-loop overflow flush by configuring a low `MaxItems` and feeding enough orders to produce two batches. Asserts both files end with `_0.pdf` and `_1.pdf` — locks in the `batchIndex` increment semantics that the refactor must preserve.

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` (append one test)

- [ ] **Step 1: Inspect `ShippingMethodRegistry` to pick a method that's easy to use**

Read `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs` and `ShippingMethodCatalog.cs`. The existing `ShoptetApiExpeditionListSource_CoolingMarkerTests` uses `ZasilkovnaDoRukyGuid = "f6610d4d-578d-11e9-beb1-002590dad85e"` — confirm the resolved `ShippingMethod` for that GUID and find its `Name` (used in the filename). If the live `MaxItems` makes it hard to force two batches with two orders, the test can either:
- (A) Provide two orders whose combined item count exceeds the configured `MaxItems`, or
- (B) Provide `maxOrders + 1` orders to overflow on the order-count limit.

If `MaxItems` is small enough (e.g. ≤ 2) the test can simply ship two orders with one item each that exceed the limit. Otherwise add multi-item details to each order to force overflow. Choose whichever produces the simpler JSON.

- [ ] **Step 2: Append the test**

The test reuses the existing `BuildSource` helper. It needs a third order's worth of HTTP setup, or repurposes the existing two with more items. Concretely (using approach (A) — two orders, four items each, against a method whose `MaxItems` is below 8):

Add the following helper inside the existing test class (alongside `BuildHandler`):

```csharp
    private static readonly string MultiItemOrderDetailJsonTemplate = $$"""
        {
          "data": {
            "order": {
              "code": "{CODE}",
              "fullName": "Customer {CODE}",
              "phone": "+420000000000",
              "billingAddress": {
                "fullName": "Customer {CODE}",
                "street": "Test",
                "houseNumber": "1",
                "city": "Praha",
                "zip": "10000"
              },
              "items": [
                { "itemType": "product", "itemId": 1, "code": "{{NormalProductCode}}", "name": "X", "amount": 1, "unit": "ks", "itemPriceWithVat": "10.00" },
                { "itemType": "product", "itemId": 2, "code": "{{NormalProductCode}}", "name": "X", "amount": 1, "unit": "ks", "itemPriceWithVat": "10.00" },
                { "itemType": "product", "itemId": 3, "code": "{{NormalProductCode}}", "name": "X", "amount": 1, "unit": "ks", "itemPriceWithVat": "10.00" },
                { "itemType": "product", "itemId": 4, "code": "{{NormalProductCode}}", "name": "X", "amount": 1, "unit": "ks", "itemPriceWithVat": "10.00" }
              ],
              "completion": []
            }
          }
        }
        """;

    private static Mock<HttpMessageHandler> BuildTwoBatchHandler()
    {
        var handler = new Mock<HttpMessageHandler>();

        var twoOrderList = $$"""
            {
              "data": {
                "orders": [
                  {
                    "code": "{{NormalOrderCode}}-A",
                    "status": { "id": -2 },
                    "shipping": { "guid": "{{ZasilkovnaDoRukyGuid}}", "name": "Zásilkovna (do ruky)" },
                    "price": { "withVat": "300.00", "currencyCode": "CZK" }
                  },
                  {
                    "code": "{{NormalOrderCode}}-B",
                    "status": { "id": -2 },
                    "shipping": { "guid": "{{ZasilkovnaDoRukyGuid}}", "name": "Zásilkovna (do ruky)" },
                    "price": { "withVat": "300.00", "currencyCode": "CZK" }
                  }
                ],
                "paginator": { "totalCount": 2, "page": 1, "pageCount": 1 }
              }
            }
            """;

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == "/api/orders" &&
                    r.RequestUri.Query.Contains("statusId")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(twoOrderList));

        foreach (var suffix in new[] { "A", "B" })
        {
            var code = $"{NormalOrderCode}-{suffix}";
            var json = MultiItemOrderDetailJsonTemplate.Replace("{CODE}", code);
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r =>
                        r.Method == HttpMethod.Get &&
                        r.RequestUri!.AbsolutePath == $"/api/orders/{code}"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(OkJson(json));
        }

        return handler;
    }
```

Then add the test:

```csharp
    [Fact]
    public async Task CreatePickingList_MultipleBatches_FilenamesContainSequentialBatchIndex()
    {
        // Two orders with 4 items each. ShippingMethodRegistry maps the Zasilkovna GUID to a method
        // whose MaxItems is below 8, forcing a mid-loop overflow flush. If the production catalog
        // raises that limit above 8, this test will need to ship more items per order to keep forcing
        // the two-batch split; the assertion (_0.pdf and _1.pdf) is what locks in the refactor.
        var handler = BuildTwoBatchHandler();
        var source = BuildSource(handler);

        var result = await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        result.ExportedFiles.Should().HaveCount(2);
        result.ExportedFiles.Should().Contain(p => p.EndsWith("_0.pdf"));
        result.ExportedFiles.Should().Contain(p => p.EndsWith("_1.pdf"));
    }
```

If `ShippingMethodRegistry` returns a method whose `MaxItems` ≥ 8 for the Zasilkovna GUID, raise the item count in `MultiItemOrderDetailJsonTemplate` until the combined item count of two orders exceeds the configured limit. The test is meaningful only if it produces two batches.

- [ ] **Step 2: Build & run**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiExpeditionListSource_CoolingMarkerTests" \
  --no-build
```
Expected: 5 tests pass (the original 4 plus the new one).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs
git commit -m "test: assert batchIndex pass-through across two batches"
```

---

## Task 10: Final validation

**Files:** (none modified)

- [ ] **Step 1: Format**

```bash
dotnet format backend/Anela.Heblo.sln --no-restore --verify-no-changes
```
Expected: exit 0 (no changes needed). If it fails, run without `--verify-no-changes`, then commit the formatting fix:
```bash
git add -u backend/
git commit -m "chore: dotnet format"
```

- [ ] **Step 2: Full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```
Expected: all tests pass.

- [ ] **Step 3: Sanity check — verify the local function is gone**

```bash
grep -n "FlushBatchAsync" backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
```
Expected: no matches.

```bash
grep -n "CoolingMarkerValue\|CoolingAdditionalFieldIndex" backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
```
Expected: no matches (constants now live on `PickingListBatchProcessor`).

- [ ] **Step 4: Sanity check — verify `CreatePickingList` line count**

```bash
awk '/public async Task<PrintPickingListResult> CreatePickingList/,/^    }$/' \
    backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
  | grep -cve '^\s*$' -ve '^\s*//'
```
Expected: ≤ 50 non-blank, non-comment lines. If higher, split the carrier loop body into a private method per FR-3.

- [ ] **Step 5: No commit unless format produced one in Step 1.**

---

## Self-Review

**Spec coverage:**
- FR-1 (preserve observable behavior) — Tasks 4-6 verify all existing tests pass unchanged.
- FR-2 (extract `PickingListBatchProcessor`) — Tasks 2-3 create the helper, Task 4 wires it.
- FR-3 (`CreatePickingList` ≤ 50 lines) — Task 10 Step 4 verifies.
- FR-4 (cooling-marker PATCH semantics) — Task 4 verifies via existing tests, Tasks 7-8 add isolated coverage.
- FR-5 (catalog enrichment testable) — Task 6.
- FR-6 (callback contract) — Task 5.
- FR-7 (logger category stability) — Task 2 fixes the parameter type to `ILogger` with a load-bearing comment; Task 4 passes `_logger` through.
- NFR-1 (code quality) — Task 10 Step 1 runs `dotnet format --verify-no-changes`.
- NFR-2 (testability via `InternalsVisibleTo`) — verified: `ShoptetApiExpeditionListSource.cs:13` already declares `InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")`.
- NFR-3 (backward compatibility) — no DI registration changes; public surface untouched.
- NFR-4 (no perf regression) — same work, same order; one extra allocation per call.
- NFR-5 (log template exact) — Task 3 copies the template verbatim; Task 8 asserts the order code appears in the formatted message.

**Placeholder scan:** every code step contains complete C# code or a complete shell command. No "TODO", "TBD", or "similar to Task N" references.

**Type consistency:** `FlushAsync(IReadOnlyList<ExpeditionOrder>, ShippingMethod, int, string, Func<IList<string>, Task>?, CancellationToken) -> Task<string>` is used identically in Tasks 2, 3, 4, 5, 6, 7, 8. Constructor signature `(ICatalogRepository, ShoptetOrderClient, Func<ExpeditionProtocolData, byte[]>, ILogger)` is used identically in Tasks 2, 3, 5. Constants `CoolingMarkerValue` and `CoolingAdditionalFieldIndex` are removed from source (Task 4 Step 1) and exist only on the helper (Task 2 Step 1).

Plan complete.
