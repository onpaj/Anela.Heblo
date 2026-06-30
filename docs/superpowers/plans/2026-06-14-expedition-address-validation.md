# Expedition Robot — Address Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** During picking-list creation, detect home-delivery orders with incomplete delivery addresses, skip them (don't print), append a remark explaining what's missing, and move them to the "poznámka" Shoptet state (id 35).

**Architecture:** A pure validator inspects the structured `ExpeditionAddress` inside the Shoptet adapter's picking loop (`ShoptetApiExpeditionListSource.BatchAndFlushAsync`). Validation runs **only for home-delivery ("na ruky") methods** (`ResolveDeliveryHandling(method) == NaRuky`). Invalid orders are flagged best-effort (status change first, remark append second — mirroring `BlockOrderProcessingHandler`) and excluded from PDF batches and from the "Balí se" transition. A new `NoteStateId` config flows from `PrintPickingListOptions` through the request chain, exactly like the existing `DesiredStateId`. A `SkippedCount` is surfaced on the result/response.

**Tech Stack:** .NET 8, MediatR, xUnit + FluentAssertions + Moq, Shoptet REST API adapter, QuestPDF.

---

## Context

Some orders have incomplete delivery addresses. Today they print, get picked, and then the
carrier API (called later, at shipment-label time, via Shoptet) rejects them — wasted picking
work and stuck orders. We catch this earlier, at picking-list creation.

**Confirmed decisions:**
- Poznámka state id = **35** (config `ExpeditionList:NoteStateId`, default 35).
- Required fields: recipient name (`fullName` **or** `company`) + `street` + `houseNumber` + `city` + `zip`.
- Validate **only home-delivery ("na ruky")** methods; pickup-point/box methods are exempt.

## File Structure

**Create:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionAddressValidator.cs` — pure missing-field detector.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ExpeditionAddressValidatorTests.cs` — validator unit tests.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_AddressValidationTests.cs` — end-to-end adapter behaviour tests.

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — add `NoteStateId`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs` — add `NoteStateId`.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — add `NoteStateId`.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — map `NoteStateId` + `SkippedCount`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs` — set `NoteStateId`, surface `SkippedCount`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixResponse.cs` — add `SkippedCount`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs` — set `NoteStateId`, log `SkippedCount`.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` — add `SkippedCount`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingResult.cs` — add `SkippedCount`.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — validation + flagging in the picking loop.
- `backend/src/Anela.Heblo.API/appsettings.json` — add `NoteStateId` to the `ExpeditionList` block.

**Commands run from `backend/`.** Test project for the adapter: `Anela.Heblo.Adapters.Shoptet.Tests`.

---

## Task 1: Address validator (pure function)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionAddressValidator.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ExpeditionAddressValidatorTests.cs`

> `ExpeditionAddress` is a public model in `Anela.Heblo.Adapters.ShoptetApi.Expedition.Model`. The test assembly already has `InternalsVisibleTo` access (declared in `ShoptetApiExpeditionListSource.cs`), so an `internal` validator is fine.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ExpeditionAddressValidatorTests.cs`:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class ExpeditionAddressValidatorTests
{
    private static ExpeditionAddress Complete() => new()
    {
        FullName = "Jan Novák",
        Street = "Hlavní",
        HouseNumber = "12",
        City = "Praha",
        Zip = "11000",
    };

    [Fact]
    public void GetMissingFields_CompleteAddress_ReturnsEmpty()
    {
        ExpeditionAddressValidator.GetMissingFields(Complete()).Should().BeEmpty();
    }

    [Fact]
    public void GetMissingFields_NullAddress_ReturnsAllFiveFields()
    {
        ExpeditionAddressValidator.GetMissingFields(null)
            .Should().BeEquivalentTo("jméno příjemce", "ulice", "číslo popisné", "město", "PSČ");
    }

    [Fact]
    public void GetMissingFields_BlankZip_ReturnsZipOnly()
    {
        var addr = Complete();
        addr.Zip = "   ";
        ExpeditionAddressValidator.GetMissingFields(addr)
            .Should().ContainSingle().Which.Should().Be("PSČ");
    }

    [Fact]
    public void GetMissingFields_CompanyOnlyName_IsValid()
    {
        var addr = Complete();
        addr.FullName = null;
        addr.Company = "Anela s.r.o.";
        ExpeditionAddressValidator.GetMissingFields(addr).Should().BeEmpty();
    }

    [Fact]
    public void GetMissingFields_MissingStreetAndCity_ReturnsBoth()
    {
        var addr = Complete();
        addr.Street = null;
        addr.City = "";
        ExpeditionAddressValidator.GetMissingFields(addr)
            .Should().BeEquivalentTo("ulice", "město");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests --filter "FullyQualifiedName~ExpeditionAddressValidatorTests"`
