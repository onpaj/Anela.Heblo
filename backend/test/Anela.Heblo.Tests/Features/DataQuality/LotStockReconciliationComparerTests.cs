using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class LotStockReconciliationComparerTests
{
    private readonly Mock<IMaterialLotStockQuery> _materialLotStockMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private LotStockReconciliationComparer CreateSut() => new(_materialLotStockMock.Object);

    private void SetupMaterials(params MaterialLotStockSnapshot[] items) =>
        _materialLotStockMock.Setup(q => q.GetMaterialsWithExpirationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToList());

    private static MaterialLotStockSnapshot Material(
        string code,
        decimal erp,
        params decimal[] lotAmounts) =>
        new()
        {
            ProductCode = code,
            ErpStock = erp,
            LotAmounts = lotAmounts.ToList()
        };

    [Fact]
    public void TestType_IsLotSumVsErpStock()
    {
        CreateSut().TestType.Should().Be(DqtTestType.LotSumVsErpStock);
    }

    [Fact]
    public async Task CompareAsync_ReturnsNoMismatch_WhenLotSumEqualsErp_AndCountsItem()
    {
        // Arrange
        SetupMaterials(Material("MAT001", erp: 10m, lotAmounts: new[] { 4m, 6m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(1);
    }

    [Fact]
    public async Task CompareAsync_ReturnsNoMismatch_WhenDifferenceWithinTolerance()
    {
        // Arrange — |10.005 - 10| = 0.005 <= 0.01
        SetupMaterials(Material("MAT001", erp: 10m, lotAmounts: new[] { 10.005m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(1);
    }

    [Fact]
    public async Task CompareAsync_ReturnsSumMismatch_WhenLotSumDiffersBeyondTolerance()
    {
        // Arrange
        SetupMaterials(Material("MAT001", erp: 10m, lotAmounts: new[] { 5m, 3m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        var mismatch = result.Mismatches.Single();
        mismatch.EntityKey.Should().Be("MAT001");
        ((LotStockReconciliationMismatch)mismatch.MismatchCode)
            .Should().Be(LotStockReconciliationMismatch.SumMismatch);
        mismatch.HebloValue.Should().Be(10m.ToString("F2"));
        mismatch.ShoptetValue.Should().Be(8m.ToString("F2"));
        mismatch.Details.Should()
            .Contain($"ERP: {10m:F2}")
            .And.Contain($"Šarže: {8m:F2}")
            .And.Contain($"Rozdíl: {-2m:F2}");
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingLots_WhenErpPositiveButNoLots()
    {
        // Arrange
        SetupMaterials(Material("MAT001", erp: 10m));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        ((LotStockReconciliationMismatch)result.Mismatches.Single().MismatchCode)
            .Should().Be(LotStockReconciliationMismatch.MissingLots);
    }

    [Fact]
    public async Task CompareAsync_ReturnsOrphanLots_WhenLotsPresentButErpZero()
    {
        // Arrange
        SetupMaterials(Material("MAT001", erp: 0m, lotAmounts: new[] { 5m }));

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        ((LotStockReconciliationMismatch)result.Mismatches.Single().MismatchCode)
            .Should().Be(LotStockReconciliationMismatch.OrphanLots);
    }

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenNoMaterialsInScope()
    {
        // Arrange
        SetupMaterials();

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(0);
    }

    [Fact]
    public async Task CompareAsync_CountsAllInScopeItems_AndReportsOnlyMismatches()
    {
        // Arrange
        SetupMaterials(
            Material("OK001", erp: 10m, lotAmounts: new[] { 10m }),          // matches
            Material("BAD001", erp: 10m, lotAmounts: new[] { 3m }),          // SumMismatch
            Material("MISS001", erp: 5m),                                    // MissingLots
            Material("ORPH001", erp: 0m, lotAmounts: new[] { 2m })           // OrphanLots
        );

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.TotalChecked.Should().Be(4);
        result.Mismatches.Select(m => m.EntityKey)
            .Should().BeEquivalentTo(new[] { "BAD001", "MISS001", "ORPH001" });
    }

    [Fact]
    public async Task CompareAsync_IgnoresDateRange()
    {
        // Arrange
        SetupMaterials(Material("MAT001", erp: 10m, lotAmounts: new[] { 10m }));

        // Act — arbitrary range must not affect the snapshot result
        var result = await CreateSut().CompareAsync(
            new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 2), CancellationToken.None);

        // Assert
        result.TotalChecked.Should().Be(1);
        result.Mismatches.Should().BeEmpty();
    }
}
