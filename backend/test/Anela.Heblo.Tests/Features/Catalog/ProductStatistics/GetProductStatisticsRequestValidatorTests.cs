using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using Anela.Heblo.Application.Features.Catalog.Validators;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.ProductStatistics;

public class GetProductStatisticsRequestValidatorTests
{
    private readonly GetProductStatisticsRequestValidator _validator = new();

    private static GetProductStatisticsRequest Valid() => new()
    {
        ProductCodes = new List<string> { "PROD-A" },
        Metric = ProductStatisticsMetric.Sales,
        DateFrom = "2025-01",
        DateTo = "2025-06",
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NoProductCodes_Fails()
    {
        var request = Valid();
        request.ProductCodes = new List<string>();

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MoreThanTenProductCodes_Fails()
    {
        var request = Valid();
        request.ProductCodes = Enumerable.Range(1, 11).Select(i => $"PROD-{i}").ToList();

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ExactlyTenProductCodes_Passes()
    {
        var request = Valid();
        request.ProductCodes = Enumerable.Range(1, 10).Select(i => $"PROD-{i}").ToList();

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_BlankProductCode_Fails()
    {
        var request = Valid();
        request.ProductCodes = new List<string> { "PROD-A", "  " };

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("2025-1")]
    [InlineData("2025-13")]
    [InlineData("25-01")]
    [InlineData("")]
    [InlineData("nonsense")]
    public void Validate_MalformedDateFrom_Fails(string dateFrom)
    {
        var request = Valid();
        request.DateFrom = dateFrom;

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("2025-1")]
    [InlineData("2025-13")]
    [InlineData("")]
    public void Validate_MalformedDateTo_Fails(string dateTo)
    {
        var request = Valid();
        request.DateTo = dateTo;

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_InvertedRange_Fails()
    {
        var request = Valid();
        request.DateFrom = "2025-06";
        request.DateTo = "2025-01";

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SameFromAndTo_Passes()
    {
        var request = Valid();
        request.DateFrom = "2025-03";
        request.DateTo = "2025-03";

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DateFromBeforeHistoryFloor_Passes()
    {
        // Pre-2020 is clamped by MonthRange.Expand, not rejected — a bookmark with an old
        // range should still render, matching how GetCatalogDetailHandler treats the floor.
        var request = Valid();
        request.DateFrom = "2018-01";

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_OutOfRangeMetric_Fails()
    {
        var request = Valid();
        request.Metric = (ProductStatisticsMetric)99;

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(ProductStatisticsMetric.Sales)]
    [InlineData(ProductStatisticsMetric.Purchase)]
    [InlineData(ProductStatisticsMetric.Consumption)]
    [InlineData(ProductStatisticsMetric.Manufacture)]
    public void Validate_DefinedMetric_Passes(ProductStatisticsMetric metric)
    {
        var request = Valid();
        request.Metric = metric;

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_120MonthSpan_Passes()
    {
        var request = Valid();
        request.DateFrom = "2015-01";
        request.DateTo = "2024-12";

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_121MonthSpan_Fails()
    {
        var request = Valid();
        request.DateFrom = "2015-01";
        request.DateTo = "2025-01";

        _validator.Validate(request).IsValid.Should().BeFalse();
    }
}