Expected: **build failure / FAIL** — `ExpeditionAddressValidator` does not exist.

- [ ] **Step 3: Implement the validator**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionAddressValidator.cs`:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

/// <summary>
/// Validates that a delivery address carries every field a carrier requires.
/// Returns the Czech labels of all missing fields; an empty list means the address is complete.
/// </summary>
internal static class ExpeditionAddressValidator
{
    public static IReadOnlyList<string> GetMissingFields(ExpeditionAddress? address)
    {
        var missing = new List<string>();

        var hasName = !string.IsNullOrWhiteSpace(address?.FullName)
                      || !string.IsNullOrWhiteSpace(address?.Company);
        if (!hasName)
            missing.Add("jméno příjemce");
        if (string.IsNullOrWhiteSpace(address?.Street))
            missing.Add("ulice");
        if (string.IsNullOrWhiteSpace(address?.HouseNumber))
            missing.Add("číslo popisné");
        if (string.IsNullOrWhiteSpace(address?.City))
            missing.Add("město");
        if (string.IsNullOrWhiteSpace(address?.Zip))
            missing.Add("PSČ");

        return missing;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests --filter "FullyQualifiedName~ExpeditionAddressValidatorTests"`
Expected: **PASS** (5 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionAddressValidator.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ExpeditionAddressValidatorTests.cs
git commit -m "feat: add expedition delivery address validator"
```

---

## Task 2: Thread `NoteStateId` config through the request chain

No new behaviour yet — this wiring makes the value reach the adapter (mirrors `DesiredStateId`). Verified by build.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add `NoteStateId` to `PrintPickingListOptions`**

In `PrintPickingListOptions.cs`, add after `DesiredStateId` (line 12):

```csharp
    public int DesiredStateId { get; set; } = 26;
    public int NoteStateId { get; set; } = 35; // Poznamka — orders with incomplete address
```

- [ ] **Step 2: Add `NoteStateId` to `ExpeditionPickingRequest`**

In `ExpeditionPickingRequest.cs`, add the constant and property:

```csharp
    public const int DefaultSourceStateId = -2;
    public const int DefaultDesiredStateId = 26;
    public const int DefaultNoteStateId = 35;

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public int NoteStateId { get; set; } = DefaultNoteStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }
```

- [ ] **Step 3: Add `NoteStateId` to `PrintPickingListRequest`**

In `PrintPickingListRequest.cs`:

```csharp
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    public const int DefaultDesiredStateId = 26; // Bali se
    public const int DefaultNoteStateId = 35; // Poznamka

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public int NoteStateId { get; set; } = DefaultNoteStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }
```

- [ ] **Step 4: Map `NoteStateId` in `LogisticsExpeditionPickingAdapter`**

In `LogisticsExpeditionPickingAdapter.cs`, add to the `innerRequest` initializer:

```csharp
        var innerRequest = new PrintPickingListRequest
        {
            Carriers = request.Carriers,
            SourceStateId = request.SourceStateId,
            DesiredStateId = request.DesiredStateId,
            NoteStateId = request.NoteStateId,
            ChangeOrderState = request.ChangeOrderState,
            SendToPrinter = request.SendToPrinter,
        };
```

- [ ] **Step 5: Set `NoteStateId` from options in the two construction sites**

In `RunExpeditionListPrintFixHandler.cs`, add to the `ExpeditionPickingRequest` initializer:

```csharp
            DesiredStateId = _options.Value.DesiredStateId,
            NoteStateId = _options.Value.NoteStateId,
            ChangeOrderState = _options.Value.ChangeOrderStateByDefault,
