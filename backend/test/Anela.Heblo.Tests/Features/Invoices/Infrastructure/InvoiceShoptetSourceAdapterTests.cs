using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

public class InvoiceShoptetSourceAdapterTests
{
    private readonly Mock<IIssuedInvoiceSource> _inner = new();

    private InvoiceShoptetSourceAdapter CreateAdapter() => new(_inner.Object);

    [Fact]
    public async Task GetAllAsync_MapsQueryFieldsIntoInnerQuery()
    {
        IssuedInvoiceSourceQuery? capturedQuery = null;
        _inner
            .Setup(s => s.GetAllAsync(It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IssuedInvoiceSourceQuery, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch>());

        var query = new DqtInvoiceSourceQuery
        {
            RequestId = "dqt-2026-01-01-2026-01-31",
            DateFrom = new DateOnly(2026, 1, 1),
            DateTo = new DateOnly(2026, 1, 31)
        };

        var adapter = CreateAdapter();

        await adapter.GetAllAsync(query, CancellationToken.None);

        capturedQuery.Should().NotBeNull();
        capturedQuery!.RequestId.Should().Be("dqt-2026-01-01-2026-01-31");
        capturedQuery.DateFrom.Should().Be(new DateTime(2026, 1, 1));
        capturedQuery.DateTo.Should().Be(new DateTime(2026, 1, 31));
        capturedQuery.InvoiceId.Should().BeNull();
        capturedQuery.Currency.Should().Be("CZK");
    }

    [Fact]
    public async Task GetAllAsync_FlattensBatchesAndMapsInvoicesAndItemsToSnapshots()
    {
        var invoice1 = new IssuedInvoiceDetail
        {
            Code = "INV-1",
            Price = new InvoicePrice { TotalWithVat = 121m, TotalWithoutVat = 100m },
            Items = new List<IssuedInvoiceDetailItem>
            {
                new IssuedInvoiceDetailItem
                {
                    Code = "PROD-A",
                    Amount = 2m,
                    ItemPrice = new InvoicePrice { WithVat = 121m, WithoutVat = 100m },
                    BuyPrice = new InvoicePrice()
                }
            }
        };
        var invoice2 = new IssuedInvoiceDetail
        {
            Code = "INV-2",
            Price = new InvoicePrice { TotalWithVat = 242m, TotalWithoutVat = 200m },
            Items = new List<IssuedInvoiceDetailItem>()
        };

        var batches = new List<IssuedInvoiceDetailBatch>
        {
            new IssuedInvoiceDetailBatch { BatchId = "b1", Invoices = new List<IssuedInvoiceDetail> { invoice1 } },
            new IssuedInvoiceDetailBatch { BatchId = "b2", Invoices = new List<IssuedInvoiceDetail> { invoice2 } }
        };

        _inner
            .Setup(s => s.GetAllAsync(It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batches);

        var adapter = CreateAdapter();

        var result = await adapter.GetAllAsync(new DqtInvoiceSourceQuery(), CancellationToken.None);

        result.Should().HaveCount(2);

        var snapshot1 = result.Single(r => r.Code == "INV-1");
        snapshot1.TotalWithVat.Should().Be(121m);
        snapshot1.TotalWithoutVat.Should().Be(100m);
        snapshot1.Items.Should().ContainSingle();
        snapshot1.Items[0].Code.Should().Be("PROD-A");
        snapshot1.Items[0].Amount.Should().Be(2m);
        snapshot1.Items[0].WithVat.Should().Be(121m);
        snapshot1.Items[0].WithoutVat.Should().Be(100m);

        var snapshot2 = result.Single(r => r.Code == "INV-2");
        snapshot2.TotalWithVat.Should().Be(242m);
        snapshot2.TotalWithoutVat.Should().Be(200m);
        snapshot2.Items.Should().BeEmpty();
    }
}
