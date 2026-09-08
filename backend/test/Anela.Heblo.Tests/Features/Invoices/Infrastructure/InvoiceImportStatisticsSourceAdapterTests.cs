using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

public sealed class InvoiceImportStatisticsSourceAdapterTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repository = new();

    private InvoiceImportStatisticsSourceAdapter CreateAdapter() => new(_repository.Object);

    [Fact]
    public async Task GetDailyCountsAsync_ForwardsArgumentsToRepository()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        _repository
            .Setup(r => r.GetDailyCountsAsync(startDate, endDate, ImportDateType.InvoiceDate, ct))
            .ReturnsAsync(new List<DailyInvoiceCount>());

        var adapter = CreateAdapter();

        // Act
        await adapter.GetDailyCountsAsync(startDate, endDate, ImportDateType.InvoiceDate, ct);

        // Assert
        _repository.Verify(
            r => r.GetDailyCountsAsync(startDate, endDate, ImportDateType.InvoiceDate, ct),
            Times.Once);
    }

    [Fact]
    public async Task GetDailyCountsAsync_ReturnsRepositoryResultUnchanged()
    {
        // Arrange
        var expected = new List<DailyInvoiceCount>
        {
            new() { Date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), Count = 2 },
            new() { Date = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), Count = 0 },
        };

        _repository
            .Setup(r => r.GetDailyCountsAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<ImportDateType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var adapter = CreateAdapter();

        // Act
        var result = await adapter.GetDailyCountsAsync(
            DateTime.UtcNow, DateTime.UtcNow, ImportDateType.LastSyncTime, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task GetDailyCountsAsync_PassesLastSyncTimeDateType_WhenRequested()
    {
        // Arrange
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        _repository
            .Setup(r => r.GetDailyCountsAsync(startDate, endDate, ImportDateType.LastSyncTime, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyInvoiceCount>())
            .Verifiable();

        var adapter = CreateAdapter();

        // Act
        await adapter.GetDailyCountsAsync(startDate, endDate, ImportDateType.LastSyncTime, CancellationToken.None);

        // Assert
        _repository.Verify();
    }
}