```

In `PrintPickingListJob.cs`, add to the `ExpeditionPickingRequest` initializer:

```csharp
                DesiredStateId = _options.Value.DesiredStateId,
                NoteStateId = _options.Value.NoteStateId,
                ChangeOrderState = _options.Value.ChangeOrderStateByDefault,
```

- [ ] **Step 6: Add `NoteStateId` to appsettings**

In `backend/src/Anela.Heblo.API/appsettings.json`, in the `ExpeditionList` block, add the line after `DesiredStateId`:

```json
    "DesiredStateId": 26, // Bali se
    "NoteStateId": 35, // Poznamka (neuplna adresa)
```

- [ ] **Step 7: Build**

Run: `cd backend && dotnet build`
Expected: **succeeds**.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList \
        backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: add NoteStateId config for expedition address validation"
```

---

## Task 3: Add `SkippedCount` to results and response

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingResult.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs`

- [ ] **Step 1: Add `SkippedCount` to `PrintPickingListResult`**

```csharp
public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
    public int SkippedCount { get; set; }
}
```

- [ ] **Step 2: Add `SkippedCount` to `ExpeditionPickingResult`**

```csharp
public class ExpeditionPickingResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
    public int SkippedCount { get; set; }
}
```

- [ ] **Step 3: Map `SkippedCount` in `LogisticsExpeditionPickingAdapter`**

In the returned `ExpeditionPickingResult`:

```csharp
        return new ExpeditionPickingResult
        {
            ExportedFiles = inner.ExportedFiles,
            TotalCount = inner.TotalCount,
            SkippedCount = inner.SkippedCount,
        };
```

- [ ] **Step 4: Add `SkippedCount` to the response**

In `RunExpeditionListPrintFixResponse.cs`:

```csharp
public class RunExpeditionListPrintFixResponse : BaseResponse
{
    public int TotalCount { get; set; }
    public int SkippedCount { get; set; }
}
```

- [ ] **Step 5: Set `SkippedCount` in the handler**

In `RunExpeditionListPrintFixHandler.cs`:

```csharp
        return new RunExpeditionListPrintFixResponse
        {
            TotalCount = result.TotalCount,
            SkippedCount = result.SkippedCount,
        };
```

- [ ] **Step 6: Log `SkippedCount` in the job**

In `PrintPickingListJob.cs`, replace the completion log line:

```csharp
            _logger.LogInformation(
                "{JobName} completed. Total orders: {TotalCount}, skipped (incomplete address): {SkippedCount}",
                Metadata.JobName, result.TotalCount, result.SkippedCount);
```

- [ ] **Step 7: Build**

Run: `cd backend && dotnet build`
Expected: **succeeds**.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingResult.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix
git commit -m "feat: surface skipped-order count from expedition picking"
```

---

## Task 4: Validate & flag incomplete addresses in the picking loop

This is the behavioural core. Write the adapter test first, watch it fail, then implement.

