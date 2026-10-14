### task: add-marginleveldto-factory

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginLevelDtoTests.cs` (new file)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginLevelDtoTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class MarginLevelDtoTests
{
    [Fact]
    public void FromDomain_CopiesAllFourFieldsVerbatim()
    {
        // Arrange
        var level = new MarginLevel(percentage: 12.34m, amount: 56.78m, costTotal: 90.12m, costLevel: 3.45m);

        // Act
        var dto = MarginLevelDto.FromDomain(level);

        // Assert
        dto.Percentage.Should().Be(12.34m);
        dto.Amount.Should().Be(56.78m);
        dto.CostTotal.Should().Be(90.12m);
        dto.CostLevel.Should().Be(3.45m);
    }

    [Fact]
    public void FromDomain_ZeroLevel_ProducesZeroDto()
    {
        // Arrange
        var level = MarginLevel.Zero;

        // Act
        var dto = MarginLevelDto.FromDomain(level);

        // Assert
        dto.Percentage.Should().Be(0m);
        dto.Amount.Should().Be(0m);
        dto.CostTotal.Should().Be(0m);
        dto.CostLevel.Should().Be(0m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarginLevelDtoTests"`
Expected: FAIL to compile — `MarginLevelDto` does not contain a definition for `FromDomain`.

- [ ] **Step 3: Add the factory method**

In `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs`, add the `using` and the method so the file reads:

```csharp
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Represents margin data for a specific margin level (M0, M1, or M2)
/// </summary>
public class MarginLevelDto
{
    /// <summary>
    /// Margin percentage at this level
    /// </summary>
    [JsonPropertyName("percentage")]
    public decimal Percentage { get; set; }

    /// <summary>
    /// Absolute margin amount at this level (in currency)
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Cost specific to this level only (incremental cost)
    /// </summary>
    [JsonPropertyName("costLevel")]
    public decimal CostLevel { get; set; }

    /// <summary>
    /// Cumulative cost up to and including this level
    /// </summary>
    [JsonPropertyName("costTotal")]
    public decimal CostTotal { get; set; }

    /// <summary>
    /// Maps a domain margin-level value to its DTO. Centralizes the field-by-field copy
    /// used at every M0-M3 call site across the Catalog module's margin handlers.
    /// </summary>
    public static MarginLevelDto FromDomain(MarginLevel level) => new()
    {
        Percentage = level.Percentage,
        Amount = level.Amount,
        CostLevel = level.CostLevel,
        CostTotal = level.CostTotal
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarginLevelDtoTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs backend/test/Anela.Heblo.Tests/Features/Catalog/MarginLevelDtoTests.cs
git commit -m "feat(catalog): add MarginLevelDto.FromDomain mapping factory"
```
