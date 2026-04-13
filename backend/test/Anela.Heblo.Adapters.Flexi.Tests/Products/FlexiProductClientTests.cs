using System.Net;
using Anela.Heblo.Adapters.Flexi.Products;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Products;

public class FlexiProductClientTests
{
    private readonly Mock<IBoMClient> _mockBomClient;
    private readonly Mock<IPriceListClient> _mockPriceListClient;
    private readonly Mock<ILogger<FlexiProductClient>> _mockLogger;
    private readonly FlexiProductClient _client;

    public FlexiProductClientTests()
    {
        _mockBomClient = new Mock<IBoMClient>();
        _mockPriceListClient = new Mock<IPriceListClient>();
        _mockLogger = new Mock<ILogger<FlexiProductClient>>();

        _client = new FlexiProductClient(
            _mockBomClient.Object,
            _mockPriceListClient.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task RefreshProductWeight_When501Returned_LogsErrorAndRethrows()
    {
        // Arrange
        var exception = new HttpRequestException("NotImplemented", null, HttpStatusCode.NotImplemented);
        _mockBomClient
            .Setup(x => x.GetBomWeight(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _client.RefreshProductWeight("PROD-001", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("501") && v.ToString()!.Contains("kusovnik")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