**Files:**
- Test: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_AddressValidationTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`

- [ ] **Step 1: Write the failing behaviour tests**

Create `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_AddressValidationTests.cs`:

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Application.Features.Logistics.Picking;
using Anela.Heblo.Application.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Expedition;

public class ShoptetApiExpeditionListSource_AddressValidationTests
{
    private const string DoRukyGuid = "f6610d4d-578d-11e9-beb1-002590dad85e"; // ZASILKOVNA_DO_RUKY (NaRuky)
    private const string ZPointGuid = "7878c138-578d-11e9-beb1-002590dad85e"; // ZASILKOVNA_ZPOINT (Box)
    private const string NoteStateId = "35";

    private const string BadRuky = "BAD-RUKY";   // home delivery, missing zip -> skip
    private const string GoodRuky = "GOOD-RUKY"; // home delivery, complete -> print
    private const string BoxBad = "BOX-BAD";     // box, incomplete -> still print (exempt)

    private static HttpResponseMessage OkJson(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly string OrderListJson = $$"""
        {
          "data": {
            "orders": [
              { "code": "{{BadRuky}}",  "status": { "id": -2 }, "shipping": { "guid": "{{DoRukyGuid}}" }, "price": { "withVat": "300.00", "currencyCode": "CZK" } },
              { "code": "{{GoodRuky}}", "status": { "id": -2 }, "shipping": { "guid": "{{DoRukyGuid}}" }, "price": { "withVat": "300.00", "currencyCode": "CZK" } },
              { "code": "{{BoxBad}}",   "status": { "id": -2 }, "shipping": { "guid": "{{ZPointGuid}}" }, "price": { "withVat": "300.00", "currencyCode": "CZK" } }
            ],
            "paginator": { "totalCount": 3, "page": 1, "pageCount": 1 }
          }
        }
        """;

    private static string OrderDetail(string code, string? street, string? houseNumber, string? city, string? zip) => $$"""
        {
          "data": {
            "order": {
              "code": "{{code}}",
              "fullName": "Customer {{code}}",
              "phone": "+420000000000",
              "deliveryAddress": {
                "fullName": "Customer {{code}}",
                "street": {{(street is null ? "null" : $"\"{street}\"")}},
                "houseNumber": {{(houseNumber is null ? "null" : $"\"{houseNumber}\"")}},
                "city": {{(city is null ? "null" : $"\"{city}\"")}},
                "zip": {{(zip is null ? "null" : $"\"{zip}\"")}}
              },
              "items": [
                { "itemType": "product", "itemId": 1, "code": "P1", "name": "Item", "amount": 1, "unit": "ks", "itemPriceWithVat": "10.00" }
              ],
              "completion": []
            }
          }
        }
        """;

    private static void SetupDetail(Mock<HttpMessageHandler> handler, string code, string json) =>
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == $"/api/orders/{code}"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(json));

    private Mock<HttpMessageHandler> BuildHandler()
    {
        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == "/api/orders" &&
                    r.RequestUri.Query.Contains("statusId")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson(OrderListJson));

        SetupDetail(handler, BadRuky, OrderDetail(BadRuky, "Hlavní", "1", "Praha", null));
        SetupDetail(handler, GoodRuky, OrderDetail(GoodRuky, "Hlavní", "2", "Praha", "11000"));
        SetupDetail(handler, BoxBad, OrderDetail(BoxBad, null, null, null, null));

        // Any PATCH (status / notes) succeeds.
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Patch),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkJson("""{"data":null,"errors":null}"""));

        return handler;
    }

    private ShoptetApiExpeditionListSource BuildSource(
        Mock<HttpMessageHandler> handler, List<ExpeditionProtocolData> captured)
    {
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        var client = new ShoptetOrderClient(http, Options.Create(new ShoptetOrdersSettings()));

        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate { ProductCode = "P1", ProductName = "Item" });

        var carrierCooling = new Mock<ICarrierCoolingRepository>();
        carrierCooling.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>());

        var giftSettings = new Mock<IGiftSettingRepository>();
        giftSettings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GiftSetting.CreateDefault());

        return new ShoptetApiExpeditionListSource(
            client, TimeProvider.System, catalog.Object, carrierCooling.Object, giftSettings.Object,
            Mock.Of<ILogger<ShoptetApiExpeditionListSource>>(),
            data => { captured.Add(data); return new byte[] { 0x25, 0x50, 0x44, 0x46 }; });
    }

    private static PrintPickingListRequest BuildRequest() => new()
    {
        SourceStateId = -2,
        DesiredStateId = 26,
        NoteStateId = 35,
        ChangeOrderState = false,
        Carriers = [],
    };

    [Fact]
    public async Task IncompleteHomeDeliveryOrder_IsSkippedAndFlagged()
    {
        var handler = BuildHandler();
        var captured = new List<ExpeditionProtocolData>();
        var source = BuildSource(handler, captured);

        var result = await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        // Skipped: not in any PDF, not counted as processed.
        var printedCodes = captured.SelectMany(d => d.Orders).Select(o => o.Code).ToList();
        printedCodes.Should().NotContain(BadRuky);
        printedCodes.Should().Contain(GoodRuky);
        result.TotalCount.Should().Be(2);   // GoodRuky + BoxBad
        result.SkippedCount.Should().Be(1); // BadRuky

        // Moved to note state 35 and remark appended.
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{BadRuky}/status"),
            ItExpr.IsAny<CancellationToken>());
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{BadRuky}/notes"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task IncompleteBoxOrder_IsNotValidated_AndStillPrinted()
    {
        var handler = BuildHandler();
        var captured = new List<ExpeditionProtocolData>();
        var source = BuildSource(handler, captured);

        var result = await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        captured.SelectMany(d => d.Orders).Select(o => o.Code).Should().Contain(BoxBad);
        handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{BoxBad}/status"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task CompleteHomeDeliveryOrder_IsNotFlagged()
    {
        var handler = BuildHandler();
        var captured = new List<ExpeditionProtocolData>();
        var source = BuildSource(handler, captured);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath == $"/api/orders/{GoodRuky}/status"),
            ItExpr.IsAny<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests --filter "FullyQualifiedName~ShoptetApiExpeditionListSource_AddressValidationTests"`
