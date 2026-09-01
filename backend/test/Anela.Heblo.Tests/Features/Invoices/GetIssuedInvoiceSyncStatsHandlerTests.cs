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
}
