# FlexiStockTakingDomainService Unit Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Write a complete unit test suite for `FlexiStockTakingDomainService.SubmitStockTakingAsync` covering all four decision branches and the exception path to bring line coverage above 60%.

**Architecture:** Single test file added to the existing `Anela.Heblo.Adapters.Flexi.Tests` project. All external collaborators (`IStockTakingRepository`, `IStockTakingClient`, `IStockTakingItemsClient`, `ICurrentUserService`) are mocked with `MockBehavior.Loose`. No new packages or project changes.

**Tech Stack:** xUnit, Moq 4.20, FluentAssertions 6.12, .NET 8

---

## File Map

| Action | Path |
|--------|------|
| Create | `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Stock/FlexiStockTakingDomainServiceTests.cs` |

---

### task: write-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Stock/FlexiStockTakingDomainServiceTests.cs`

- [ ] **Step 1: Create the test file**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Stock/FlexiStockTakingDomainServiceTests.cs` with the following content:

```csharp
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

    // ─────────────────────────────────────────────────────────────────────────
    // FR-1: SoftStockTaking — no ERP calls, AmountNew == AmountOld == sum(items)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitStockTakingAsync_WhenAllItemsAreSoftStockTaking_NoErpCallsMade()
    {
        // Arrange — SoftStockTaking is a computed property: true when ALL items have SoftStockTaking=true
        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-001",
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 5m, SoftStockTaking = true },
                new() { Amount = 3m, SoftStockTaking = true },
            }
        };

        // Act
        await _sut.SubmitStockTakingAsync(order);

        // Assert — no ERP client should be called at all
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
        // Arrange
        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-001",
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 5m, SoftStockTaking = true },
                new() { Amount = 3m, SoftStockTaking = true },
            }
        };

        // Act
        var result = await _sut.SubmitStockTakingAsync(order);

        // Assert
        result.AmountNew.Should().Be(8.0);
        result.AmountOld.Should().Be(8.0);
        result.Code.Should().Be("PROD-001");
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenAllItemsAreSoftStockTaking_RecordSavedToRepository()
    {
        // Arrange
        var order = new ErpStockTakingRequest
        {
            ProductCode = "PROD-002",
            StockTakingItems = new List<ErpStockTakingLot>
            {
                new() { Amount = 10m, SoftStockTaking = true },
            }
        };

        // Act
        await _sut.SubmitStockTakingAsync(order);

        // Assert
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-2: Real ERP path (SoftStockTaking=false, DryRun=false) — SubmitAsync called
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitStockTakingAsync_WhenRealErpAndNotDryRun_SubmitAsyncCalledOnce()
    {
        // Arrange
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

        // Act
        await _sut.SubmitStockTakingAsync(order);

        // Assert
        _mockStockTakingClient.Verify(
            x => x.SubmitAsync(headerId, 60, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenRealErpAndNotDryRun_AmountsComputedFromErpItems()
    {
        // Arrange
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

        // Act
        var result = await _sut.SubmitStockTakingAsync(order);

        // Assert — AmountNew from itemsAfter.AmountFound, AmountOld from itemsBefore.AmountErp
        result.AmountNew.Should().Be(7.5);
        result.AmountOld.Should().Be(5.0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-3: DryRun=true — document created but SubmitAsync NOT called
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitStockTakingAsync_WhenDryRun_SubmitAsyncNotCalled()
    {
        // Arrange
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

        // Act
        await _sut.SubmitStockTakingAsync(order);

        // Assert — document created but never submitted
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
        // Arrange
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

        // Act
        await _sut.SubmitStockTakingAsync(order);

        // Assert — repo still gets the record
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-4: RemoveMissingLots=true — GetStockTakingsAsync + AddMissingLotsAsync called
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitStockTakingAsync_WhenRemoveMissingLotsTrue_GetStockTakingsAndAddMissingLotsCalled()
    {
        // Arrange — set up the extra GetStockTakingsAsync call for missing lots lookup
        var headerId = 77;
        var productIds = new List<int> { 101, 102 };

        _mockStockTakingClient
            .Setup(x => x.CreateHeaderAsync(It.IsAny<StockTakingHeaderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTakingHeader { Id = headerId });

        _mockStockTakingItemsClient
            .Setup(x => x.AddStockTakingsAsync(headerId, 5, It.IsAny<IEnumerable<AddStockTakingItemRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // First call after AddStockTakingsAsync: returns items with ProductIds for missing lots
        // Subsequent calls: return itemsBefore and itemsAfter
        var callCount = 0;
        _mockStockTakingItemsClient
            .Setup(x => x.GetStockTakingsAsync(headerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call: for missing lots — returns items with ProductId
                    return new List<StockTakingItemResult>
                    {
                        new() { ProductId = 101, AmountErp = 1.0, AmountFound = 1.0 },
                        new() { ProductId = 102, AmountErp = 2.0, AmountFound = 2.0 },
                    };
                }
                // Second call: itemsBefore
                return new List<StockTakingItemResult>
                {
                    new() { ProductId = 101, AmountErp = 3.0, AmountFound = 4.0 },
                };
            });

        _mockStockTakingClient
            .Setup(x => x.GetHeaderAsync(headerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTakingHeader { Id = headerId });

        // itemsAfter
        _mockStockTakingItemsClient
            .SetupSequence(x => x.GetStockTakingsAsync(headerId, It.IsAny<CancellationToken>()));

        // Re-setup cleanly with a sequence for all three GetStockTakingsAsync calls
        _mockStockTakingItemsClient.Reset();
        _mockStockTakingItemsClient
            .Setup(x => x.AddStockTakingsAsync(headerId, 5, It.IsAny<IEnumerable<AddStockTakingItemRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStockTakingItemsClient
            .SetupSequence(x => x.GetStockTakingsAsync(headerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingItemResult>
            {
                // Call 1: for GetStockTakingsAsync after AddStockTakingsAsync (missing lots lookup)
                new() { ProductId = 101, AmountErp = 1.0, AmountFound = 1.0 },
                new() { ProductId = 102, AmountErp = 2.0, AmountFound = 2.0 },
            })
            .ReturnsAsync(new List<StockTakingItemResult>
            {
                // Call 2: itemsBefore
                new() { ProductId = 101, AmountErp = 5.0, AmountFound = 6.0 },
            })
            .ReturnsAsync(new List<StockTakingItemResult>
            {
                // Call 3: itemsAfter
                new() { ProductId = 101, AmountErp = 5.0, AmountFound = 6.0 },
            });

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

        // Act
        await _sut.SubmitStockTakingAsync(order);

        // Assert
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
        // Arrange
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

        // Act
        await _sut.SubmitStockTakingAsync(order);

        // Assert
        _mockStockTakingClient.Verify(
            x => x.AddMissingLotsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-5: Exception path — Error field set, repo not called
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitStockTakingAsync_WhenErpThrows_ReturnsRecordWithErrorSet()
    {
        // Arrange — single item with Amount=10 so AmountOld is predictable from catch block
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

        // Act
        var result = await _sut.SubmitStockTakingAsync(order);

        // Assert
        result.Error.Should().Be("ERP connection failed");
        result.Code.Should().Be("PROD-ERR");
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenErpThrows_AmountOldEqualsItemAmountSum()
    {
        // Arrange — catch block sets AmountOld = order.StockTakingItems.Sum(s => s.Amount)
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

        // Act
        var result = await _sut.SubmitStockTakingAsync(order);

        // Assert — AmountOld in catch = (double)Sum(Amount) = 10.0
        result.AmountOld.Should().Be(10.0);
    }

    [Fact]
    public async Task SubmitStockTakingAsync_WhenErpThrows_RepositoryNotCalled()
    {
        // Arrange
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

        // Act
        await _sut.SubmitStockTakingAsync(order);

        // Assert — silent catch must NOT persist anything
        _mockRepository.Verify(
            x => x.AddAsync(It.IsAny<StockTakingRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets up the standard ERP path (no RemoveMissingLots) with controlled amounts.
    /// GetStockTakingsAsync is called twice: once for itemsBefore, once for itemsAfter.
    /// </summary>
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
```

- [ ] **Step 2: Run the tests to verify they compile and pass**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "FullyQualifiedName~FlexiStockTakingDomainServiceTests" \
  -v normal 2>&1
```

Expected: all tests pass (green). If any fail, review the failure message — the most likely issues are:
- `SetupSequence` not returning enough values: add another `.ReturnsAsync(...)` call in `SetupFullErpPath` for `GetStockTakingsAsync`.
- Missing `CancellationToken` parameter on a mock setup: all SDK interface methods take an optional `CancellationToken` — ensure `It.IsAny<CancellationToken>()` is used in every `Setup`.

- [ ] **Step 3: Run the full test project to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/ -v minimal 2>&1
```

Expected: all previously passing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Stock/FlexiStockTakingDomainServiceTests.cs
git commit -m "test: add unit tests for FlexiStockTakingDomainService.SubmitStockTakingAsync

Covers all five behavioral contracts (FR-1 through FR-5):
- SoftStockTaking path (no ERP calls, AmountNew == AmountOld)
- Real ERP path (SubmitAsync called, amounts from ERP items)
- DryRun=true (SubmitAsync skipped, repo still saved)
- RemoveMissingLots=true (GetStockTakings + AddMissingLots called)
- Exception path (Error field set, repo not called)"
```
