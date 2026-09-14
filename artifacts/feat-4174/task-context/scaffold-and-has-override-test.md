### task: scaffold-and-has-override-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs`

This task creates the test file with its scaffold (mocks, constructor stub, `CreateHandler` helper) plus the first test, covering spec FR-1 (has-override path).

- [ ] **Step 1: Write the test file with the class scaffold and the has-override test**

Create `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;
using Anela.Heblo.Domain.Features.FeatureFlags;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ListFlags;

public class ListFlagsHandlerTests
{
    private readonly Mock<IFeatureFlagOverrideRepository> _repoMock = new();
    private readonly Mock<IFeatureFlagChecker> _checkerMock = new();

    public ListFlagsHandlerTests()
    {
        // Every registry entry is evaluated via Task.WhenAll inside Handle, so every
        // key must resolve. CurrentValue is irrelevant to the IsOverridden branch under
        // test here (see spec.r1.md FR-4), so a single any-key stub covers all of them.
        _checkerMock
            .Setup(c => c.IsEnabledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, bool defaultValue, CancellationToken ct) => defaultValue);
    }

    private ListFlagsHandler CreateHandler() => new(_repoMock.Object, _checkerMock.Object);

    [Fact]
    public async Task Handle_FlagHasMatchingOverride_SetsIsOverriddenTrueAndPopulatesAuthorAndDate()
    {
        var updatedAt = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>
            {
                new()
                {
                    Key = FeatureFlagKeys.LabelPrintingEnabled,
                    IsEnabled = false,
                    UpdatedBy = "jane@example.com",
                    UpdatedAt = updatedAt,
                },
            });

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.IsOverridden.Should().BeTrue();
        dto.UpdatedBy.Should().Be("jane@example.com");
        dto.UpdatedAt.Should().Be(updatedAt);
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"`

Expected: build succeeds, `Passed! - Failed: 0, Passed: 1, Skipped: 0` (exactly one test collected and passing so far).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs
git commit -m "test(feature-flags): cover ListFlagsHandler has-override path (#4174)"
```

---
