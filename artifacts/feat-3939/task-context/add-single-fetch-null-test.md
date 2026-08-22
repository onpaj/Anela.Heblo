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
