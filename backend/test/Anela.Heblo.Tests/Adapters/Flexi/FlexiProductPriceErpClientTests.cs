using Anela.Heblo.Adapters.Flexi.Price;
using Anela.Heblo.Domain.Features.Catalog.Price;
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
    private readonly Mock<ILogger<FlexiProductPriceErpClient>> _clientLoggerMock;
    private readonly Mock<IBoMClient> _bomClientMock;
    private readonly FlexiBeeSettings _flexiBeeSettings;
    private readonly FlexiProductPriceErpClient _client;

    public FlexiProductPriceErpClientTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _resultHandlerMock = new Mock<IResultHandler>();
        _memoryCacheMock = new Mock<IMemoryCache>();
        _loggerMock = new Mock<ILogger<ReceivedInvoiceClient>>();
        _clientLoggerMock = new Mock<ILogger<FlexiProductPriceErpClient>>();
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
            _bomClientMock.Object,
            _clientLoggerMock.Object);
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

    [Fact]
    public async Task GetAllAsync_WhenInternalTimeoutCancels_LogsWarningAndRethrows()
    {
        // Arrange — HttpClient internal timeout: its own CTS fires, not the caller's
        var timeoutCts = new CancellationTokenSource();
        var timeoutException = new TaskCanceledException("HttpClient timeout", null, timeoutCts.Token);
        await timeoutCts.CancelAsync();

        // Cache miss
        object? cacheOut = null;
        _memoryCacheMock.Setup(x => x.TryGetValue(It.IsAny<object>(), out cacheOut)).Returns(false);

        // HTTP client throws timeout exception
        var handler = new ThrowingHttpMessageHandler(timeoutException);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.flexibee.com/") };
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        // Act — caller's token is NOT canceled
        var act = async () => await _client.GetAllAsync(false, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _clientLoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("timed out")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_WhenCallerCancels_LogsInformationAndRethrows()
    {
        // Arrange — caller cancels via their own token
        using var callerCts = new CancellationTokenSource();
        var canceledException = new TaskCanceledException("Caller canceled", null, callerCts.Token);
        await callerCts.CancelAsync();

        // Cache miss
        object? cacheOut = null;
        _memoryCacheMock.Setup(x => x.TryGetValue(It.IsAny<object>(), out cacheOut)).Returns(false);

        // HTTP client throws caller cancellation exception
        var handler = new ThrowingHttpMessageHandler(canceledException);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.flexibee.com/") };
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        // Act — same token is canceled
        var act = async () => await _client.GetAllAsync(false, callerCts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _clientLoggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("canceled by the caller")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void MapToProductPrices_WhenPriceIncludesVat_DerivesPriceWithoutVatByDividing()
    {
        // Arrange: typCenyDphK = typCeny.sDph means cenaZakl IS the with-VAT price.
        // Grossing it up (today's default behavior) would double-count VAT.
        var dto = new ProductPriceFlexiDto
        {
            ProductId = 1,
            ProductCode = "A",
            Price = 447.70m,
            PurchasePrice = 0m,
            VatLevel = "základní",
            ProductType = "Zboží",
            TypCenyDphK = "typCeny.sDph",
        };

        // Act
        var result = _client.MapToProductPrices(new[] { dto }).Single();

        // Assert
        result.PriceWithVat.Should().Be(447.70m);
        result.PriceWithoutVat.Should().Be(370.00m);
        _clientLoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void MapToProductPrices_WhenPriceExcludesVat_GrossesUpAsBefore()
    {
        // Arrange: typCenyDphK = typCeny.bezDph is today's observed real-item value.
        var dto = new ProductPriceFlexiDto
        {
            ProductId = 1,
            ProductCode = "A",
            Price = 370.00m,
            PurchasePrice = 0m,
            VatLevel = "základní",
            ProductType = "Zboží",
            TypCenyDphK = "typCeny.bezDph",
        };

        // Act
        var result = _client.MapToProductPrices(new[] { dto }).Single();

        // Assert
        result.PriceWithoutVat.Should().Be(370.00m);
        result.PriceWithVat.Should().Be(447.70m);
    }

    [Fact]
    public void MapToProductPrices_WhenPriceTypeIsAbsent_AssumesExclVatAndWarnsOnce()
    {
        // Arrange: user query 41 may not expose typCenyDphK at all. Treat as bezDph
        // (today's behavior) but log a warning — never assume silently. Two items missing
        // the field must warn only once for the whole batch.
        var dtoA = new ProductPriceFlexiDto
        {
            ProductId = 1,
            ProductCode = "A",
            Price = 370.00m,
            PurchasePrice = 0m,
            VatLevel = "základní",
            ProductType = "Zboží",
            TypCenyDphK = null,
        };
        var dtoB = new ProductPriceFlexiDto
        {
            ProductId = 2,
            ProductCode = "B",
            Price = 100.00m,
            PurchasePrice = 0m,
            VatLevel = "základní",
            ProductType = "Zboží",
            TypCenyDphK = null,
        };

        // Act
        var results = _client.MapToProductPrices(new[] { dtoA, dtoB }).ToList();

        // Assert
        results[0].PriceWithoutVat.Should().Be(370.00m);
        results[0].PriceWithVat.Should().Be(447.70m);
        results[1].PriceWithoutVat.Should().Be(100.00m);
        _clientLoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("typCenyDphK")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_WithoutForceReload_ServesTheCachedEntry()
    {
        // Arrange: a live cache entry and an HTTP client that would blow up if used.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(FlexiProductPriceErpClient.CacheKey, new List<ProductPriceFlexiDto>
        {
            new()
            {
                ProductId = 1,
                ProductCode = "A",
                Price = 100m,
                PurchasePrice = 0m,
                VatLevel = "dphZakl",
                ProductType = "Zbo\u017e\u00ed",
                TypCenyDphK = "typCeny.bezDph",
            },
        });
        var client = CreateClientWith(cache, new ThrowingHttpMessageHandler(new HttpRequestException("network")));

        // Act
        var result = await client.GetAllAsync(false, CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.ProductCode.Should().Be("A");
    }

    [Fact]
    public async Task GetAllAsync_WithForceReload_BypassesTheCachedEntry()
    {
        // Arrange: forceReload was accepted and silently dropped, so nothing could ever get
        // a fresh read out of this client. Same cache entry as above; reaching the network
        // is what proves the cache was bypassed.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(FlexiProductPriceErpClient.CacheKey, new List<ProductPriceFlexiDto>
        {
            new()
            {
                ProductId = 1,
                ProductCode = "A",
                Price = 100m,
                PurchasePrice = 0m,
                VatLevel = "dphZakl",
                ProductType = "Zbo\u017e\u00ed",
                TypCenyDphK = "typCeny.bezDph",
            },
        });
        var client = CreateClientWith(cache, new ThrowingHttpMessageHandler(new HttpRequestException("network")));

        // Act
        var act = async () => await client.GetAllAsync(true, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private FlexiProductPriceErpClient CreateClientWith(IMemoryCache cache, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.flexibee.com/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        return new FlexiProductPriceErpClient(
            _flexiBeeSettings,
            factory.Object,
            _resultHandlerMock.Object,
            cache,
            _loggerMock.Object,
            _bomClientMock.Object,
            _clientLoggerMock.Object);
    }

    [Fact]
    public void MapToProductPrices_CarriesTheRecognisedVatBandThrough()
    {
        // Arrange: a genuinely 12% item. The write path must see 12, not a rate recovered
        // arithmetically from prices Heblo itself grossed up.
        var dto = new ProductPriceFlexiDto
        {
            ProductId = 1,
            ProductCode = "A",
            Price = 100.00m,
            PurchasePrice = 0m,
            VatLevel = "typSzbDph.dphSniz",
            ProductType = "Zbo\u017e\u00ed",
            TypCenyDphK = "typCeny.bezDph",
        };

        // Act
        var result = _client.MapToProductPrices(new[] { dto }).Single();

        // Assert
        result.VatRate.Should().Be(12m);
        result.PriceWithVat.Should().Be(112.00m);
    }

    [Fact]
    public void MapToProductPrices_WhenTheVatBandIsUnrecognised_ReportsNoRateButKeepsTheReadPathAt21()
    {
        // Arrange
        var dto = new ProductPriceFlexiDto
        {
            ProductId = 1,
            ProductCode = "A",
            Price = 100.00m,
            PurchasePrice = 0m,
            VatLevel = "ovobozeno",
            ProductType = "Zbo\u017e\u00ed",
            TypCenyDphK = "typCeny.bezDph",
        };

        // Act
        var result = _client.MapToProductPrices(new[] { dto }).Single();

        // Assert: the comparison screen still sees today's numbers; only the write path
        // learns that the band is unknown.
        result.VatRate.Should().BeNull();
        result.PriceWithVat.Should().Be(121.00m);
    }

    [Fact]
    public void MapToProductPrices_WhenTheVatBandIsUnrecognised_LogsTheRawValueOncePerRun()
    {
        // Arrange: the first live run has to tell us which vocabulary query 41 really uses,
        // and must not flood the log with one line per catalogue row.
        var dtoA = new ProductPriceFlexiDto
        {
            ProductId = 1,
            ProductCode = "A",
            Price = 100.00m,
            PurchasePrice = 0m,
            VatLevel = "ovobozeno",
            ProductType = "Zbo\u017e\u00ed",
            TypCenyDphK = "typCeny.bezDph",
        };
        var dtoB = new ProductPriceFlexiDto
        {
            ProductId = 2,
            ProductCode = "B",
            Price = 200.00m,
            PurchasePrice = 0m,
            VatLevel = "ovobozeno",
            ProductType = "Zbo\u017e\u00ed",
            TypCenyDphK = "typCeny.bezDph",
        };

        // Act
        _client.MapToProductPrices(new[] { dtoA, dtoB }).ToList();

        // Assert
        _clientLoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("ovobozeno")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public ThrowingHttpMessageHandler(Exception exception) => _exception = exception;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _exception;
    }
}
