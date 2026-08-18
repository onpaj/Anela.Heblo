# ShoptetApiInvoiceSource Unit Test Coverage Implementation Plan

**Goal:** Add a deterministic unit test suite for `ShoptetApiInvoiceSource.GetAllAsync` that mocks `IShoptetInvoiceClient` to cover the single-invoice fetch branch (including its null sub-case), the case-insensitive in-memory currency filter, and the null-detail guard in the per-code detail-fetch loop — closing the three coverage gaps flagged against a class that currently only has an inert, credential-gated integration test.

**Architecture:** One new test file, `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`, is added alongside the existing `Integration/ShoptetApiInvoiceSourceIntegrationTests.cs` for the same class. It mocks the only external collaborator (`IShoptetInvoiceClient`) with Moq using exact-argument, per-code setups, uses a **real** `ShoptetInvoiceMapper` (built the same way `ShoptetInvoiceMapperTests` does) so the tests prove mapping actually runs, and uses a no-op `ILogger<ShoptetApiInvoiceSource>`. No production code changes. This is a coverage-closing addition against source code already confirmed correct (per spec/arch-review) — every new test is expected to **pass** on first run, not fail; there is no missing implementation to drive out via red-green.

**Tech Stack:** .NET 8, xUnit (`[Fact]`/`[Theory]`), Moq, FluentAssertions — all already referenced by `Anela.Heblo.Adapters.Shoptet.Tests.csproj`. No new NuGet packages or csproj changes.

---

## Reference material (for every task below)

**Class under test** — `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/ShoptetApiInvoiceSource.cs`:
```csharp
namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;

public class ShoptetApiInvoiceSource : IIssuedInvoiceSource
{
    private readonly IShoptetInvoiceClient _client;
    private readonly ShoptetInvoiceMapper _mapper;
    private readonly ILogger<ShoptetApiInvoiceSource> _logger;

    public ShoptetApiInvoiceSource(
        IShoptetInvoiceClient client,
        ShoptetInvoiceMapper mapper,
        ILogger<ShoptetApiInvoiceSource> logger)
    { ... }

    public async Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.QueryByInvoice)
        {
            var single = await _client.GetInvoiceAsync(query.InvoiceId!, cancellationToken);
            var invoices = single != null ? new[] { single } : Array.Empty<ShoptetInvoiceDto>();
            var details = invoices.Select(i => _mapper.Map(i)).ToList();
            return new List<IssuedInvoiceDetailBatch>
            {
                new IssuedInvoiceDetailBatch { BatchId = query.RequestId, Invoices = details },
            };
        }

        var listItems = await _client.ListInvoicesAsync(query.DateFrom, query.DateTo, cancellationToken);
        var total = listItems.Count;

        var matchingCodes = listItems
            .Where(i => string.Equals(
                i.Price?.CurrencyCode,
                query.Currency,
                StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Code)
            .ToList();

        _logger.LogInformation(/* ... */);

        var detailDtos = new List<ShoptetInvoiceDto>(matchingCodes.Count);
        foreach (var code in matchingCodes)
        {
            var detail = await _client.GetInvoiceAsync(code, cancellationToken);
            if (detail != null)
                detailDtos.Add(detail);
        }

        var batch = new IssuedInvoiceDetailBatch
        {
            BatchId = query.RequestId,
            Invoices = detailDtos.Select(i => _mapper.Map(i)).ToList(),
        };

        return new List<IssuedInvoiceDetailBatch> { batch };
    }

    public Task CommitAsync(IssuedInvoiceDetailBatch batch, string? commitMessage = default) => Task.CompletedTask;
    public Task FailAsync(IssuedInvoiceDetailBatch batch, string? errorMessage = default) => Task.CompletedTask;
}
```

**`IShoptetInvoiceClient`** — `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/IShoptetInvoiceClient.cs`:
```csharp
namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;

public interface IShoptetInvoiceClient
{
    Task<IReadOnlyList<ShoptetInvoiceDto>> ListInvoicesAsync(DateTime? dateFrom, DateTime? dateTo, CancellationToken ct = default);
    Task<ShoptetInvoiceDto?> GetInvoiceAsync(string code, CancellationToken ct = default);
    Task<string> GetInvoiceRawJsonAsync(string code, CancellationToken ct = default);
}
```

