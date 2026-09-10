using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class GetIssuedInvoiceSyncStatsHandlerTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repositoryMock;
    private readonly GetIssuedInvoiceSyncStatsHandler _handler;

    public GetIssuedInvoiceSyncStatsHandlerTests()
    {
        _repositoryMock = new Mock<IIssuedInvoiceRepository>();

        _handler = new GetIssuedInvoiceSyncStatsHandler(
            _repositoryMock.Object,
            Mock.Of<ILogger<GetIssuedInvoiceSyncStatsHandler>>());
    }

    [Fact]
    public async Task Handle_BothDatesNull_DefaultsToTrailing30DayWindow()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = null,
            ToDate = null
        };
        var expectedFrom = DateTime.Now.Date.AddDays(-30);
        var expectedTo = DateTime.Now.Date;

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(
                It.Is<DateTime>(d => d.Date == expectedFrom),
                It.Is<DateTime>(d => d.Date == expectedTo),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedInvoiceSyncStats());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.GetSyncStatsAsync(
                It.Is<DateTime>(d => d.Date == expectedFrom),
                It.Is<DateTime>(d => d.Date == expectedTo),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitDates_PassesThemThroughUnchanged()
    {
        // Arrange
        var explicitFrom = new DateTime(2026, 1, 5);
        var explicitTo = new DateTime(2026, 1, 20);
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = explicitFrom,
            ToDate = explicitTo
        };

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(explicitFrom, explicitTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedInvoiceSyncStats());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.GetSyncStatsAsync(explicitFrom, explicitTo, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RepositoryThrows_ReturnsStructuredFailure()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest();

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("repository failure"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.Exception);
        response.Params.Should().NotBeNull();
        response.Params.Should().ContainKey("ErrorMessage")
            .WhoseValue.Should().Be("Chyba při načítání statistik synchronizace faktur");
        response.TotalInvoices.Should().Be(0);
        response.SyncedInvoices.Should().Be(0);
        response.UnsyncedInvoices.Should().Be(0);
        response.InvoicesWithErrors.Should().Be(0);
        response.CriticalErrors.Should().Be(0);
        response.LastSyncTime.Should().BeNull();
        response.SyncSuccessRate.Should().Be(0);
    }

    [Fact]
    public async Task Handle_RepositoryReturnsStats_MapsAllFieldsOntoResponse()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = new DateTime(2026, 2, 1),
            ToDate = new DateTime(2026, 2, 28)
        };
        var lastSync = new DateTime(2026, 2, 27, 14, 30, 0);
        var stats = new IssuedInvoiceSyncStats
        {
            TotalInvoices = 200,
            SyncedInvoices = 150,   // SyncSuccessRate = 150/200*100 = 75
            UnsyncedInvoices = 50,
            InvoicesWithErrors = 12,
            CriticalErrors = 3,
            LastSyncTime = lastSync
        };

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(request.FromDate.Value, request.ToDate.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.TotalInvoices.Should().Be(200);
        response.SyncedInvoices.Should().Be(150);
        response.UnsyncedInvoices.Should().Be(50);
        response.InvoicesWithErrors.Should().Be(12);
        response.CriticalErrors.Should().Be(3);
        response.LastSyncTime.Should().Be(lastSync);
        response.SyncSuccessRate.Should().Be(75m);
    }
}
