using Anela.Heblo.Adapters.Flexi.Lots;
using FluentAssertions;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;
using Rem.FlexiBeeSDK.Model.Products.Lots;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Lots;

public class FlexiLotsClientTests
{
    private readonly Mock<ILotsClient> _mockSdkClient;
    private readonly FlexiLotsClient _client;

    public FlexiLotsClientTests()
    {
        _mockSdkClient = new Mock<ILotsClient>();
        _client = new FlexiLotsClient(_mockSdkClient.Object);
    }

    [Fact]
    public async Task GetAsync_ReturnsEmptyList_WhenSdkThrowsLotsItemNotFound()
    {
        // Arrange
        _mockSdkClient
            .Setup(x => x.GetAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Entity LotsItem not found"));

        // Act
        var result = await _client.GetAsync("INGR-001");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_PropagatesOtherKeyNotFoundExceptions()
    {
        // Arrange
        _mockSdkClient
            .Setup(x => x.GetAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Some other entity not found"));

        // Act
        var act = async () => await _client.GetAsync("INGR-001");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Some other entity not found");
    }

    [Fact]
    public async Task GetAsync_MapsProductLotsToCorrectCatalogLots()
    {
        // Arrange
        var expiration = new DateTime(2025, 12, 31);
        var sdkLots = new List<ProductLot>
        {
            new() { Id = 1, ProductCode = "INGR-001", Amount = 5.5m, Expiration = expiration, Lot = "LOT-A" },
            new() { Id = 2, ProductCode = "INGR-001", Amount = 3.0m, Expiration = null, Lot = null }
        };

        _mockSdkClient
            .Setup(x => x.GetAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sdkLots);

        // Act
        var result = await _client.GetAsync("INGR-001");

        // Assert
        result.Should().HaveCount(2);

        result[0].Id.Should().Be(1);
        result[0].ProductCode.Should().Be("INGR-001");
        result[0].Amount.Should().Be(5.5m);
        result[0].Expiration.Should().Be(DateOnly.FromDateTime(expiration.Date));
        result[0].Lot.Should().Be("LOT-A");

        result[1].Id.Should().Be(2);
        result[1].Expiration.Should().BeNull();
        result[1].Lot.Should().BeNull();
    }
}
