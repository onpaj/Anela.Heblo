using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

public class FlatManufactureCostProviderTests
{
    private const int DefaultDifficultyValue = 1;

    [Fact]
    public async Task GetHistoricalDifficulty_WithExistingSetting_ReturnsDifficultyValue()
    {
        // Arrange
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        var productCode = "TEST001";
        var referenceDate = new DateTime(2025, 6, 15);
        var expectedDifficulty = 5;

        difficultyRepoMock.Setup(r => r.FindAsync(productCode, referenceDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting
            {
                ProductCode = productCode,
                DifficultyValue = expectedDifficulty,
                ValidFrom = new DateTime(2025, 1, 1),
                ValidTo = null
            });

        var provider = CreateProvider(difficultyRepository: difficultyRepoMock.Object);

        // Act
        var result = await provider.GetHistoricalDifficultyAsync(productCode, referenceDate);

        // Assert
        Assert.Equal(expectedDifficulty, result);
    }

    [Fact]
    public async Task GetHistoricalDifficulty_WithNoSetting_ReturnsDefaultValue()
    {
        // Arrange
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        var productCode = "TEST002";
        var referenceDate = new DateTime(2025, 6, 15);

        difficultyRepoMock.Setup(r => r.FindAsync(productCode, referenceDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureDifficultySetting?)null);

        var provider = CreateProvider(difficultyRepository: difficultyRepoMock.Object);

        // Act
        var result = await provider.GetHistoricalDifficultyAsync(productCode, referenceDate);

        // Assert
        Assert.Equal(DefaultDifficultyValue, result);
    }

    private FlatManufactureCostProvider CreateProvider(
        IFlatManufactureCostCache? cache = null,
        ICatalogRepository? catalogRepository = null,
        ILedgerService? ledgerService = null,
        IManufactureHistoryClient? manufactureHistoryClient = null,
        IManufactureDifficultyRepository? difficultyRepository = null,
        ILogger<FlatManufactureCostProvider>? logger = null,
        CostCacheOptions? options = null)
    {
        return new FlatManufactureCostProvider(
            cache ?? new FlatManufactureCostCache(new MemoryCache(new MemoryCacheOptions())),
            catalogRepository ?? Mock.Of<ICatalogRepository>(),
            ledgerService ?? Mock.Of<ILedgerService>(),
            manufactureHistoryClient ?? Mock.Of<IManufactureHistoryClient>(),
            difficultyRepository ?? Mock.Of<IManufactureDifficultyRepository>(),
            logger ?? Mock.Of<ILogger<FlatManufactureCostProvider>>(),
            Options.Create(options ?? new CostCacheOptions())
        );
    }
}
