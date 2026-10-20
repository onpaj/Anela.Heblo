using Anela.Heblo.Adapters.Flexi.Accounting.MarketingPerformance;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

public class FlexiMonthlyAdCostSourceTests
{
    [Fact]
    public async Task GetAsync_QueriesWholeMonthByAccountingDate_AndMapsFields()
    {
        var client = new Mock<IReceivedInvoicesClient>();
        client.Setup(c => c.SearchByVatIdsAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ReceivedInvoice>
              {
                  new() { InvoiceNumber = "PF1", SupplierVatId = "IE9692928F", AccountingDate = new DateTime(2026, 8, 3), TotalAmountWithoutVat = 1000m, IsCancelled = false },
                  new() { InvoiceNumber = "PF2", SupplierVatId = null, AccountingDate = null, TotalAmountWithoutVat = 5m, IsCancelled = true },
              });
        var source = new FlexiMonthlyAdCostSource(client.Object, NullLogger<FlexiMonthlyAdCostSource>.Instance);

        var result = await source.GetAsync(new YearMonth(2026, 8), new[] { "IE9692928F" }, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new AdCostInvoice { InvoiceNumber = "PF1", SupplierVatId = "IE9692928F", AccountingDate = new DateTime(2026, 8, 3), AmountWithoutVat = 1000m, IsCancelled = false });
        result[1].SupplierVatId.Should().BeEmpty();
        result[1].AccountingDate.Should().Be(new YearMonth(2026, 8).Start);
        client.VerifyAll();
    }

    [Fact]
    public async Task GetAsync_NoVatIds_ReturnsEmptyWithoutCallingFlexi()
    {
        var client = new Mock<IReceivedInvoicesClient>(MockBehavior.Strict);
        var source = new FlexiMonthlyAdCostSource(client.Object, NullLogger<FlexiMonthlyAdCostSource>.Instance);

        var result = await source.GetAsync(new YearMonth(2026, 8), Array.Empty<string>(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
