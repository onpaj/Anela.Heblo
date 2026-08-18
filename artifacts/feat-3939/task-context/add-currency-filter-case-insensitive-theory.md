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
