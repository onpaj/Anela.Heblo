using System;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.ProductStatistics;

public class ProductStatisticsContractTests
{
    [Fact]
    public void Response_InheritsBaseResponse()
    {
        typeof(GetProductStatisticsResponse).Should().BeDerivedFrom<BaseResponse>();
    }

    [Fact]
    public void Contracts_AreClassesNotRecords()
    {
        // Records emit a compiler-generated <Clone>$ method; DTOs here must not be records
        // because the OpenAPI generator mishandles record parameter order.
        typeof(GetProductStatisticsResponse).GetMethod("<Clone>$").Should().BeNull();
        typeof(ProductStatisticsSeriesDto).GetMethod("<Clone>$").Should().BeNull();
        typeof(GetProductStatisticsRequest).GetMethod("<Clone>$").Should().BeNull();
    }

    [Fact]
    public void Response_DefaultsToSuccessWithEmptyCollections()
    {
        var response = new GetProductStatisticsResponse();

        response.Success.Should().BeTrue();
        response.Months.Should().BeEmpty();
        response.Products.Should().BeEmpty();
    }

    [Fact]
    public void Metric_HasExactlyFourValues()
    {
        Enum.GetValues<ProductStatisticsMetric>().Should().BeEquivalentTo(new[]
        {
            ProductStatisticsMetric.Sales,
            ProductStatisticsMetric.Purchase,
            ProductStatisticsMetric.Consumption,
            ProductStatisticsMetric.Manufacture,
        });
    }
}
