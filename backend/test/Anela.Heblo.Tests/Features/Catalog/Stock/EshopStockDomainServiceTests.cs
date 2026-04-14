using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog.Stock;

public class EshopStockDomainServiceTests
{
    private readonly Mock<IStockTakingRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<TimeProvider> _timeProvider = new();
    private readonly Mock<IEshopStockClient> _stockClient = new();

    private EshopStockDomainService CreateService() =>
        new(
            _repo.Object,
            _currentUser.Object,
            _timeProvider.Object,
            _stockClient.Object,
            NullLogger<EshopStockDomainService>.Instance);

    [Fact]
    public async Task StockUpAsync_CallsUpdateStock()
    {
        // Arrange
        var request = new StockUpRequest("AKL001", 5, "DOC-001");

        _stockClient.Setup(c => c.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.StockUpAsync(request);

        // Assert
        _stockClient.Verify(c => c.UpdateStockAsync("AKL001", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyStockUpExistsAsync_AlwaysReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.VerifyStockUpExistsAsync("BOX-000001-AKL001");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitStockTakingAsync_HardStockTaking_SetsRealStock()
    {
        // Arrange
        var supply = new EshopStockSupply { Code = "AKL001", Amount = 10, Claim = 2 };
        _stockClient.Setup(c => c.GetSupplyAsync("AKL001", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(supply);
        _stockClient.Setup(c => c.SetRealStockAsync("AKL001", 15, It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        _currentUser.Setup(u => u.GetCurrentUser())
                    .Returns(new CurrentUser("user-1", "Test User", "test@anela.cz", true));

        _timeProvider.Setup(t => t.GetUtcNow())
                     .Returns(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));

        _repo.Setup(r => r.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((StockTakingRecord r, CancellationToken _) => r);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        var order = new EshopStockTakingRequest
        {
            ProductCode = "AKL001",
            TargetAmount = 15,
            SoftStockTaking = false,
        };

        var service = CreateService();

        // Act
        var result = await service.SubmitStockTakingAsync(order);

        // Assert — supply(Amount=10) + supply(Claim=2) = 12
        result.AmountOld.Should().Be(12);
        result.AmountNew.Should().Be(15);
        result.Code.Should().Be("AKL001");

        _stockClient.Verify(c => c.SetRealStockAsync("AKL001", 15, default), Times.Once);
        _repo.Verify(r => r.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_SoftStockTaking_DoesNotCallShoptet()
    {
        // Arrange
        _currentUser.Setup(u => u.GetCurrentUser())
                    .Returns(new CurrentUser("user-1", "Test User", "test@anela.cz", true));

        _timeProvider.Setup(t => t.GetUtcNow())
                     .Returns(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));

        _repo.Setup(r => r.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((StockTakingRecord r, CancellationToken _) => r);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        var order = new EshopStockTakingRequest
        {
            ProductCode = "AKL001",
            TargetAmount = 20,
            SoftStockTaking = true,
        };

        var service = CreateService();

        // Act
        await service.SubmitStockTakingAsync(order);

        // Assert — soft stock taking must not touch Shoptet
        _stockClient.Verify(c => c.GetSupplyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _stockClient.Verify(c => c.SetRealStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenClientThrows_ReturnsRecordWithError()
    {
        // Arrange
        const string errorMessage = "Shoptet connection refused";
        _stockClient.Setup(c => c.GetSupplyAsync("AKL001", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException(errorMessage));

        _timeProvider.Setup(t => t.GetUtcNow())
                     .Returns(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));

        var order = new EshopStockTakingRequest
        {
            ProductCode = "AKL001",
            TargetAmount = 10,
            SoftStockTaking = false,
        };

        var service = CreateService();

        // Act
        var result = await service.SubmitStockTakingAsync(order);

        // Assert
        result.Error.Should().Contain(errorMessage);
        result.Code.Should().Be("AKL001");
    }

    [Fact]
    public async Task SubmitStockTakingAsync_SetsUserAndDate()
    {
        // Arrange
        var expectedDate = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        _stockClient.Setup(c => c.GetSupplyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new EshopStockSupply { Code = "AKL001", Amount = 5, Claim = 0 });
        _stockClient.Setup(c => c.SetRealStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        _currentUser.Setup(u => u.GetCurrentUser())
                    .Returns(new CurrentUser("user-42", "Jane Doe", "jane@anela.cz", true));

        _timeProvider.Setup(t => t.GetUtcNow())
                     .Returns(new DateTimeOffset(expectedDate, TimeSpan.Zero));

        _repo.Setup(r => r.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((StockTakingRecord r, CancellationToken _) => r);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        var order = new EshopStockTakingRequest
        {
            ProductCode = "AKL001",
            TargetAmount = 8,
            SoftStockTaking = false,
        };

        var service = CreateService();

        // Act
        var result = await service.SubmitStockTakingAsync(order);

        // Assert
        result.User.Should().Be("Jane Doe");
        result.Date.Should().Be(expectedDate);
    }
}