**Critical mapping detail** — `ShoptetInvoiceMapper.Map` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Mapping/ShoptetInvoiceMapper.cs`) **inverts** `Code`/`OrderCode`:
```csharp
Code = src.OrderCode ?? string.Empty,   // mapped Code comes from DTO.OrderCode
OrderCode = src.Code,                   // mapped OrderCode comes from DTO.Code
```
Every test below asserts on the mapped result's `OrderCode` (equal to the input DTO's `Code`) — never on `Code` — so that a future Code/OrderCode regression in the mapper would fail these tests loudly.

**Domain types used** (`backend/src/Anela.Heblo.Domain/Features/Invoices/`):
```csharp
public class IssuedInvoiceSourceQuery
{
    public string RequestId { get; set; } = "undefined";
    public string? InvoiceId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string Currency { get; set; } = "CZK";
    public bool QueryByInvoice => InvoiceId != null;
}

public class IssuedInvoiceDetailBatch
{
    public List<IssuedInvoiceDetail> Invoices { get; set; } = new();
    public string BatchId { get; set; } = string.Empty;
}

public class IssuedInvoiceDetail
{
    public string Code { get; set; } = string.Empty;
    public string? OrderCode { get; set; }
    // ...plus CreationTime, DueDate, TaxDate, BillingMethod, ShippingMethod, VatPayer,
    // Items, BillingAddress, DeliveryAddress, Price, Customer (all irrelevant to these tests)
}
```

**Shoptet DTOs** (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Model/`):
```csharp
public class ShoptetInvoiceDto
{
    public string Code { get; set; } = null!;
    public string? OrderCode { get; set; }
    // ...VarSymbol, CreationTime, DueDate, TaxDate, Shipping, BillingMethod, BillingAddress, DeliveryAddress
    public List<ShoptetInvoiceItemDto> Items { get; set; } = new();
    public ShoptetInvoicePriceDto? Price { get; set; }
}

public class ShoptetInvoicePriceDto
{
    public string? WithVat { get; set; }
    public string? WithoutVat { get; set; }
    public string? ToPay { get; set; }
    public string? Vat { get; set; }
    public string? CurrencyCode { get; set; }
    public string? ExchangeRate { get; set; }
}
```

**Mapper's own dependencies** (already default-constructible, no mocking needed):
- `BillingMethodMapper` — `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Mapping/BillingMethodMapper.cs`, namespace `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping`. Has a parameterless constructor (delegates to `NullLogger<BillingMethodMapper>.Instance`).
- `ShippingMethodMapper` — same namespace, constructor takes `IOptions<ShoptetApiSettings>`.
- `ShoptetApiSettings` — `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs`, namespace `Anela.Heblo.Adapters.ShoptetApi.Orders`.

