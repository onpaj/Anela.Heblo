using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

public class InvoiceDqtSnapshotMapperTests
{
    [Fact]
    public void ToDqtSnapshot_MapsInvoiceLevelFields()
    {
        var invoice = new IssuedInvoiceDetail
        {
            Code = "INV-100",
            Price = new InvoicePrice { TotalWithVat = 1210m, TotalWithoutVat = 1000m },
            Items = new List<IssuedInvoiceDetailItem>()
        };

        var snapshot = invoice.ToDqtSnapshot();

        snapshot.Code.Should().Be("INV-100");
        snapshot.TotalWithVat.Should().Be(1210m);
        snapshot.TotalWithoutVat.Should().Be(1000m);
        snapshot.Items.Should().BeEmpty();
    }

    [Fact]
    public void ToDqtSnapshot_MapsMultipleItems_WithoutSwappingWithVatAndWithoutVat()
    {
        var invoice = new IssuedInvoiceDetail
        {
            Code = "INV-101",
            Price = new InvoicePrice { TotalWithVat = 2420m, TotalWithoutVat = 2000m },
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
                    ItemPrice = new InvoicePrice { WithVat = 363m, WithoutVat = 300m },
                    BuyPrice = new InvoicePrice()
                }
            }
        };

        var snapshot = invoice.ToDqtSnapshot();

        snapshot.Items.Should().HaveCount(2);

        snapshot.Items[0].Code.Should().Be("PROD-A");
        snapshot.Items[0].Amount.Should().Be(2m);
        snapshot.Items[0].WithVat.Should().Be(121m);
        snapshot.Items[0].WithoutVat.Should().Be(100m);

        snapshot.Items[1].Code.Should().Be("PROD-B");
        snapshot.Items[1].Amount.Should().Be(5m);
        snapshot.Items[1].WithVat.Should().Be(363m);
        snapshot.Items[1].WithoutVat.Should().Be(300m);
    }

    [Fact]
    public void ToDqtItem_MapsFieldsFromNestedItemPrice()
    {
        var item = new IssuedInvoiceDetailItem
        {
            Code = "PROD-C",
            Amount = 3m,
            ItemPrice = new InvoicePrice { WithVat = 121m, WithoutVat = 100m },
            BuyPrice = new InvoicePrice()
        };

        var dqtItem = item.ToDqtItem();

        dqtItem.Code.Should().Be("PROD-C");
        dqtItem.Amount.Should().Be(3m);
        dqtItem.WithVat.Should().Be(121m);
        dqtItem.WithoutVat.Should().Be(100m);
    }
}