Expected: **FAIL** — `BadRuky` is currently printed and counted (no validation yet); `SkippedCount` is 0; no status PATCH issued.

- [ ] **Step 3: Implement validation in `CreatePickingList`**

In `ShoptetApiExpeditionListSource.cs`, update `CreatePickingList` to track skipped codes and pass `NoteStateId`. Replace the body from the `processedCodes` declaration through the `return`:

```csharp
        var exportedFiles = new List<string>();
        var processedCodes = new List<string>();
        var skippedCodes = new List<string>();
        var timestamp = _timeProvider.GetFilenameTimestamp();

        var allSettings = await _carrierCooling.GetAllAsync(cancellationToken);
        var coolingMatrix = allSettings.ToDictionary(
            s => (s.Carrier, s.DeliveryHandling),
            s => s.Cooling);
        var coolingTextMatrix = allSettings.ToDictionary(
            s => (s.Carrier, s.DeliveryHandling),
            s => s.CoolingText);

        var giftSetting = await _giftSettings.GetAsync(cancellationToken);
        var processor = new PickingListBatchProcessor(_catalog, _client, _generateDocument, _logger);

        foreach (var (method, orders) in ordersByMethod)
            await BatchAndFlushAsync(method, orders, coolingMatrix, coolingTextMatrix,
                giftSetting, processor, timestamp, request.NoteStateId, exportedFiles, processedCodes,
                skippedCodes, onBatchFilesReady, cancellationToken);

        if (request.ChangeOrderState)
        {
            foreach (var code in processedCodes)
                await _client.UpdateStatusAsync(code, request.DesiredStateId, cancellationToken);
        }

        return new PrintPickingListResult
        {
            ExportedFiles = exportedFiles,
            TotalCount = processedCodes.Count,
            SkippedCount = skippedCodes.Count,
        };
```

- [ ] **Step 4: Update `BatchAndFlushAsync` signature and per-order loop**

Change the `BatchAndFlushAsync` signature to add `int noteStateId` (after `timestamp`) and `List<string> skippedCodes` (after `processedCodes`):

```csharp
    private async Task BatchAndFlushAsync(
        ShippingMethod method,
        List<(string Code, string ShippingGuid, decimal? TotalWithVat, string? CurrencyCode)> orders,
        IReadOnlyDictionary<(Carriers, DeliveryHandling), Cooling> coolingMatrix,
        IReadOnlyDictionary<(Carriers, DeliveryHandling), string?> coolingTextMatrix,
        GiftSetting giftSetting,
        PickingListBatchProcessor processor,
        string timestamp,
        int noteStateId,
        List<string> exportedFiles,
        List<string> processedCodes,
        List<string> skippedCodes,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken)
    {
        var validateAddress = ShippingMethodRegistry.ResolveDeliveryHandling(method) == DeliveryHandling.NaRuky;

        var allExpeditionOrders = new List<ExpeditionOrder>();
        foreach (var (code, shippingGuid, totalWithVat, currencyCode) in orders)
        {
            var detail = await _client.GetExpeditionOrderDetailAsync(code, cancellationToken);

            if (validateAddress)
            {
                var missing = ExpeditionAddressValidator.GetMissingFields(detail.DeliveryAddress ?? detail.BillingAddress);
                if (missing.Count > 0)
                {
                    await FlagIncompleteAddressAsync(code, missing, noteStateId, cancellationToken);
                    skippedCodes.Add(code);
                    continue;
                }
            }

            var expeditionOrder = MapToExpeditionOrder(detail);
            expeditionOrder.CarrierCooling = ResolveCarrierCooling(shippingGuid, coolingMatrix);
            expeditionOrder.CoolingText = ResolveCarrierCoolingText(shippingGuid, coolingTextMatrix);
            expeditionOrder.GiftBadgeText = ResolveGiftBadge(totalWithVat, currencyCode, giftSetting);
            allExpeditionOrders.Add(expeditionOrder);
            processedCodes.Add(code);
        }
```

