using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality.DashboardTiles;

public class PriceComparisonStatusTileTests
{
    private readonly Mock<IDqtRunRepository> _repository = new();

    private PriceComparisonStatusTile CreateSut() =>
        new(_repository.Object, NullLogger<PriceComparisonStatusTile>.Instance);

    [Fact]
    public async Task reports_no_data_before_the_first_run()
    {
        // Arrange
        _repository.Setup(r => r.GetLatestByTestTypeAsync(DqtTestType.PriceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        // Act
        var data = await CreateSut().LoadDataAsync();

        // Assert
        StatusOf(data).Should().Be("no_data");
    }

    [Theory]
    [InlineData(0, "success")]
    [InlineData(7, "warning")]
    public async Task maps_the_mismatch_count_to_a_status(int mismatches, string expected)
    {
        // Arrange
        var run = DqtRun.Start(DqtTestType.PriceComparison, new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 10), DqtTriggerType.Scheduled, DateTime.UtcNow);
        run.Complete(totalChecked: 400, totalMismatches: mismatches, DateTime.UtcNow);
        _repository.Setup(r => r.GetLatestByTestTypeAsync(DqtTestType.PriceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        // Act
        var data = await CreateSut().LoadDataAsync();

        // Assert
        StatusOf(data).Should().Be(expected);
    }

    [Fact]
    public async Task reports_warning_while_a_run_is_still_in_progress()
    {
        // Arrange
        var run = DqtRun.Start(DqtTestType.PriceComparison, new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 10), DqtTriggerType.Scheduled, DateTime.UtcNow);
        _repository.Setup(r => r.GetLatestByTestTypeAsync(DqtTestType.PriceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        // Act
        var data = await CreateSut().LoadDataAsync();

        // Assert
        StatusOf(data).Should().Be("warning");
    }

    [Fact]
    public async Task surfaces_how_many_products_had_no_shoptet_price()
    {
        // Arrange: MissingInShoptet is excluded from the mismatch count on purpose, so
        // without this the tile can read "0 neshod / v\u0161e OK" while most of the catalogue
        // was never compared at all.
        var run = DqtRun.Start(DqtTestType.PriceComparison, new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 10), DqtTriggerType.Scheduled, DateTime.UtcNow);
        run.Complete(totalChecked: 400, totalMismatches: 0, DateTime.UtcNow);
        _repository.Setup(r => r.GetLatestByTestTypeAsync(DqtTestType.PriceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        _repository.Setup(r => r.CountDriftResultsByMismatchCodeAsync(
                run.Id, (int)PriceComparisonMismatch.MissingInShoptet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(37);

        // Act
        var data = await CreateSut().LoadDataAsync();

        // Assert
        MissingInShoptetOf(data).Should().Be(37);
    }

    private static int MissingInShoptetOf(object data)
    {
        var payload = data.GetType().GetProperty("data")!.GetValue(data)!;
        return (int)payload.GetType().GetProperty("missingInShoptet")!.GetValue(payload)!;
    }

    private static string StatusOf(object data) =>
        (string)data.GetType().GetProperty("status")!.GetValue(data)!;
}
