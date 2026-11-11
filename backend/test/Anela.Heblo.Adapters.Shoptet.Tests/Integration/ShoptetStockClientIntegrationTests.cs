using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

/// <summary>Read-only supplies listing against the Shoptet TEST store. Never point at production.</summary>
[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetStockClientIntegrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IEshopStockClient _stockClient;

    public ShoptetStockClientIntegrationTests(ShoptetIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _stockClient = fixture.ServiceProvider.GetRequiredService<IEshopStockClient>();
    }

    [Fact]
    public async Task ListAsync_TestStore_ReturnsCodesStockAndLocation()
    {
        // Arrange
        ShoptetTestGuard.Assert(_fixture.Configuration);
        var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

        // Act
        var result = await _stockClient.ListAsync(cancellationToken);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().OnlyContain(s => !string.IsNullOrEmpty(s.Code));
        result.Should().OnlyContain(s => s.Location != null);
        result.Select(s => s.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ListAsync_WithCancelledToken_RespectsCancellation()
    {
        // Arrange
        ShoptetTestGuard.Assert(_fixture.Configuration);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await _stockClient.ListAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
