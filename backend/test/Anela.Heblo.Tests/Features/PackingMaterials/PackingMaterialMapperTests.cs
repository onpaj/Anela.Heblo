using Anela.Heblo.Application.Features.PackingMaterials.Mapping;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialMapperTests
{
    private static PackingMaterial MakeMaterial(int id, string name, decimal consumptionRate, ConsumptionType consumptionType, decimal currentQuantity)
    {
        var material = new PackingMaterial(name, consumptionRate, consumptionType, currentQuantity);
        typeof(PackingMaterial)
            .GetProperty("Id")!
            .SetValue(material, id);
        return material;
    }

    [Fact]
    public void ToDto_MapsAllFields_WhenForecastedDaysIsProvided()
    {
        // Arrange
        var material = MakeMaterial(7, "Cardboard Box", 2.5m, ConsumptionType.PerOrder, 150m);

        // Act
        var dto = PackingMaterialMapper.ToDto(material, 12.3m);

        // Assert
        Assert.Equal(material.Id, dto.Id);
        Assert.Equal(material.Name, dto.Name);
        Assert.Equal(material.ConsumptionRate, dto.ConsumptionRate);
        Assert.Equal(material.ConsumptionType, dto.ConsumptionType);
        Assert.Equal("za zakázku", dto.ConsumptionTypeText);
        Assert.Equal(material.CurrentQuantity, dto.CurrentQuantity);
        Assert.Equal(12.3m, dto.ForecastedDays);
        Assert.Equal(material.CreatedAt, dto.CreatedAt);
        Assert.Equal(material.UpdatedAt, dto.UpdatedAt);
    }

    [Fact]
    public void ToDto_SetsForecastedDaysToNull_WhenForecastedDaysArgumentIsNull()
    {
        // Arrange
        var material = MakeMaterial(1, "Tape Roll", 1m, ConsumptionType.PerDay, 50m);

        // Act
        var dto = PackingMaterialMapper.ToDto(material, null);

        // Assert
        Assert.Null(dto.ForecastedDays);
    }

    [Theory]
    [InlineData(ConsumptionType.PerOrder, "za zakázku")]
    [InlineData(ConsumptionType.PerProduct, "za produkt")]
    [InlineData(ConsumptionType.PerDay, "za den")]
    public void ToDto_DerivesConsumptionTypeText_FromConsumptionType(ConsumptionType type, string expectedText)
    {
        // Arrange
        var material = MakeMaterial(1, "Material", 1m, type, 10m);

        // Act
        var dto = PackingMaterialMapper.ToDto(material, null);

        // Assert
        Assert.Equal(expectedText, dto.ConsumptionTypeText);
    }
}
