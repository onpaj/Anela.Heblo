using System.Net;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class FlexiManufactureTemplateServiceTests
{
    private readonly Mock<IBoMClient> _mockBomClient;
    private readonly Mock<IErpStockClient> _mockStockClient;
    private readonly Mock<ILogger<FlexiManufactureTemplateService>> _mockLogger;
    private readonly FlexiManufactureTemplateService _service;

    public FlexiManufactureTemplateServiceTests()
    {
        _mockBomClient = new Mock<IBoMClient>();
        _mockStockClient = new Mock<IErpStockClient>();
        _mockLogger = new Mock<ILogger<FlexiManufactureTemplateService>>();

        _service = new FlexiManufactureTemplateService(
            _mockBomClient.Object,
            _mockStockClient.Object,
            TimeProvider.System,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetManufactureTemplateAsync_When501Returned_LogsErrorAndRethrows()
    {
        // Arrange
        var exception = new HttpRequestException("NotImplemented", null, HttpStatusCode.NotImplemented);
        _mockBomClient
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _service.GetManufactureTemplateAsync("PROD-001", CancellationToken.None);

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
