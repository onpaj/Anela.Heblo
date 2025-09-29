using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class GetInvoiceImportStatisticsHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _mockRepository;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly GetInvoiceImportStatisticsHandler _handler;

    public GetInvoiceImportStatisticsHandlerTests()
    {
        _mockRepository = new Mock<IAnalyticsRepository>();
        _mockConfiguration = new Mock<IConfiguration>();
        _handler = new GetInvoiceImportStatisticsHandler(_mockRepository.Object, _mockConfiguration.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnStatisticsWithMinimumThreshold()
    {
        // Arrange
        var request = new GetInvoiceImportStatisticsRequest
        {
            DateType = ImportDateType.InvoiceDate,
            DaysBack = 14
        };

        var expectedThreshold = 10;
        var expectedData = new List<DailyInvoiceCount>
        {
            new() { Date = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-1), DateTimeKind.Utc), Count = 15, IsBelowThreshold = false },
            new() { Date = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc), Count = 5, IsBelowThreshold = false } // Will be set by handler
        };

        _mockConfiguration.Setup(c => c["InvoiceImport:MinimumDailyThreshold"])
            .Returns(expectedThreshold.ToString());

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                ImportDateType.InvoiceDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(expectedThreshold, result.MinimumThreshold);
        Assert.Equal(2, result.Data.Count);
        
        // Verify threshold logic is applied
        Assert.False(result.Data[0].IsBelowThreshold); // 15 >= 10
        Assert.True(result.Data[1].IsBelowThreshold);  // 5 < 10
    }

    [Fact]
    public async Task Handle_ShouldUseDefaultThresholdWhenNotConfigured()
    {
        // Arrange
        var request = new GetInvoiceImportStatisticsRequest();
        var expectedData = new List<DailyInvoiceCount>();

        _mockConfiguration.Setup(c => c["InvoiceImport:MinimumDailyThreshold"])
            .Returns((string?)null); // Return null to test default value

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ImportDateType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(10, result.MinimumThreshold);
    }

    [Theory]
    [InlineData(ImportDateType.InvoiceDate)]
    [InlineData(ImportDateType.LastSyncTime)]
    public async Task Handle_ShouldPassCorrectDateTypeToRepository(ImportDateType dateType)
    {
        // Arrange
        var request = new GetInvoiceImportStatisticsRequest { DateType = dateType };
        
        _mockConfiguration.Setup(c => c["InvoiceImport:MinimumDailyThreshold"])
            .Returns("10");

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                dateType,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyInvoiceCount>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _mockRepository.Verify(r => r.GetInvoiceImportStatisticsAsync(
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            dateType,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}