(Leave the batching/flush block below this loop unchanged.)

- [ ] **Step 5: Add the `FlagIncompleteAddressAsync` helper**

Add this private method to `ShoptetApiExpeditionListSource` (e.g. directly after `BatchAndFlushAsync`):

```csharp
    // Best-effort flagging, mirroring BlockOrderProcessingHandler: move the order out of the
    // source state first (so it leaves the picking queue even if the note write fails), then
    // append a remark naming the missing fields. A Shoptet failure must never abort the run.
    private async Task FlagIncompleteAddressAsync(
        string code,
        IReadOnlyList<string> missingFields,
        int noteStateId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.UpdateStatusAsync(code, noteStateId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to move order {Code} with incomplete address to note state {NoteStateId}",
                code, noteStateId);
        }

        try
        {
            var note = $"Robot expedice: neúplná adresa – chybí: {string.Join(", ", missingFields)}.";
            var current = await _client.GetEshopRemarkAsync(code, cancellationToken);
            var updated = string.IsNullOrEmpty(current) ? note : $"{current}\n{note}";
            await _client.UpdateEshopRemarkAsync(code, updated, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Order {Code} flagged with incomplete address but the note could not be appended", code);
        }
    }
```

- [ ] **Step 6: Run the new behaviour tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests --filter "FullyQualifiedName~ShoptetApiExpeditionListSource_AddressValidationTests"`
Expected: **PASS** (3 tests).

- [ ] **Step 7: Run the full adapter test project (regression check)**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests`
Expected: **PASS** — existing `ShoptetApiExpeditionListSource_CoolingMarkerTests` still green (their orders use `billingAddress` only with complete data; cooling order remains valid; note: those tests use the DO_RUKY GUID and complete addresses, so none are skipped).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_AddressValidationTests.cs
git commit -m "feat: skip and flag incomplete-address orders during expedition picking"
```

---

## Task 5: Final validation

- [ ] **Step 1: Build + format**

Run: `cd backend && dotnet build && dotnet format`
Expected: build succeeds, formatter makes no/minimal changes.

- [ ] **Step 2: Run the broader expedition test suite**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~Expedition"`
Expected: **PASS**.

- [ ] **Step 3: Commit any formatter changes**

```bash
git add -A && git commit -m "chore: dotnet format" || echo "nothing to format"
```

---

## Self-Review Notes (for the implementer)

- **Existing cooling tests use `billingAddress` only.** Those orders ship via DO_RUKY (NaRuky) and now pass through validation. Their billing addresses are complete (`street`, `houseNumber`, `city`, `zip`, `fullName` all set) and `MapToExpeditionOrder` already falls back `DeliveryAddress ?? BillingAddress`, so the validator (same fallback) sees a complete address → **not skipped**. No change needed to those tests. Confirm in Task 4 Step 7.
- **`GetEshopRemarkAsync` returns `string.Empty` (never null)** when there are no notes, so `string.IsNullOrEmpty(current)` is the correct guard.
- **Status-before-remark ordering** is deliberate: it removes the order from the source-status query even if the remark write fails. A repeated status-write failure can append a duplicate remark on a later run — acceptable, documented in code comment.
- **Box/pickup exemption** is driven entirely by `ResolveDeliveryHandling(method)`; methods whose `Name` lacks `DO_RUKY` (and the Osobák `OSOBAK`, which resolves to `null`) are never validated.
- **`Carriers.Osobak` (`OSOBAK`)** resolves to `null` handling, not `NaRuky`, so personal pickup is exempt — correct (no carrier address needed).
```
