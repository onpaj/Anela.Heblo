using System.Net;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class FlexiManufactureTemplateServiceTests
{
    private readonly Mock<IBoMClient> _mockBomClient = new();
    private readonly Mock<IErpStockClient> _mockStockClient = new();
    private readonly Mock<ILogger<FlexiManufactureTemplateService>> _mockLogger = new();
    private readonly Mock<ITelemetryService> _mockTelemetry = new();
    private readonly PassthroughTemplateCache _passthroughCache = new();
    private readonly FlexiManufactureTemplateService _service;

    public FlexiManufactureTemplateServiceTests()
    {
        _service = new FlexiManufactureTemplateService(
            _mockBomClient.Object,
            _mockStockClient.Object,
            TimeProvider.System,
            _passthroughCache,
            _mockTelemetry.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_When501Returned_LogsErrorAndRethrows()
    {
        var exception = new HttpRequestException("NotImplemented", null, HttpStatusCode.NotImplemented);
        _mockBomClient
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var act = async () => await _service.GetManufactureTemplateAsync("PROD-001", CancellationToken.None);

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

    /// <summary>
    /// Test cache that always invokes the fetcher (acts as pass-through so we test
    /// the inner fetch logic directly).
    /// </summary>
    private sealed class PassthroughTemplateCache : IManufactureTemplateCache
    {
        public int Calls { get; private set; }

        public async Task<ManufactureTemplate?> GetOrFetchAsync(
            string productCode,
            Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
            CancellationToken cancellationToken)
        {
            Calls++;
            return await fetch(cancellationToken);
        }
    }
}
