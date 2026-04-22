using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Invoices;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class InvoiceDqtComparerTests
{
    private readonly Mock<IIssuedInvoiceSource> _sourceMock = new();
    private readonly Mock<IIssuedInvoiceClient> _clientMock = new();
    private readonly InvoiceDqtComparer _sut;

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    public InvoiceDqtComparerTests()
    {
        _sut = new InvoiceDqtComparer(_sourceMock.Object, _clientMock.Object);
    }

    private void SetupShoptet(params IssuedInvoiceDetail[] invoices)
    {
        var batch = new IssuedInvoiceDetailBatch { Invoices = invoices.ToList() };
        _sourceMock.Setup(s => s.GetAllAsync(It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });
    }

    private void SetupFlexi(params IssuedInvoiceDetail[] invoices)
    {
        _clientMock.Setup(c => c.GetAllAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoices.ToList());
    }

    private static IssuedInvoiceDetail MakeInvoice(string code, decimal totalWithVat = 100m, decimal totalWithoutVat = 80m, List<IssuedInvoiceDetailItem>? items = null)
    {
        return new IssuedInvoiceDetail
        {
            Code = code,
            Price = new InvoicePrice { TotalWithVat = totalWithVat, TotalWithoutVat = totalWithoutVat },
            Items = items ?? new List<IssuedInvoiceDetailItem>()
        };
    }

    private static IssuedInvoiceDetailItem MakeItem(string code, decimal amount = 1m, decimal withVat = 100m, decimal withoutVat = 80m)
    {
        return new IssuedInvoiceDetailItem
        {
            Code = code,
            Name = code,
            VariantName = string.Empty,
            AmountUnit = "ks",
            Amount = amount,
            ItemPrice = new InvoicePrice { WithVat = withVat, WithoutVat = withoutVat },
            BuyPrice = new InvoicePrice()
        };
    }

    [Fact]
    public async Task BothEmpty_ReturnsZeroCheckedZeroMismatches()
    {
        SetupShoptet();
        SetupFlexi();

        var result = await _sut.CompareAsync(From, To);

        Assert.Equal(0, result.TotalChecked);
        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public async Task InvoiceInShoptetOnly_FlagsMissingInFlexi()
    {
        SetupShoptet(MakeInvoice("INV-001"));
        SetupFlexi();

        var result = await _sut.CompareAsync(From, To);

        Assert.Single(result.Mismatches);
        Assert.Equal("INV-001", result.Mismatches[0].InvoiceCode);
        Assert.Equal(InvoiceMismatchType.MissingInFlexi, result.Mismatches[0].MismatchType);
    }

    [Fact]
    public async Task InvoiceInFlexiOnly_FlagsMissingInShoptet()
    {
        SetupShoptet();
        SetupFlexi(MakeInvoice("INV-002"));

        var result = await _sut.CompareAsync(From, To);

        Assert.Single(result.Mismatches);
        Assert.Equal("INV-002", result.Mismatches[0].InvoiceCode);
        Assert.Equal(InvoiceMismatchType.MissingInShoptet, result.Mismatches[0].MismatchType);
    }

    [Fact]
    public async Task MatchingInvoices_ReturnsZeroMismatches()
    {
        var inv = MakeInvoice("INV-003");
        SetupShoptet(inv);
        SetupFlexi(MakeInvoice("INV-003"));

        var result = await _sut.CompareAsync(From, To);

        Assert.Equal(1, result.TotalChecked);
        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public async Task WithinTolerance_NoMismatch()
    {
        SetupShoptet(MakeInvoice("INV-004", totalWithVat: 100.00m, totalWithoutVat: 80.00m));
        SetupFlexi(MakeInvoice("INV-004", totalWithVat: 100.01m, totalWithoutVat: 80.02m));

        var result = await _sut.CompareAsync(From, To);

        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public async Task WithVatDiffers_FlagsTotalWithVatDiffers()
    {
        SetupShoptet(MakeInvoice("INV-005", totalWithVat: 100.00m));
        SetupFlexi(MakeInvoice("INV-005", totalWithVat: 110.00m));

        var result = await _sut.CompareAsync(From, To);

        Assert.Single(result.Mismatches);
        Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.TotalWithVatDiffers));
    }

    [Fact]
    public async Task WithoutVatDiffers_FlagsTotalWithoutVatDiffers()
    {
        SetupShoptet(MakeInvoice("INV-006", totalWithoutVat: 80.00m));
        SetupFlexi(MakeInvoice("INV-006", totalWithoutVat: 90.00m));

        var result = await _sut.CompareAsync(From, To);

        Assert.Single(result.Mismatches);
        Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.TotalWithoutVatDiffers));
    }

    [Fact]
    public async Task ItemsDiffer_ByProductCode()
    {
        var shoptetItems = new List<IssuedInvoiceDetailItem> { MakeItem("PROD-A"), MakeItem("PROD-B") };
        var flexiItems = new List<IssuedInvoiceDetailItem> { MakeItem("PROD-A") };

        SetupShoptet(MakeInvoice("INV-007", items: shoptetItems));
        SetupFlexi(MakeInvoice("INV-007", items: flexiItems));

        var result = await _sut.CompareAsync(From, To);

        Assert.Single(result.Mismatches);
        Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
        Assert.Contains("PROD-B", result.Mismatches[0].Details);
    }

    [Fact]
    public async Task ItemsDiffer_ByAmount()
    {
        var shoptetItems = new List<IssuedInvoiceDetailItem> { MakeItem("PROD-C", amount: 2m) };
        var flexiItems = new List<IssuedInvoiceDetailItem> { MakeItem("PROD-C", amount: 3m) };

        SetupShoptet(MakeInvoice("INV-008", items: shoptetItems));
        SetupFlexi(MakeInvoice("INV-008", items: flexiItems));

        var result = await _sut.CompareAsync(From, To);

        Assert.Single(result.Mismatches);
        Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
        Assert.Contains("Amount", result.Mismatches[0].Details);
    }

    [Fact]
    public async Task ItemPriceDiffers()
    {
        var shoptetItems = new List<IssuedInvoiceDetailItem> { MakeItem("PROD-D", withVat: 50m) };
        var flexiItems = new List<IssuedInvoiceDetailItem> { MakeItem("PROD-D", withVat: 60m) };

        SetupShoptet(MakeInvoice("INV-009", items: shoptetItems));
        SetupFlexi(MakeInvoice("INV-009", items: flexiItems));

        var result = await _sut.CompareAsync(From, To);

        Assert.Single(result.Mismatches);
        Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
        Assert.Contains("WithVat", result.Mismatches[0].Details);
    }

    [Fact]
    public async Task MultipleIssues_CombinesFlags()
    {
        // INV-010: missing in Flexi
        // INV-011: total mismatch + item diff
        var shoptetItems = new List<IssuedInvoiceDetailItem> { MakeItem("PROD-X", withVat: 50m) };
        var flexiItems = new List<IssuedInvoiceDetailItem> { MakeItem("PROD-X", withVat: 60m) };

        SetupShoptet(
            MakeInvoice("INV-010"),
            MakeInvoice("INV-011", totalWithVat: 100m, items: shoptetItems));
        SetupFlexi(
            MakeInvoice("INV-011", totalWithVat: 200m, items: flexiItems));

        var result = await _sut.CompareAsync(From, To);

        Assert.Equal(2, result.TotalChecked);
        Assert.Equal(2, result.Mismatches.Count);

        var missing = result.Mismatches.Single(m => m.InvoiceCode == "INV-010");
        Assert.Equal(InvoiceMismatchType.MissingInFlexi, missing.MismatchType);

        var combined = result.Mismatches.Single(m => m.InvoiceCode == "INV-011");
        Assert.True(combined.MismatchType.HasFlag(InvoiceMismatchType.TotalWithVatDiffers));
        Assert.True(combined.MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
    }
}
