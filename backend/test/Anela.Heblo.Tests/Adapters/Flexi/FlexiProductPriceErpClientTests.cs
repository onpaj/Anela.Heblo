using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Xcc.Audit;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Client.ResultFilters;

namespace Anela.Heblo.Tests.Adapters.Flexi;

public class FlexiProductPriceErpClientTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IResultHandler> _resultHandlerMock;
    private readonly Mock<IMemoryCache> _memoryCacheMock;
    private readonly Mock<ILogger<ReceivedInvoiceClient>> _loggerMock;
    private readonly Mock<IDataLoadAuditService> _auditServiceMock;
    private readonly Mock<IBoMClient> _bomClientMock;
    private readonly FlexiBeeSettings _flexiBeeSettings;
    private readonly FlexiProductPriceErpClient _client;

    public FlexiProductPriceErpClientTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _resultHandlerMock = new Mock<IResultHandler>();
        _memoryCacheMock = new Mock<IMemoryCache>();
        _loggerMock = new Mock<ILogger<ReceivedInvoiceClient>>();
        _auditServiceMock = new Mock<IDataLoadAuditService>();
        _bomClientMock = new Mock<IBoMClient>();
        
        _flexiBeeSettings = new FlexiBeeSettings
        {
            Server = "test.flexibee.com",
            Company = "test_company",
            Login = "test_user",
            Password = "test_password"
        };

        _client = new FlexiProductPriceErpClient(
            _flexiBeeSettings,
            _httpClientFactoryMock.Object,
            _resultHandlerMock.Object,
            _memoryCacheMock.Object,
            _loggerMock.Object,
            _auditServiceMock.Object,
            _bomClientMock.Object);
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithValidBomId_ShouldCallBoMClient()
    {
        // Arrange
        const int bomId = 123;

        _bomClientMock.Setup(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _client.RecalculatePurchasePrice(bomId, CancellationToken.None);

        // Assert
        _bomClientMock.Verify(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithNegativeBomId_ShouldCallBoMClient()
    {
        // Arrange
        const int bomId = -123;

        _bomClientMock.Setup(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _client.RecalculatePurchasePrice(bomId, CancellationToken.None);

        // Assert
        _bomClientMock.Verify(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithZeroBomId_ShouldCallBoMClient()
    {
        // Arrange
        const int bomId = 0;

        _bomClientMock.Setup(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _client.RecalculatePurchasePrice(bomId, CancellationToken.None);

        // Assert
        _bomClientMock.Verify(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithLargeBomId_ShouldCallBoMClient()
    {
        // Arrange
        const int bomId = int.MaxValue;

        _bomClientMock.Setup(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _client.RecalculatePurchasePrice(bomId, CancellationToken.None);

        // Assert
        _bomClientMock.Verify(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithBoMClientException_ShouldWrapException()
    {
        // Arrange
        const int bomId = 123;
        var originalException = new HttpRequestException("FlexiBee API error");

        _bomClientMock.Setup(x => x.RecalculatePurchasePrice(bomId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(originalException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _client.RecalculatePurchasePrice(bomId, CancellationToken.None));

        exception.Message.Should().Contain("Failed to recalculate purchase price for BoM ID 123");
        exception.Message.Should().Contain("FlexiBee API error");
        exception.InnerException.Should().Be(originalException);
    }

    [Fact]
    public async Task RecalculatePurchasePrice_WithCancellationToken_ShouldPassTokenToBoMClient()
    {
        // Arrange
        const int bomId = 123;
        var cancellationToken = new CancellationToken();

        _bomClientMock.Setup(x => x.RecalculatePurchasePrice(bomId, cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _client.RecalculatePurchasePrice(bomId, cancellationToken);

        // Assert
        _bomClientMock.Verify(x => x.RecalculatePurchasePrice(bomId, cancellationToken), Times.Once);
    }
}