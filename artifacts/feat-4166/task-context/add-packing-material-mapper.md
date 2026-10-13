### task: add-packing-material-mapper

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialMapperTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialMapperTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialMapperTests"`
Expected: FAIL — build error, `PackingMaterialMapper` does not exist (`CS0246: The type or namespace name 'PackingMaterialMapper' could not be found`).

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;

namespace Anela.Heblo.Application.Features.PackingMaterials.Mapping;

internal static class PackingMaterialMapper
{
    public static PackingMaterialDto ToDto(PackingMaterial material, decimal? forecastedDays) => new()
    {
        Id = material.Id,
        Name = material.Name,
        ConsumptionRate = material.ConsumptionRate,
        ConsumptionType = material.ConsumptionType,
        ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
        CurrentQuantity = material.CurrentQuantity,
        ForecastedDays = forecastedDays,
        CreatedAt = material.CreatedAt,
        UpdatedAt = material.UpdatedAt
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialMapperTests"`
Expected: PASS — 5 tests passed (1 + 1 + 3 theory cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialMapperTests.cs
git commit -m "feat(packing-materials): add PackingMaterialMapper"
```

---

