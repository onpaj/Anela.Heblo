using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityStockTakingQueryAdapterTests
{
    private readonly Mock<IStockTakingRepository> _repository = new();

    private DataQualityStockTakingQueryAdapter CreateAdapter() => new(_repository.Object);

    [Fact]
    public async Task GetByDateRangeAsync_ProjectsAllRequiredFields()
    {
        // Arrange
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);
        _repository.Setup(r => r.GetByDateRangeAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>
            {
                new()
                {
                    Code = "P-001",
                    AmountNew = 12.5,
                    AmountOld = 10.0,
                    Error = "Shoptet API timeout",
                    Date = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc)
                },
            });

        // Act
        var result = await CreateAdapter().GetByDateRangeAsync(from, to, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        var snapshot = result[0];
        snapshot.Code.Should().Be("P-001");
        snapshot.AmountNew.Should().Be(12.5);
        snapshot.Error.Should().Be("Shoptet API timeout");
    }

    [Fact]
    public async Task GetByDateRangeAsync_ProjectsNullError()
    {
        // Arrange
        _repository.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>
            {
                new()
                {
                    Code = "P-002",
                    AmountNew = 5.0,
                    AmountOld = 5.0,
                    Error = null,
                    Date = DateTime.UtcNow
                },
            });

        // Act
        var result = await CreateAdapter().GetByDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.Error.Should().BeNull();
    }

    [Fact]
    public async Task GetByDateRangeAsync_PropagatesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _repository.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>());

        // Act
        await CreateAdapter().GetByDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            cts.Token);

        // Assert
        _repository.Verify(r => r.GetByDateRangeAsync(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task GetByDateRangeAsync_WhenRepositoryEmpty_ReturnsEmptyList()
    {
        // Arrange
        _repository.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>());

        // Act
        var result = await CreateAdapter().GetByDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
