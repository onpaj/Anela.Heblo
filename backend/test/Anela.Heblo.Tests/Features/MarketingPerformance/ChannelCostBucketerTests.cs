using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class ChannelCostBucketerTests
{
    private static readonly IReadOnlyList<MarketingChannelDefinition> Channels = new[]
    {
        new MarketingChannelDefinition { Code = "meta", Label = "FB/IG", VatIds = new[] { "IE9692928F", "IE0000001X" } },
        new MarketingChannelDefinition { Code = "google", Label = "Google", VatIds = new[] { "IE6388047V" } },
        new MarketingChannelDefinition { Code = "sklik", Label = "S-Klik", VatIds = new[] { "CZ26168685" } },
    };

    private static AdCostInvoice Inv(string vat, decimal amount, bool cancelled = false) =>
        new() { InvoiceNumber = Guid.NewGuid().ToString("N")[..8], SupplierVatId = vat, AmountWithoutVat = amount, IsCancelled = cancelled, AccountingDate = new DateTime(2026, 8, 5) };

    [Fact]
    public void Bucket_SumsPerChannel_AndMultipleVatIdsPerChannel()
    {
        var result = ChannelCostBucketer.Bucket(new[]
        {
            Inv("IE9692928F", 100m), Inv("ie0000001x", 50m), Inv("IE6388047V", 30m), Inv("CZ26168685", 5m),
        }, Channels);

        result.Buckets.Select(b => b.ChannelCode).Should().Equal("meta", "google", "sklik");
        result.Buckets[0].CostWithoutVat.Should().Be(150m);
        result.Buckets[0].InvoiceCount.Should().Be(2);
        result.Buckets[1].CostWithoutVat.Should().Be(30m);
        result.Buckets[2].CostWithoutVat.Should().Be(5m);
    }

    [Fact]
    public void Bucket_ChannelWithoutInvoices_GetsExplicitZeroRow()
    {
        var result = ChannelCostBucketer.Bucket(new[] { Inv("IE9692928F", 100m) }, Channels);
        result.Buckets.Should().HaveCount(3);
        result.Buckets.Single(b => b.ChannelCode == "sklik").Should().BeEquivalentTo(new ChannelCostBucket { ChannelCode = "sklik", CostWithoutVat = 0m, InvoiceCount = 0 });
    }

    [Fact]
    public void Bucket_SkipsCancelled_AndReportsUnmatched()
    {
        var result = ChannelCostBucketer.Bucket(new[]
        {
            Inv("IE9692928F", 100m), Inv("IE9692928F", 999m, cancelled: true), Inv("DE123", 7m),
        }, Channels);

        result.Buckets.Single(b => b.ChannelCode == "meta").CostWithoutVat.Should().Be(100m);
        result.SkippedCancelled.Should().Be(1);
        result.UnmatchedVatIds.Should().Equal("DE123");
    }

    [Fact]
    public void Bucket_NegativeCreditNote_ReducesTheSum()
    {
        var result = ChannelCostBucketer.Bucket(new[] { Inv("IE6388047V", 100m), Inv("IE6388047V", -20m) }, Channels);
        result.Buckets.Single(b => b.ChannelCode == "google").CostWithoutVat.Should().Be(80m);
    }
}
