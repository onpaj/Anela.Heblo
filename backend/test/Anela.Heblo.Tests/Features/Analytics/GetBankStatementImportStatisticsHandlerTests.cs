using Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class GetBankStatementImportStatisticsHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _mockRepository;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetBankStatementImportStatisticsHandler _handler;
    private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 14, 10, 0, 0, DateTimeKind.Utc);

    public GetBankStatementImportStatisticsHandlerTests()
    {
        _mockRepository = new Mock<IAnalyticsRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime);
        _handler = new GetBankStatementImportStatisticsHandler(_mockRepository.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_WithNoDatesProvided_UsesInjectedTimeProviderForDefaultRange()
    {
        // Arrange
        var request = new GetBankStatementImportStatisticsRequest();
        var expectedEndDate = DateTime.SpecifyKind(_fixedDateTime.Date, DateTimeKind.Utc);
        var expectedStartDate = DateTime.SpecifyKind(_fixedDateTime.Date.AddDays(-30), DateTimeKind.Utc);

        _mockRepository
            .Setup(r => r.GetBankStatementImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<BankStatementDateType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyBankStatementStatistics>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        _timeProviderMock.Verify(x => x.GetUtcNow(), Times.Once);
        _mockRepository.Verify(r => r.GetBankStatementImportStatisticsAsync(
            expectedStartDate,
            expectedEndDate,
            request.DateType,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithExplicitDatesProvided_DoesNotConsultTimeProvider()
    {
        // Arrange
        var suppliedStartDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var suppliedEndDate = new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var request = new GetBankStatementImportStatisticsRequest
        {
            StartDate = suppliedStartDate,
            EndDate = suppliedEndDate,
            DateType = BankStatementDateType.ImportDate
        };

        _mockRepository
            .Setup(r => r.GetBankStatementImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<BankStatementDateType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyBankStatementStatistics>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        _timeProviderMock.Verify(x => x.GetUtcNow(), Times.Never);
        _mockRepository.Verify(r => r.GetBankStatementImportStatisticsAsync(
            suppliedStartDate,
            suppliedEndDate,
            BankStatementDateType.ImportDate,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
