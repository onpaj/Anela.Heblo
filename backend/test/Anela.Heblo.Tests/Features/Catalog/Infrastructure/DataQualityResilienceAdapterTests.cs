using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityResilienceAdapterTests
{
    private readonly Mock<ICatalogResilienceService> _resilienceService = new();

    private DataQualityResilienceAdapter CreateAdapter() => new(_resilienceService.Object);

    [Fact]
    public async Task ExecuteWithResilienceAsync_DelegatesToUnderlyingService_WithSameArgumentsAndReturnValue()
    {
        // Arrange
        Func<CancellationToken, Task<int>> operation = _ => Task.FromResult(42);
        const string operationName = "TestOperation";
        using var cts = new CancellationTokenSource();

        _resilienceService
            .Setup(r => r.ExecuteWithResilienceAsync(operation, operationName, cts.Token))
            .ReturnsAsync(42);

        // Act
        var result = await CreateAdapter().ExecuteWithResilienceAsync(operation, operationName, cts.Token);

        // Assert
        result.Should().Be(42);
        _resilienceService.Verify(
            r => r.ExecuteWithResilienceAsync(operation, operationName, cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_PropagatesException_WhenUnderlyingServiceThrows()
    {
        // Arrange
        Func<CancellationToken, Task<int>> operation = _ => Task.FromResult(0);
        const string operationName = "FailingOperation";

        _resilienceService
            .Setup(r => r.ExecuteWithResilienceAsync(operation, operationName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var act = () => CreateAdapter().ExecuteWithResilienceAsync(operation, operationName, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
