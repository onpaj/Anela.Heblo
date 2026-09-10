using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

public class InvoiceErpClientAdapterTests
{
    private readonly Mock<IIssuedInvoiceClient> _inner = new();

    private InvoiceErpClientAdapter CreateAdapter() => new(_inner.Object);

    [Fact]
    public async Task GetAllAsync_ForwardsFromToAndCancellationTokenToInnerClient()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 31);
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        _inner
            .Setup(c => c.GetAllAsync(from, to, ct))
            .ReturnsAsync(new List<IssuedInvoiceDetail>());

        var adapter = CreateAdapter();

        await adapter.GetAllAsync(from, to, ct);

        _inner.Verify(c => c.GetAllAsync(from, to, ct), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_MapsInvoicesWithMultipleItemsToSnapshots()
    {
        var invoice = new IssuedInvoiceDetail
        {
            Code = "INV-1",
            Price = new InvoicePrice { TotalWithVat = 363m, TotalWithoutVat = 300m },
            Items = new List<IssuedInvoiceDetailItem>
            {
                new IssuedInvoiceDetailItem
                {
                    Code = "PROD-A",
                    Amount = 2m,
                    ItemPrice = new InvoicePrice { WithVat = 121m, WithoutVat = 100m },
                    BuyPrice = new InvoicePrice()
                },
                new IssuedInvoiceDetailItem
                {
                    Code = "PROD-B",
                    Amount = 5m,
                    ItemPrice = new InvoicePrice { WithVat = 242m, WithoutVat = 200m },
                    BuyPrice = new InvoicePrice()
                }
            }
        };

        _inner
            .Setup(c => c.GetAllAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetail> { invoice });

        var adapter = CreateAdapter();

        var result = await adapter.GetAllAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), CancellationToken.None);

        result.Should().ContainSingle();
        var snapshot = result[0];
        snapshot.Code.Should().Be("INV-1");
        snapshot.TotalWithVat.Should().Be(363m);
        snapshot.TotalWithoutVat.Should().Be(300m);
        snapshot.Items.Should().HaveCount(2);

        snapshot.Items[0].Code.Should().Be("PROD-A");
        snapshot.Items[0].Amount.Should().Be(2m);
        snapshot.Items[0].WithVat.Should().Be(121m);
        snapshot.Items[0].WithoutVat.Should().Be(100m);

        snapshot.Items[1].Code.Should().Be("PROD-B");
        snapshot.Items[1].Amount.Should().Be(5m);
        snapshot.Items[1].WithVat.Should().Be(242m);
        snapshot.Items[1].WithoutVat.Should().Be(200m);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyList_WhenInnerResultIsEmpty()
    {
        _inner
            .Setup(c => c.GetAllAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetail>());

        var adapter = CreateAdapter();

        var result = await adapter.GetAllAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