**Existing sibling-file conventions confirmed in this repo** (`backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/*.cs`): xUnit `[Fact]`/`[Theory]`, Moq `Mock<T>`, FluentAssertions `.Should()`, private `static` `Build*` helper methods, per-code exact-argument `Setup`/`Verify` with `It.IsAny<CancellationToken>()` always spelled out explicitly (never relying on the interface's default `CancellationToken ct = default` to auto-match in Moq).

**Test project** — `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj` already references `Anela.Heblo.Adapters.ShoptetApi.csproj` and already has `Moq`, `FluentAssertions`, `xunit`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging` as package references, plus a global `<Using Include="Xunit" />`. **No csproj changes are needed in any task below.**

**Repo root** (all commands below are run from here): `/home/user/worktrees/feature-3939-Coverage-Gap-Adapters-Shoptetapiinvoicesource-Quer`. Solution file: `Anela.Heblo.sln`.

---

### task: scaffold-invoice-source-test-file

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`

This task creates the new test file with its `Build*` helper methods and the first scenario (FR-1: single-invoice fetch hit).

- [ ] **Step 1: Write the test file**

Create `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` with exactly this content:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetApiInvoiceSourceTests
{
    private static ShoptetInvoiceMapper BuildMapper() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    private static ShoptetApiInvoiceSource BuildSource(Mock<IShoptetInvoiceClient> client) =>
        new(client.Object, BuildMapper(), Mock.Of<ILogger<ShoptetApiInvoiceSource>>());

    private static ShoptetInvoiceDto BuildDto(string code, string? orderCode = null, string currency = "CZK") =>
        new()
        {
            Code = code,
            OrderCode = orderCode ?? $"ORD-{code}",
            Items = new List<ShoptetInvoiceItemDto>(),
            Price = new ShoptetInvoicePriceDto { CurrencyCode = currency, WithVat = "0", WithoutVat = "0" },
        };

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice()
    {
        // Arrange
        var dto = BuildDto("INV-1", orderCode: "ORD-1");
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-1",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-1");
        batch.Invoices.Should().HaveCount(1);
        // ShoptetInvoiceMapper.Map swaps Code/OrderCode: mapped.OrderCode = src.Code.
        // Asserting on OrderCode (not Code) proves the real mapper ran on this exact DTO.
        batch.Invoices[0].OrderCode.Should().Be("INV-1");

        client.Verify(
            x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests.GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice"
```
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`. This test exercises production code that already implements FR-1 correctly (per spec/arch-review, this is a coverage-only addition, not new implementation) — it must **pass** on this first run. If it fails, stop and diagnose before proceeding (do not paper over a real failure by weakening the assertion).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs
git commit -m "test: add ShoptetApiInvoiceSource single-invoice-fetch-found coverage (FR-1)"
```

---

### task: add-single-fetch-null-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`

Adds FR-2: single-invoice mode when the client returns `null` for the requested ID.

- [ ] **Step 1: Add the new test**

Replace the full content of `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` with exactly this (adds one new `[Fact]` method after the FR-1 test; everything else is unchanged from the previous task):

```csharp
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetApiInvoiceSourceTests
{
    private static ShoptetInvoiceMapper BuildMapper() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    private static ShoptetApiInvoiceSource BuildSource(Mock<IShoptetInvoiceClient> client) =>
        new(client.Object, BuildMapper(), Mock.Of<ILogger<ShoptetApiInvoiceSource>>());

    private static ShoptetInvoiceDto BuildDto(string code, string? orderCode = null, string currency = "CZK") =>
        new()
        {
            Code = code,
            OrderCode = orderCode ?? $"ORD-{code}",
            Items = new List<ShoptetInvoiceItemDto>(),
            Price = new ShoptetInvoicePriceDto { CurrencyCode = currency, WithVat = "0", WithoutVat = "0" },
        };

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice()
    {
        // Arrange
        var dto = BuildDto("INV-1", orderCode: "ORD-1");
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-1",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-1");
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("INV-1");

        client.Verify(
            x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList()
    {
        // Arrange
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoptetInvoiceDto?)null);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-2",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert — GetAllAsync must not throw (proven by the successful await above) and must return
        // an empty, non-null Invoices list rather than null.
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-2");
        batch.Invoices.Should().NotBeNull();
        batch.Invoices.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the new test to verify it passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests.GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList"
```
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 3: Run the whole new test class to confirm no regression**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"
```
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs
git commit -m "test: add ShoptetApiInvoiceSource single-invoice-fetch-null coverage (FR-2)"
```

---

### task: add-currency-filter-exclude-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`

Adds FR-3: list-mode currency filter excludes a non-matching-currency summary from both the detail-fetch calls and the result.

- [ ] **Step 1: Add the new test**

Replace the full content of `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` with exactly this (adds one new `[Fact]` method after the FR-2 test; FR-1/FR-2 are unchanged):

```csharp
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetApiInvoiceSourceTests
{
    private static ShoptetInvoiceMapper BuildMapper() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    private static ShoptetApiInvoiceSource BuildSource(Mock<IShoptetInvoiceClient> client) =>
        new(client.Object, BuildMapper(), Mock.Of<ILogger<ShoptetApiInvoiceSource>>());

    private static ShoptetInvoiceDto BuildDto(string code, string? orderCode = null, string currency = "CZK") =>
        new()
        {
            Code = code,
            OrderCode = orderCode ?? $"ORD-{code}",
            Items = new List<ShoptetInvoiceItemDto>(),
            Price = new ShoptetInvoicePriceDto { CurrencyCode = currency, WithVat = "0", WithoutVat = "0" },
        };

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice()
    {
        // Arrange
        var dto = BuildDto("INV-1", orderCode: "ORD-1");
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-1",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-1");
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("INV-1");

        client.Verify(
            x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList()
    {
        // Arrange
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoptetInvoiceDto?)null);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-2",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-2");
        batch.Invoices.Should().NotBeNull();
        batch.Invoices.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ListModeCurrencyFilter_ExcludesNonMatchingCurrency()
    {
        // Arrange
        var dtoA = BuildDto("A", orderCode: "ORD-A", currency: "CZK");
        var dtoB = BuildDto("B", orderCode: "ORD-B", currency: "EUR");

        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShoptetInvoiceDto> { dtoA, dtoB });
        client.Setup(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtoA);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-3",
            Currency = "CZK",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        var batch = result.Single();
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("A");

        client.Verify(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(x => x.GetInvoiceAsync("B", It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run the new test to verify it passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests.GetAllAsync_ListModeCurrencyFilter_ExcludesNonMatchingCurrency"
```
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 3: Run the whole new test class to confirm no regression**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"
```
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs
git commit -m "test: add ShoptetApiInvoiceSource currency-filter-excludes coverage (FR-3)"
```

---

### task: add-currency-filter-case-insensitive-theory

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`

Adds FR-4: the currency filter comparison is case-insensitive in both casing directions, as a `[Theory]`.

- [ ] **Step 1: Add the new theory test**

Replace the full content of `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` with exactly this (adds one new `[Theory]` method after the FR-3 test; FR-1/FR-2/FR-3 are unchanged):

```csharp
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetApiInvoiceSourceTests
{
    private static ShoptetInvoiceMapper BuildMapper() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    private static ShoptetApiInvoiceSource BuildSource(Mock<IShoptetInvoiceClient> client) =>
        new(client.Object, BuildMapper(), Mock.Of<ILogger<ShoptetApiInvoiceSource>>());

    private static ShoptetInvoiceDto BuildDto(string code, string? orderCode = null, string currency = "CZK") =>
        new()
        {
            Code = code,
            OrderCode = orderCode ?? $"ORD-{code}",
            Items = new List<ShoptetInvoiceItemDto>(),
            Price = new ShoptetInvoicePriceDto { CurrencyCode = currency, WithVat = "0", WithoutVat = "0" },
        };

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice()
    {
        // Arrange
        var dto = BuildDto("INV-1", orderCode: "ORD-1");
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-1",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-1");
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("INV-1");

        client.Verify(
            x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList()
    {
        // Arrange
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoptetInvoiceDto?)null);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-2",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-2");
        batch.Invoices.Should().NotBeNull();
        batch.Invoices.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ListModeCurrencyFilter_ExcludesNonMatchingCurrency()
    {
        // Arrange
        var dtoA = BuildDto("A", orderCode: "ORD-A", currency: "CZK");
        var dtoB = BuildDto("B", orderCode: "ORD-B", currency: "EUR");

        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShoptetInvoiceDto> { dtoA, dtoB });
        client.Setup(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtoA);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-3",
            Currency = "CZK",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        var batch = result.Single();
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("A");

        client.Verify(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(x => x.GetInvoiceAsync("B", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("czk", "CZK")]
    [InlineData("CZK", "czk")]
    public async Task GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive(string summaryCurrency, string queryCurrency)
    {
        // Arrange
        var dto = BuildDto("A", orderCode: "ORD-A", currency: summaryCurrency);

        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShoptetInvoiceDto> { dto });
        client.Setup(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-4",
            Currency = queryCurrency,
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        var batch = result.Single();
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("A");

        client.Verify(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the new theory test to verify both cases pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests.GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive"
```
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0` (both `InlineData` cases pass).

- [ ] **Step 3: Run the whole new test class to confirm no regression**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"
```
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs
git commit -m "test: add ShoptetApiInvoiceSource currency-filter case-insensitivity coverage (FR-4)"
```

---

### task: add-null-detail-guard-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`

Adds FR-5: the null-detail guard excludes the affected code without aborting the rest of the batch. This is the final test method for the file.

- [ ] **Step 1: Add the new test**

Replace the full content of `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` with exactly this (adds one new `[Fact]` method after the FR-4 theory; FR-1/FR-2/FR-3/FR-4 are unchanged). This is the complete, final version of the file:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetApiInvoiceSourceTests
{
    private static ShoptetInvoiceMapper BuildMapper() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    private static ShoptetApiInvoiceSource BuildSource(Mock<IShoptetInvoiceClient> client) =>
        new(client.Object, BuildMapper(), Mock.Of<ILogger<ShoptetApiInvoiceSource>>());

    private static ShoptetInvoiceDto BuildDto(string code, string? orderCode = null, string currency = "CZK") =>
        new()
        {
            Code = code,
            OrderCode = orderCode ?? $"ORD-{code}",
            Items = new List<ShoptetInvoiceItemDto>(),
            Price = new ShoptetInvoicePriceDto { CurrencyCode = currency, WithVat = "0", WithoutVat = "0" },
        };

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice()
    {
        // Arrange
        var dto = BuildDto("INV-1", orderCode: "ORD-1");
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-1",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-1");
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("INV-1");

        client.Verify(
            x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList()
    {
        // Arrange
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoptetInvoiceDto?)null);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-2",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-2");
        batch.Invoices.Should().NotBeNull();
        batch.Invoices.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ListModeCurrencyFilter_ExcludesNonMatchingCurrency()
    {
        // Arrange
        var dtoA = BuildDto("A", orderCode: "ORD-A", currency: "CZK");
        var dtoB = BuildDto("B", orderCode: "ORD-B", currency: "EUR");

        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShoptetInvoiceDto> { dtoA, dtoB });
        client.Setup(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtoA);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-3",
            Currency = "CZK",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        var batch = result.Single();
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("A");

        client.Verify(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(x => x.GetInvoiceAsync("B", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("czk", "CZK")]
    [InlineData("CZK", "czk")]
    public async Task GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive(string summaryCurrency, string queryCurrency)
    {
        // Arrange
        var dto = BuildDto("A", orderCode: "ORD-A", currency: summaryCurrency);

        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShoptetInvoiceDto> { dto });
        client.Setup(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-4",
            Currency = queryCurrency,
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        var batch = result.Single();
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("A");

        client.Verify(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ListModeNullDetail_ExcludesAffectedCodeWithoutAbortingBatch()
    {
        // Arrange
        var dtoB = BuildDto("B", orderCode: "ORD-B", currency: "CZK");

        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShoptetInvoiceDto>
            {
                BuildDto("A", orderCode: "ORD-A", currency: "CZK"),
                dtoB,
            });
        client.Setup(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoptetInvoiceDto?)null);
        client.Setup(x => x.GetInvoiceAsync("B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtoB);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-5",
            Currency = "CZK",
        };

        var source = BuildSource(client);

        // Act — GetAllAsync must not throw despite "A"'s detail fetch returning null; proven by the
        // successful await below (an unguarded NullReferenceException would fail this test).
        var result = await source.GetAllAsync(query);

        // Assert
        var batch = result.Single();
        batch.Invoices.Should().HaveCount(1);
        batch.Invoices[0].OrderCode.Should().Be("B");

        // Both codes were sent to GetInvoiceAsync — the loop must not short-circuit/abort on the null result.
        client.Verify(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(x => x.GetInvoiceAsync("B", It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the new test to verify it passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests.GetAllAsync_ListModeNullDetail_ExcludesAffectedCodeWithoutAbortingBatch"
```
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 3: Run the whole new test class to confirm all six scenarios pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"
```
Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0` (FR-1, FR-2, FR-3, FR-4×2 InlineData cases, FR-5 = 6 total test executions).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs
git commit -m "test: add ShoptetApiInvoiceSource null-detail-guard coverage (FR-5)"
```

---

### task: final-validation

**Files:** none (validation-only; no further edits expected)

Runs the full project-standard validation gate from `CLAUDE.md` — `dotnet build` and `dotnet format` — plus the complete `Anela.Heblo.Adapters.Shoptet.Tests` project's test suite (not just the new class), to confirm the new file compiles cleanly, is correctly formatted, and does not regress any existing test in the project (including `Integration/ShoptetApiInvoiceSourceIntegrationTests.cs`, which stays inert/skipped without `Shoptet:ApiToken` configured, and every other `Unit/`/`Expedition/` test in the project).

- [ ] **Step 1: Build the solution**

Run:
```bash
dotnet build Anela.Heblo.sln
```
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 2: Run dotnet format and check for changes**

Run:
```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```
Expected: exits with code 0 and no output listing changed files, meaning `ShoptetApiInvoiceSourceTests.cs` (as written in the previous tasks) is already compliant with the repo's formatting rules.

If it instead reports files needing formatting, run:
```bash
dotnet format Anela.Heblo.sln
```
then re-run `git diff` to inspect what changed. If the only file changed is `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`, proceed to Step 3; if `dotnet format` touches any other file, revert those unrelated changes with `git checkout -- <path>` before continuing (this task's scope is limited to the new test file — no other file should be touched).

- [ ] **Step 3: Run the full test project to confirm no regressions**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```
Expected: overall run reports `Failed: 0`; the six `ShoptetApiInvoiceSourceTests` executions (FR-1 through FR-5, with FR-4 contributing two `InlineData` cases) are all `Passed`, and every pre-existing test in the project (including any integration tests that skip/no-op without `Shoptet:ApiToken` configured) is unaffected.

- [ ] **Step 4: Commit (only if Step 2 produced formatting changes)**

If `dotnet format` in Step 2 modified `ShoptetApiInvoiceSourceTests.cs`, commit that formatting fix:
```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs
git commit -m "style: apply dotnet format to ShoptetApiInvoiceSourceTests"
```
If Step 2 reported no changes needed, skip this commit — there is nothing new to commit in this task.
