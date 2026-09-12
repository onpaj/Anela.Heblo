using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ClearFlagOverride;

public class ClearFlagOverrideHandlerTests
{
    private readonly Mock<IFeatureFlagOverrideRepository> _repoMock;
    private readonly Mock<IMemoryCache> _cacheMock;
    private readonly ClearFlagOverrideHandler _handler;

    public ClearFlagOverrideHandlerTests()
    {
        _repoMock = new Mock<IFeatureFlagOverrideRepository>();
        _cacheMock = new Mock<IMemoryCache>();
        _handler = new ClearFlagOverrideHandler(_repoMock.Object, _cacheMock.Object);
    }

    [Fact]
    public async Task Handle_OverrideNotFound_ReturnsResourceNotFound_AndDoesNotTouchCache()
    {
        var request = new ClearFlagOverrideRequest { Key = "some-flag-key" };
        _repoMock.Setup(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repoMock.Verify(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.Remove(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OverrideDeleted_InvalidatesCache_AndReturnsSuccess()
    {
        var request = new ClearFlagOverrideRequest { Key = "some-flag-key" };
        _repoMock.Setup(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        _repoMock.Verify(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.Remove(HebloFeatureProvider.CacheKey), Times.Once);
    }
}
