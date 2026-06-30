using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockTaking;
using Rem.FlexiBeeSDK.Model.Products.StockTaking;

namespace Anela.Heblo.Adapters.Flexi.Tests.Stock;

public class FlexiStockTakingDomainServiceTests
{
    private readonly Mock<IStockTakingRepository> _mockRepository;
    private readonly Mock<IStockTakingClient> _mockStockTakingClient;
    private readonly Mock<IStockTakingItemsClient> _mockStockTakingItemsClient;
    private readonly Mock<ICurrentUserService> _mockCurrentUser;
    private readonly FlexiStockTakingDomainService _sut;

    public FlexiStockTakingDomainServiceTests()
    {
        _mockRepository = new Mock<IStockTakingRepository>(MockBehavior.Loose);
        _mockStockTakingClient = new Mock<IStockTakingClient>(MockBehavior.Loose);
        _mockStockTakingItemsClient = new Mock<IStockTakingItemsClient>(MockBehavior.Loose);
        _mockCurrentUser = new Mock<ICurrentUserService>(MockBehavior.Loose);

        _mockCurrentUser
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test User", "test@example.com", true));

        _mockRepository
            .Setup(x => x.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTakingRecord r, CancellationToken _) => r);

        _mockRepository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _sut = new FlexiStockTakingDomainService(
            _mockRepository.Object,
            _mockStockTakingClient.Object,
            _mockStockTakingItemsClient.Object,
            _mockCurrentUser.Object,
            TimeProvider.System);
    }

    // FR-1: SoftStockTaking — no ERP calls, AmountNew == AmountOld == sum(items)

    [Fact]
    public async Task SubmitStockTakingAsync_WhenAllItemsAreSoftStockTaking_NoErpCallsMade()
    {
        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-001",
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 5m, SoftStockTaking = true },
                new() { Amount = 3m, SoftStockTaking = true },
            }
        };

        await _sut.SubmitStockTakingAsync(order);

        _mockStockTakingClient.Verify(
            x => x.CreateHeaderAsync(It.IsAny<StockTakingHeaderRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockStockTakingItemsClient.Verify(
            x => x.AddStockTakingsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IEnumerable<AddStockTakingItemRequest>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenAllItemsAreSoftStockTaking_AmountNewEqualsAmountOldEqualsSumOfItems()
    {
        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-001",
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 5m, SoftStockTaking = true },
                new() { Amount = 3m, SoftStockTaking = true },
            }
        };

        var result = await _sut.SubmitStockTakingAsync(order);

        result.AmountNew.Should().Be(8.0);
        result.AmountOld.Should().Be(8.0);
        result.Code.Should().Be("PROD-001");
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenAllItemsAreSoftStockTaking_RecordSavedToRepository()
    {
        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-002",
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 10m, SoftStockTaking = true },
            }
        };

        await _sut.SubmitStockTakingAsync(order);

        _mockRepository.Verify(x => x.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // FR-2: Real ERP path — SubmitAsync called, amounts from ERP items

    [Fact]
    public async Task SubmitStockTakingAsync_WhenRealErpAndNotDryRun_SubmitAsyncCalledOnce()
    {
        var headerId = 42;
        SetupFullErpPath(headerId, amountFound: 7.5, amountErp: 5.0);

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-003",
            DryRun = false,
            RemoveMissingLots = false,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 4m, SoftStockTaking = false },
            }
        };

        await _sut.SubmitStockTakingAsync(order);

        _mockStockTakingClient.Verify(
            x => x.SubmitAsync(headerId, 60, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenRealErpAndNotDryRun_AmountsComputedFromErpItems()
    {
        var headerId = 42;
        SetupFullErpPath(headerId, amountFound: 7.5, amountErp: 5.0);

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-003",
            DryRun = false,
            RemoveMissingLots = false,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 4m, SoftStockTaking = false },
            }
        };

        var result = await _sut.SubmitStockTakingAsync(order);

        result.AmountNew.Should().Be(7.5);
        result.AmountOld.Should().Be(5.0);
    }

    // FR-3: DryRun=true — SubmitAsync NOT called, repo still saved

    [Fact]
    public async Task SubmitStockTakingAsync_WhenDryRun_SubmitAsyncNotCalled()
    {
        var headerId = 99;
        SetupFullErpPath(headerId, amountFound: 3.0, amountErp: 2.0);

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-004",
            DryRun = true,
            RemoveMissingLots = false,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 2m, SoftStockTaking = false },
            }
        };

        await _sut.SubmitStockTakingAsync(order);

        _mockStockTakingClient.Verify(
            x => x.CreateHeaderAsync(It.IsAny<StockTakingHeaderRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockStockTakingClient.Verify(
            x => x.SubmitAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenDryRun_RecordStillSavedToRepository()
    {
        var headerId = 99;
        SetupFullErpPath(headerId, amountFound: 3.0, amountErp: 2.0);

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-004",
            DryRun = true,
            RemoveMissingLots = false,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 2m, SoftStockTaking = false },
            }
        };

        await _sut.SubmitStockTakingAsync(order);

        _mockRepository.Verify(x => x.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // FR-4: RemoveMissingLots=true/false

    [Fact]
    public async Task SubmitStockTakingAsync_WhenRemoveMissingLotsTrue_GetStockTakingsAndAddMissingLotsCalled()
    {
        var headerId = 77;

        _mockStockTakingClient
            .Setup(x => x.CreateHeaderAsync(It.IsAny<StockTakingHeaderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTakingHeader { Id = headerId });

        _mockStockTakingItemsClient
            .Setup(x => x.AddStockTakingsAsync(headerId, 5, It.IsAny<IEnumerable<AddStockTakingItemRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStockTakingItemsClient
            .SetupSequence(x => x.GetStockTakingsAsync(headerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingItemResult>
            {
                new() { ProductId = 101, AmountErp = 1.0, AmountFound = 1.0 },
                new() { ProductId = 102, AmountErp = 2.0, AmountFound = 2.0 },
            })
            .ReturnsAsync(new List<StockTakingItemResult>
            {
                new() { ProductId = 101, AmountErp = 5.0, AmountFound = 6.0 },
            })
            .ReturnsAsync(new List<StockTakingItemResult>
            {
                new() { ProductId = 101, AmountErp = 5.0, AmountFound = 6.0 },
            });

        _mockStockTakingClient
            .Setup(x => x.SubmitAsync(headerId, 60, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStockTakingClient
            .Setup(x => x.GetHeaderAsync(headerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTakingHeader { Id = headerId });

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-005",
            DryRun = false,
            RemoveMissingLots = true,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 2m, SoftStockTaking = false },
            }
        };

        await _sut.SubmitStockTakingAsync(order);

        _mockStockTakingItemsClient.Verify(
            x => x.GetStockTakingsAsync(headerId, It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
        _mockStockTakingClient.Verify(
            x => x.AddMissingLotsAsync(headerId, It.Is<IEnumerable<int>>(ids => ids.Contains(101) && ids.Contains(102)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenRemoveMissingLotsFalse_AddMissingLotsAsyncNotCalled()
    {
        var headerId = 55;
        SetupFullErpPath(headerId, amountFound: 1.0, amountErp: 1.0);

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-006",
            DryRun = false,
            RemoveMissingLots = false,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 1m, SoftStockTaking = false },
            }
        };

        await _sut.SubmitStockTakingAsync(order);

        _mockStockTakingClient.Verify(
            x => x.AddMissingLotsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // FR-5: Exception path — Error field set, repo not called

    [Fact]
    public async Task SubmitStockTakingAsync_WhenErpThrows_ReturnsRecordWithErrorSet()
    {
        _mockStockTakingClient
            .Setup(x => x.CreateHeaderAsync(It.IsAny<StockTakingHeaderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ERP connection failed"));

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-ERR",
            DryRun = false,
            RemoveMissingLots = false,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 10m, SoftStockTaking = false },
            }
        };

        var result = await _sut.SubmitStockTakingAsync(order);

        result.Error.Should().Be("ERP connection failed");
        result.Code.Should().Be("PROD-ERR");
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenErpThrows_AmountNewEqualsItemAmountSum()
    {
        _mockStockTakingClient
            .Setup(x => x.CreateHeaderAsync(It.IsAny<StockTakingHeaderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ERP error"));

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-ERR2",
            DryRun = false,
            RemoveMissingLots = false,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 10m, SoftStockTaking = false },
            }
        };

        var result = await _sut.SubmitStockTakingAsync(order);

        // In the error path the production code sets AmountNew to the submitted sum; AmountOld stays at default 0
        result.AmountNew.Should().Be(10.0);
        result.AmountOld.Should().Be(0.0);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenErpThrows_RepositoryNotCalled()
    {
        _mockStockTakingClient
            .Setup(x => x.CreateHeaderAsync(It.IsAny<StockTakingHeaderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ERP error"));

        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-ERR3",
            DryRun = false,
            RemoveMissingLots = false,
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 10m, SoftStockTaking = false },
            }
        };

        await _sut.SubmitStockTakingAsync(order);

        _mockRepository.Verify(
            x => x.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Helper

    private void SetupFullErpPath(int headerId, double amountFound, double amountErp)
    {
        _mockStockTakingClient
            .Setup(x => x.CreateHeaderAsync(It.IsAny<StockTakingHeaderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTakingHeader { Id = headerId });

        _mockStockTakingItemsClient
            .Setup(x => x.AddStockTakingsAsync(headerId, 5, It.IsAny<IEnumerable<AddStockTakingItemRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStockTakingItemsClient
            .SetupSequence(x => x.GetStockTakingsAsync(headerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingItemResult>
            {
                new() { AmountErp = amountErp, AmountFound = amountFound },
            })
            .ReturnsAsync(new List<StockTakingItemResult>
            {
                new() { AmountErp = amountErp, AmountFound = amountFound },
            });

        _mockStockTakingClient
            .Setup(x => x.SubmitAsync(headerId, 60, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStockTakingClient
            .Setup(x => x.GetHeaderAsync(headerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTakingHeader { Id = headerId });
    }
}
