using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class GetInvoiceImportStatisticsHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _mockRepository;
    private readonly GetInvoiceImportStatisticsHandler _handler;

    public GetInvoiceImportStatisticsHandlerTests()
    {
        _mockRepository = new Mock<IAnalyticsRepository>();
        // Create a simple configuration for testing
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "InvoiceImport:MinimumDailyThreshold", "10" },
            { "InvoiceImport:DefaultDaysBack", "14" }
        });
        var configuration = configurationBuilder.Build();
        _handler = new GetInvoiceImportStatisticsHandler(_mockRepository.Object, configuration);
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
        var baseDate = DateTime.UtcNow.Date;
        var expectedData = new List<DailyInvoiceCount>
        {
            new() { Date = DateTime.SpecifyKind(baseDate.AddDays(-1), DateTimeKind.Utc), Count = 15, IsBelowThreshold = false },
            new() { Date = DateTime.SpecifyKind(baseDate, DateTimeKind.Utc), Count = 5, IsBelowThreshold = false } // Will be set by handler
        };

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
        // Arrange - Create handler with empty configuration
        var emptyConfigurationBuilder = new ConfigurationBuilder();
        emptyConfigurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>());
        var emptyConfiguration = emptyConfigurationBuilder.Build();
        var handlerWithEmptyConfig = new GetInvoiceImportStatisticsHandler(_mockRepository.Object, emptyConfiguration);

        var request = new GetInvoiceImportStatisticsRequest();
        var expectedData = new List<DailyInvoiceCount>();

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ImportDateType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await handlerWithEmptyConfig.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(10, result.MinimumThreshold);
    }

    [Fact]
    public async Task Handle_ShouldUseConfigurableDefaultDaysBack()
    {
        // Arrange - Create handler with custom default days back
        var customConfigurationBuilder = new ConfigurationBuilder();
        customConfigurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "InvoiceImport:MinimumDailyThreshold", "10" },
            { "InvoiceImport:DefaultDaysBack", "30" } // Custom default
        });
        var customConfiguration = customConfigurationBuilder.Build();
        var handlerWithCustomConfig = new GetInvoiceImportStatisticsHandler(_mockRepository.Object, customConfiguration);

        var request = new GetInvoiceImportStatisticsRequest { DaysBack = 0 }; // Use default
        var expectedData = new List<DailyInvoiceCount>();

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ImportDateType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await handlerWithCustomConfig.Handle(request, CancellationToken.None);

        // Assert - Verify that 30 days range was used by checking repository call
        _mockRepository.Verify(r => r.GetInvoiceImportStatisticsAsync(
            It.Is<DateTime>(d => d <= DateTime.UtcNow.Date.AddDays(-29)), // Should be around 30 days ago
            It.Is<DateTime>(d => d >= DateTime.UtcNow.Date.AddDays(-1)), // Should be around today
            It.IsAny<ImportDateType>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ImportDateType.InvoiceDate)]
    [InlineData(ImportDateType.LastSyncTime)]
    public async Task Handle_ShouldPassCorrectDateTypeToRepository(ImportDateType dateType)
    {
        // Arrange
        var request = new GetInvoiceImportStatisticsRequest { DateType = dateType };

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