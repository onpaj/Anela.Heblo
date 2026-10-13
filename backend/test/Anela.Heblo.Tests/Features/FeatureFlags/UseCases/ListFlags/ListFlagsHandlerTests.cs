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

    [Fact]
    public async Task Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>());

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.IsOverridden.Should().BeFalse();
        dto.UpdatedBy.Should().BeNull();
        dto.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>
            {
                new()
                {
                    Key = FeatureFlagKeys.LabelPrintingEnabled.ToUpperInvariant(),
                    IsEnabled = false,
                    UpdatedBy = "jane@example.com",
                    UpdatedAt = DateTime.UtcNow,
                },
            });

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.IsOverridden.Should().BeFalse();
        dto.UpdatedBy.Should().BeNull();
        dto.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AlwaysReturnsOneDtoPerRegisteredFlag_WithFieldsCopiedFromDefinition()
    {
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FeatureFlagOverride>());

        var response = await CreateHandler().Handle(new ListFlagsRequest(), CancellationToken.None);

        response.Flags.Should().HaveCount(FeatureFlagRegistry.All.Count);

        var definition = FeatureFlagRegistry.ByKey[FeatureFlagKeys.LabelPrintingEnabled];
        var dto = response.Flags.Single(f => f.Key == FeatureFlagKeys.LabelPrintingEnabled);
        dto.Description.Should().Be(definition.Description);
        dto.DefaultValue.Should().Be(definition.DefaultValue);
    }
}
