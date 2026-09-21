using System.Net;
using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Application.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureHistoryClientTests
{
    private readonly Mock<IStockItemsMovementClient> _mockMovementClient;
    private readonly Mock<ILogger<FlexiManufactureHistoryClient>> _mockLogger;
    private readonly FlexiManufactureHistoryClient _client;

    // Legacy (pre 2026-03-24) and current ids for semi-product and product receipts.
    private const int LegacySemiProductTypeId = 54;
    private const int LegacyProductTypeId = 56;
    private const int CurrentSemiProductTypeId = 65;
    private const int CurrentProductTypeId = 67;

    public FlexiManufactureHistoryClientTests()
    {
        _mockMovementClient = new Mock<IStockItemsMovementClient>();
        _mockLogger = new Mock<ILogger<FlexiManufactureHistoryClient>>();

        var options = Options.Create(new DataSourceOptions
        {
            ManufactureDocumentTypeIds = new[]
            {
                LegacySemiProductTypeId, LegacyProductTypeId, CurrentSemiProductTypeId, CurrentProductTypeId
            }
        });

        _client = new FlexiManufactureHistoryClient(
            _mockMovementClient.Object,
            options,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetHistoryAsync_WhenInternalTimeoutCancels_LogsWarningAndRethrows()
    {
        // Arrange — HttpClient internal timeout (not caller's token)
        var timeoutCts = new CancellationTokenSource();
        var timeoutException = new TaskCanceledException("HttpClient timeout", null, timeoutCts.Token);
        await timeoutCts.CancelAsync();

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(timeoutException);

        // Act — caller's token is NOT canceled
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, cancellationToken: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("timed out")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_WhenCallerCancels_LogsInformationAndRethrows()
    {
        // Arrange — caller cancels via their own token
        using var callerCts = new CancellationTokenSource();
        var canceledException = new TaskCanceledException("Caller canceled", null, callerCts.Token);
        await callerCts.CancelAsync();

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(canceledException);

        // Act — same token is canceled
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, cancellationToken: callerCts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("canceled by the caller")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_When503ThenSucceeds_RetriesAndReturnsResult()
    {
        // Arrange
        var transient = new HttpRequestException(
            "Service Unavailable",
            inner: null,
            statusCode: HttpStatusCode.ServiceUnavailable);

        _mockMovementClient
            .SetupSequence(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(transient)
            .ThrowsAsync(transient)
            .ReturnsAsync(new List<StockItemMovementFlexiDto>())
            .ReturnsAsync(new List<StockItemMovementFlexiDto>())
            .ReturnsAsync(new List<StockItemMovementFlexiDto>())
            .ReturnsAsync(new List<StockItemMovementFlexiDto>());

        // Act
        var result = await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert — 3 calls for the first document type (2 retries + success),
        // then 1 each for the remaining 3 configured types = 6.
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(6));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Retry attempt")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetHistoryAsync_When503Persists_LogsWarningAndRethrowsAfterRetries()
    {
        // Arrange
        var transient = new HttpRequestException(
            "Service Unavailable",
            inner: null,
            statusCode: HttpStatusCode.ServiceUnavailable);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(transient);

        // Act
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("returned transient")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_When502Persists_RetriesAndLogsWarning()
    {
        // Arrange
        var transient = new HttpRequestException(
            "Bad Gateway",
            inner: null,
            statusCode: HttpStatusCode.BadGateway);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(transient);

        // Act
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("returned transient")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_When400_DoesNotRetry_LogsErrorAndRethrows()
    {
        // Arrange
        var nonTransient = new HttpRequestException(
            "Bad Request",
            inner: null,
            statusCode: HttpStatusCode.BadRequest);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(nonTransient);

        // Act
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("FlexiBee skladovy-pohyb-polozka returned") && !v.ToString()!.Contains("transient")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_When500_DoesNotRetry_LogsErrorAndRethrows()
    {
        // Arrange
        var nonTransient = new HttpRequestException(
            "Internal Server Error",
            inner: null,
            statusCode: HttpStatusCode.InternalServerError);

        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(nonTransient);

        // Act
        var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("FlexiBee skladovy-pohyb-polozka returned") && !v.ToString()!.Contains("transient")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_UnionsLegacyAndCurrentProductReceiptTypes()
    {
        // Arrange — FlexiBee renamed the product-receipt document type on 2026-03-24:
        // 56 VYROBA-PRODUKT (retired) -> 67 V-PRIJEM-VYROBEK (current).
        var legacyDate = new DateTime(2026, 3, 1);
        var currentDate = new DateTime(2026, 5, 14);

        SetupMovementsForType(LegacySemiProductTypeId);
        SetupMovementsForType(CurrentSemiProductTypeId);
        SetupMovementsForType(LegacyProductTypeId,
            BuildMovement("SER003030", legacyDate, amount: 10, pricePerUnit: 5, totalSum: 50));
        SetupMovementsForType(CurrentProductTypeId,
            BuildMovement("SER003030", currentDate, amount: 84, pricePerUnit: 7, totalSum: 588));

        // Act
        var result = await _client.GetHistoryAsync(new DateTime(2026, 1, 1), new DateTime(2026, 9, 21));

        // Assert — both the pre-cutover and the post-cutover receipt are returned
        result.Should().HaveCount(2);
        result.Should().ContainSingle(r => r.Date == legacyDate && r.Amount == 10);
        result.Should().ContainSingle(r => r.Date == currentDate && r.Amount == 84);
    }

    [Fact]
    public async Task GetHistoryAsync_UnionsLegacyAndCurrentSemiProductReceiptTypes()
    {
        // Arrange — the semi-product receipt type was renumbered in the same cutover:
        // 54 VYROBA-POLOTOVAR (retired) -> 65 V-PRIJEM-POLOTOVAR (current).
        var legacyDate = new DateTime(2026, 2, 10);
        var currentDate = new DateTime(2026, 6, 3);

        SetupMovementsForType(LegacyProductTypeId);
        SetupMovementsForType(CurrentProductTypeId);
        SetupMovementsForType(LegacySemiProductTypeId,
            BuildMovement("POL001000", legacyDate, amount: 20, pricePerUnit: 3, totalSum: 60));
        SetupMovementsForType(CurrentSemiProductTypeId,
            BuildMovement("POL001000", currentDate, amount: 45, pricePerUnit: 4, totalSum: 180));

        // Act
        var result = await _client.GetHistoryAsync(new DateTime(2026, 1, 1), new DateTime(2026, 9, 21));

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainSingle(r => r.Date == legacyDate && r.Amount == 20);
        result.Should().ContainSingle(r => r.Date == currentDate && r.Amount == 45);
    }

    [Fact]
    public async Task GetHistoryAsync_MergesSameProductAndDayAcrossDocumentTypes()
    {
        // Arrange — the cutover day itself can carry receipts of both types for one product.
        // Grouping keys on date + code, so the two streams must merge into a single record.
        var cutoverDate = new DateTime(2026, 3, 24);

        SetupMovementsForType(LegacySemiProductTypeId);
        SetupMovementsForType(CurrentSemiProductTypeId);
        SetupMovementsForType(LegacyProductTypeId,
            BuildMovement("SER003030", cutoverDate, amount: 10, pricePerUnit: 5, totalSum: 50));
        SetupMovementsForType(CurrentProductTypeId,
            BuildMovement("SER003030", cutoverDate, amount: 6, pricePerUnit: 7, totalSum: 42));

        // Act
        var result = await _client.GetHistoryAsync(new DateTime(2026, 1, 1), new DateTime(2026, 9, 21));

        // Assert — one merged record, amounts summed, not duplicated
        result.Should().ContainSingle();
        result[0].Amount.Should().Be(16);
        result[0].PriceTotal.Should().Be(92m);
        result[0].PricePerPiece.Should().Be(6m);
    }

    [Fact]
    public async Task GetHistoryAsync_QueriesEveryConfiguredDocumentType()
    {
        // Arrange — the configured id set is what drives the queries, so an ERP-side
        // renumbering is a config edit rather than a code change.
        SetupMovementsForType(LegacySemiProductTypeId);
        SetupMovementsForType(LegacyProductTypeId);
        SetupMovementsForType(CurrentSemiProductTypeId);
        SetupMovementsForType(CurrentProductTypeId);

        // Act
        await _client.GetHistoryAsync(new DateTime(2026, 1, 1), new DateTime(2026, 9, 21));

        // Assert
        foreach (var documentTypeId in new[]
                 {
                     LegacySemiProductTypeId, LegacyProductTypeId, CurrentSemiProductTypeId, CurrentProductTypeId
                 })
        {
            _mockMovementClient.Verify(
                x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), StockMovementDirection.In, It.IsAny<string?>(), documentTypeId, It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task GetHistoryAsync_WhenNoDocumentTypesConfigured_ThrowsInsteadOfReturningEmpty()
    {
        // Arrange — misconfiguration must be loud. Returning an empty history here is exactly
        // the silent failure this change exists to prevent.
        var client = new FlexiManufactureHistoryClient(
            _mockMovementClient.Object,
            Options.Create(new DataSourceOptions { ManufactureDocumentTypeIds = Array.Empty<int>() }),
            _mockLogger.Object);

        // Act
        var act = () => client.GetHistoryAsync(new DateTime(2026, 1, 1), new DateTime(2026, 9, 21));

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ManufactureDocumentTypeIds*");
        _mockMovementClient.Verify(
            x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupMovementsForType(int documentTypeId, params StockItemMovementFlexiDto[] movements) =>
        _mockMovementClient
            .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), documentTypeId, It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(movements.ToList());

    private static StockItemMovementFlexiDto BuildMovement(
        string productCode, DateTime date, double amount, double pricePerUnit, double totalSum) =>
        new()
        {
            Date = date,
            Amount = amount,
            PricePerUnit = pricePerUnit,
            TotalSum = totalSum,
            Items = new List<StockItemProductFlexiDto> { new() { Code = productCode } }
        };
}
