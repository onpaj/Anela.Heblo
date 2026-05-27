using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderMappingProfileTests
{
    private readonly IMapper _mapper;

    public ManufactureOrderMappingProfileTests()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<ManufactureOrderMappingProfile>(), NullLoggerFactory.Instance);
        config.AssertConfigurationIsValid();
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void Map_ResidueDistribution_To_Dto_PreservesAllFields()
    {
        // Arrange
        var source = new ResidueDistribution
        {
            ActualSemiProductQuantity = 12.5m,
            TheoreticalConsumption = 12.0m,
            Difference = 0.5m,
            DifferencePercentage = 4.17,
            IsWithinAllowedThreshold = true,
            AllowedResiduePercentage = 5.0,
            Products = new List<ProductConsumptionDistribution>
            {
                new()
                {
                    ProductCode = "PROD-A",
                    ProductName = "Product A",
                    ActualPieces = 100m,
                    TheoreticalGramsPerUnit = 0.12m,
                    TheoreticalConsumption = 12.0m,
                    AdjustedConsumption = 12.5m,
                    AdjustedGramsPerUnit = 0.125m,
                    ProportionRatio = 1.0,
                },
                new()
                {
                    ProductCode = "PROD-B",
                    ProductName = "Product B",
                    ActualPieces = 0m,
                    TheoreticalGramsPerUnit = 0m,
                    TheoreticalConsumption = 0m,
                    AdjustedConsumption = 0m,
                    AdjustedGramsPerUnit = 0m,
                    ProportionRatio = 0.0,
                },
            },
        };

        // Act
        var dto = _mapper.Map<ResidueDistributionDto>(source);

        // Assert
        dto.ActualSemiProductQuantity.Should().Be(12.5m);
        dto.TheoreticalConsumption.Should().Be(12.0m);
        dto.Difference.Should().Be(0.5m);
        dto.DifferencePercentage.Should().Be(4.17);
        dto.IsWithinAllowedThreshold.Should().BeTrue();
        dto.AllowedResiduePercentage.Should().Be(5.0);
        dto.Products.Should().HaveCount(2);

        dto.Products[0].ProductCode.Should().Be("PROD-A");
        dto.Products[0].ProductName.Should().Be("Product A");
        dto.Products[0].ActualPieces.Should().Be(100m);
        dto.Products[0].TheoreticalGramsPerUnit.Should().Be(0.12m);
        dto.Products[0].TheoreticalConsumption.Should().Be(12.0m);
        dto.Products[0].AdjustedConsumption.Should().Be(12.5m);
        dto.Products[0].AdjustedGramsPerUnit.Should().Be(0.125m);
        dto.Products[0].ProportionRatio.Should().Be(1.0);

        // Zero-quantity edge case (PROD-B) round-trips intact.
        dto.Products[1].ActualPieces.Should().Be(0m);
        dto.Products[1].AdjustedGramsPerUnit.Should().Be(0m);
    }

    [Fact]
    public void Map_ResidueDistribution_To_Dto_EmptyProductList()
    {
        // Arrange
        var source = new ResidueDistribution
        {
            ActualSemiProductQuantity = 0m,
            Products = new List<ProductConsumptionDistribution>(),
        };

        // Act
        var dto = _mapper.Map<ResidueDistributionDto>(source);

        // Assert
        dto.Products.Should().NotBeNull().And.BeEmpty();
    }
}
