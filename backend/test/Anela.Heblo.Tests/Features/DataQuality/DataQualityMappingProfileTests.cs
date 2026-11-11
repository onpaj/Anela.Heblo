using Anela.Heblo.Application.Features.DataQuality;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class DataQualityMappingProfileTests
{
    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddProfile<DataQualityMappingProfile>(), NullLoggerFactory.Instance)
            .CreateMapper();

    private static InvoiceDqtResultDto Map(InvoiceMismatchType type)
    {
        var result = InvoiceDqtResult.Create(Guid.NewGuid(), "INV-1", type, null, null, null);
        return Mapper.Map<InvoiceDqtResultDto>(result);
    }

    [Theory]
    [InlineData(InvoiceMismatchType.MissingInFlexi, 1)]
    [InlineData(InvoiceMismatchType.MissingInShoptet, 2)]
    [InlineData(InvoiceMismatchType.TotalWithVatDiffers, 4)]
    [InlineData(InvoiceMismatchType.TotalWithoutVatDiffers, 8)]
    [InlineData(InvoiceMismatchType.ItemsDiffer, 16)]
    [InlineData(InvoiceMismatchType.DuplicateInvoiceCode, 32)]
    public void Map_SingleFlag_ProducesSingleNameAndNumericValue(InvoiceMismatchType flag, int expectedValue)
    {
        var dto = Map(flag);

        dto.MismatchFlags.Should().Equal(flag.ToString());
        dto.MismatchType.Should().Be(expectedValue);
    }

    [Fact]
    public void Map_CompoundFlags_ProducesNamesInAscendingOrder()
    {
        var dto = Map(InvoiceMismatchType.MissingInFlexi | InvoiceMismatchType.TotalWithVatDiffers);

        dto.MismatchFlags.Should().Equal("MissingInFlexi", "TotalWithVatDiffers");
        dto.MismatchType.Should().Be(5);
    }

    [Fact]
    public void Map_AllFlags_ProducesSixNamesWithoutNone()
    {
        var all = InvoiceMismatchType.MissingInFlexi | InvoiceMismatchType.MissingInShoptet
                  | InvoiceMismatchType.TotalWithVatDiffers | InvoiceMismatchType.TotalWithoutVatDiffers
                  | InvoiceMismatchType.ItemsDiffer | InvoiceMismatchType.DuplicateInvoiceCode;

        var dto = Map(all);

        dto.MismatchFlags.Should().Equal(
            "MissingInFlexi", "MissingInShoptet", "TotalWithVatDiffers",
            "TotalWithoutVatDiffers", "ItemsDiffer", "DuplicateInvoiceCode");
        dto.MismatchFlags.Should().NotContain("None");
    }

    [Fact]
    public void Map_None_ProducesEmptyNonNullListAndZero()
    {
        var dto = Map(InvoiceMismatchType.None);

        dto.MismatchFlags.Should().NotBeNull().And.BeEmpty();
        dto.MismatchType.Should().Be(0);
    }

    [Fact]
    public void Map_DqtRun_MapsEnumsToStrings()
    {
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual, DateTime.UtcNow);

        var dto = Mapper.Map<DqtRunDto>(run);

        dto.TestType.Should().Be(run.TestType.ToString());
        dto.Status.Should().Be(run.Status.ToString());
        dto.TriggerType.Should().Be(DqtTriggerType.Manual.ToString());
    }
}
