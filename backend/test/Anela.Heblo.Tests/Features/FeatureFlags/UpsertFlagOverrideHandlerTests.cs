using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

public class UpsertFlagOverrideHandlerTests
{
    private readonly Mock<IFeatureFlagOverrideRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private UpsertFlagOverrideHandler CreateHandler() =>
        new(_repo.Object, _cache, _currentUserService.Object);

    [Fact]
    public async Task Handle_AuthenticatedUser_PersistsResolvedDisplayNameAsUpdatedBy()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Jane Admin", "jane@example.com", IsAuthenticated: true));

        var response = await CreateHandler().Handle(
            new UpsertFlagOverrideRequest { Key = FeatureFlagKeys.LabelPrintingEnabled, IsEnabled = false },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _repo.Verify(r => r.UpsertAsync(
            FeatureFlagKeys.LabelPrintingEnabled, false, "Jane Admin", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnauthenticatedCurrentUser_PersistsSystemFallback()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser(null, null, null, IsAuthenticated: false));

        var response = await CreateHandler().Handle(
            new UpsertFlagOverrideRequest { Key = FeatureFlagKeys.LabelPrintingEnabled, IsEnabled = true },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _repo.Verify(r => r.UpsertAsync(
            FeatureFlagKeys.LabelPrintingEnabled, true, "System", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownKey_ReturnsResourceNotFoundAndDoesNotCallRepo()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Jane Admin", "jane@example.com", IsAuthenticated: true));

        var response = await CreateHandler().Handle(
            new UpsertFlagOverrideRequest { Key = "does-not-exist", IsEnabled = true },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repo.Verify(r => r.UpsertAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
