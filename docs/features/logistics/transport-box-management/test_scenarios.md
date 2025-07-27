# Transport Box Management - Test Scenarios

## Unit Test Scenarios

### Domain Model Tests

#### TransportBox Aggregate Tests

```csharp
[TestFixture]
public class TransportBoxTests
{
    private DateTime _testDate;
    private string _testUser;

    [SetUp]
    public void SetUp()
    {
        _testDate = DateTime.UtcNow;
        _testUser = "test-user";
    }

    [Test]
    public void NewBox_ShouldHaveCorrectInitialState()
    {
        // Arrange & Act
        var box = new TransportBox();

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.New));
        Assert.That(box.Code, Is.Null);
        Assert.That(box.Items, Is.Empty);
        Assert.That(box.StateLog, Is.Empty);
        Assert.That(box.DefaultReceiveState, Is.EqualTo(TransportBoxState.Stocked));
    }

    [Test]
    public void Open_FromNewState_ShouldTransitionToOpened()
    {
        // Arrange
        var box = new TransportBox();
        var code = "BOX-001";

        // Act
        box.Open(code, _testDate, _testUser);

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.Opened));
        Assert.That(box.Code, Is.EqualTo(code));
        Assert.That(box.Location, Is.Null);
        Assert.That(box.LastStateChanged, Is.EqualTo(_testDate));
        Assert.That(box.StateLog.Count, Is.EqualTo(1));
        Assert.That(box.StateLog.First().State, Is.EqualTo(TransportBoxState.Opened));
    }

    [Test]
    public void Open_FromInvalidState_ShouldThrowValidationException()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.Stocked);

        // Act & Assert
        var ex = Assert.Throws<AbpValidationException>(() => 
            box.Open("BOX-002", _testDate, _testUser));
        Assert.That(ex.Message, Does.Contain("Invalid state transition"));
    }

    [Test]
    public void AddItem_InOpenedState_ShouldAddSuccessfully()
    {
        // Arrange
        var box = CreateOpenedBox();
        var productCode = "PROD-001";
        var productName = "Test Product";
        var amount = 10.5;

        // Act
        var item = box.AddItem(productCode, productName, amount, _testDate, _testUser);

        // Assert
        Assert.That(box.Items.Count, Is.EqualTo(1));
        Assert.That(item.ProductCode, Is.EqualTo(productCode));
        Assert.That(item.ProductName, Is.EqualTo(productName));
        Assert.That(item.Amount, Is.EqualTo(amount));
        Assert.That(item.DateAdded, Is.EqualTo(_testDate));
        Assert.That(item.UserAdded, Is.EqualTo(_testUser));
    }

    [Test]
    public void AddItem_InNonOpenedState_ShouldThrowValidationException()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.InTransit);

        // Act & Assert
        Assert.Throws<AbpValidationException>(() => 
            box.AddItem("PROD-001", "Product", 10, _testDate, _testUser));
    }

    [Test]
    public void DeleteItem_InOpenedState_ShouldRemoveSuccessfully()
    {
        // Arrange
        var box = CreateOpenedBoxWithItems();
        var itemToDelete = box.Items.First();

        // Act
        var deletedItem = box.DeleteItem(itemToDelete.Id);

        // Assert
        Assert.That(deletedItem, Is.Not.Null);
        Assert.That(box.Items.Any(i => i.Id == itemToDelete.Id), Is.False);
    }

    [Test]
    public void ToTransit_FromOpenedState_ShouldTransitionSuccessfully()
    {
        // Arrange
        var box = CreateOpenedBoxWithItems();

        // Act
        box.ToTransit(_testDate, _testUser);

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.InTransit));
        Assert.That(box.LastStateChanged, Is.EqualTo(_testDate));
        Assert.That(box.StateLog.Count, Is.GreaterThan(1));
        Assert.That(box.StateLog.Last().State, Is.EqualTo(TransportBoxState.InTransit));
    }

    [Test]
    public void ToReserve_FromOpenedState_ShouldSetLocationAndTransition()
    {
        // Arrange
        var box = CreateOpenedBox();
        var location = TransportBoxLocation.Reserve;

        // Act
        box.ToReserve(_testDate, _testUser, location);

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.Reserve));
        Assert.That(box.Location, Is.EqualTo(location.ToString()));
    }

    [Test]
    public void Receive_FromInTransit_ShouldTransitionToReceived()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.InTransit);
        var receiveState = TransportBoxState.InSwap;

        // Act
        box.Receive(_testDate, _testUser, receiveState);

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.Received));
        Assert.That(box.DefaultReceiveState, Is.EqualTo(receiveState));
    }

    [Test]
    public void ToSwap_FromReceived_ShouldTransitionToInSwap()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.Received);

        // Act
        box.ToSwap(_testDate, _testUser);

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.InSwap));
    }

    [Test]
    public void ToPick_FromInSwap_ShouldTransitionToStocked()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.InSwap);

        // Act
        box.ToPick(_testDate, _testUser);

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.Stocked));
    }

    [Test]
    public void Close_FromAnyState_ShouldTransitionToClosed()
    {
        // Arrange
        var states = new[] 
        { 
            TransportBoxState.New, 
            TransportBoxState.Opened, 
            TransportBoxState.Stocked 
        };

        foreach (var state in states)
        {
            var box = CreateBoxInState(state);

            // Act
            box.Close(_testDate, _testUser);

            // Assert
            Assert.That(box.State, Is.EqualTo(TransportBoxState.Closed));
        }
    }

    [Test]
    public void Error_FromAnyState_ShouldTransitionToError()
    {
        // Arrange
        var box = CreateOpenedBox();
        var errorMessage = "Test error occurred";

        // Act
        box.Error(_testDate, _testUser, errorMessage);

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.Error));
        Assert.That(box.Description, Does.Contain(errorMessage));
        Assert.That(box.StateLog.Last().Description, Is.EqualTo(errorMessage));
    }

    [Test]
    public void Reset_ShouldClearBoxAndReturnToNew()
    {
        // Arrange
        var box = CreateOpenedBoxWithItems();

        // Act
        box.Reset(_testDate, _testUser);

        // Assert
        Assert.That(box.State, Is.EqualTo(TransportBoxState.New));
        Assert.That(box.Code, Is.Null);
        Assert.That(box.Items, Is.Empty);
    }

    [Test]
    public void IsInTransit_ShouldReturnTrueForTransitStates()
    {
        // Arrange & Act & Assert
        var transitStates = new[] 
        { 
            TransportBoxState.InTransit, 
            TransportBoxState.Received, 
            TransportBoxState.Opened 
        };

        foreach (var state in transitStates)
        {
            var box = CreateBoxInState(state);
            Assert.That(box.IsInTransit, Is.True, $"Failed for state {state}");
        }

        var nonTransitStates = new[] 
        { 
            TransportBoxState.New, 
            TransportBoxState.Stocked, 
            TransportBoxState.Closed 
        };

        foreach (var state in nonTransitStates)
        {
            var box = CreateBoxInState(state);
            Assert.That(box.IsInTransit, Is.False, $"Failed for state {state}");
        }
    }

    [Test]
    public void NextState_ShouldReturnCorrectTransition()
    {
        // Arrange
        var box = new TransportBox();

        // Act & Assert
        Assert.That(box.NextState, Is.EqualTo(TransportBoxState.Opened));

        box.Open("BOX-001", _testDate, _testUser);
        Assert.That(box.NextState, Is.EqualTo(TransportBoxState.InTransit));

        box.ToTransit(_testDate, _testUser);
        Assert.That(box.NextState, Is.EqualTo(TransportBoxState.Received));
    }

    [Test]
    public void PreviousState_ShouldReturnCorrectTransition()
    {
        // Arrange
        var box = CreateOpenedBox();

        // Act & Assert
        Assert.That(box.PreviousState, Is.EqualTo(TransportBoxState.New));

        box.ToTransit(_testDate, _testUser);
        box.Receive(_testDate, _testUser);
        Assert.That(box.State, Is.EqualTo(TransportBoxState.Received));
        // Received doesn't have a previous state defined
        Assert.That(box.PreviousState, Is.Null);
    }

    private TransportBox CreateOpenedBox()
    {
        var box = new TransportBox();
        box.Open("BOX-TEST", _testDate, _testUser);
        return box;
    }

    private TransportBox CreateOpenedBoxWithItems()
    {
        var box = CreateOpenedBox();
        box.AddItem("PROD-001", "Product 1", 10, _testDate, _testUser);
        box.AddItem("PROD-002", "Product 2", 20, _testDate, _testUser);
        return box;
    }

    private TransportBox CreateBoxInState(TransportBoxState targetState)
    {
        var box = new TransportBox();

        // Navigate to desired state
        switch (targetState)
        {
            case TransportBoxState.New:
                break;
            case TransportBoxState.Opened:
                box.Open("BOX-TEST", _testDate, _testUser);
                break;
            case TransportBoxState.InTransit:
                box.Open("BOX-TEST", _testDate, _testUser);
                box.AddItem("PROD-001", "Product", 10, _testDate, _testUser);
                box.ToTransit(_testDate, _testUser);
                break;
            case TransportBoxState.Received:
                box.Open("BOX-TEST", _testDate, _testUser);
                box.AddItem("PROD-001", "Product", 10, _testDate, _testUser);
                box.ToTransit(_testDate, _testUser);
                box.Receive(_testDate, _testUser);
                break;
            case TransportBoxState.InSwap:
                box.Open("BOX-TEST", _testDate, _testUser);
                box.AddItem("PROD-001", "Product", 10, _testDate, _testUser);
                box.ToTransit(_testDate, _testUser);
                box.Receive(_testDate, _testUser);
                box.ToSwap(_testDate, _testUser);
                break;
            case TransportBoxState.Stocked:
                box.Open("BOX-TEST", _testDate, _testUser);
                box.AddItem("PROD-001", "Product", 10, _testDate, _testUser);
                box.ToTransit(_testDate, _testUser);
                box.Receive(_testDate, _testUser);
                box.ToSwap(_testDate, _testUser);
                box.ToPick(_testDate, _testUser);
                break;
            case TransportBoxState.Reserve:
                box.Open("BOX-TEST", _testDate, _testUser);
                box.ToReserve(_testDate, _testUser, TransportBoxLocation.Reserve);
                break;
            case TransportBoxState.Closed:
                box.Close(_testDate, _testUser);
                break;
            case TransportBoxState.Error:
                box.Error(_testDate, _testUser, "Test error");
                break;
        }

        return box;
    }
}
```

#### TransportBoxItem Entity Tests

```csharp
[TestFixture]
public class TransportBoxItemTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateSuccessfully()
    {
        // Arrange
        var productCode = "PROD-001";
        var productName = "Test Product";
        var amount = 10.5;
        var dateAdded = DateTime.UtcNow;
        var userAdded = "test-user";

        // Act
        var item = new TransportBoxItem(productCode, productName, amount, dateAdded, userAdded);

        // Assert
        Assert.That(item.ProductCode, Is.EqualTo(productCode));
        Assert.That(item.ProductName, Is.EqualTo(productName));
        Assert.That(item.Amount, Is.EqualTo(amount));
        Assert.That(item.DateAdded, Is.EqualTo(dateAdded));
        Assert.That(item.UserAdded, Is.EqualTo(userAdded));
    }

    [Test]
    public void Create_WithEmptyProductCode_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            new TransportBoxItem("", "Product", 10, DateTime.Now, "user"));
    }

    [Test]
    public void Create_WithEmptyProductName_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            new TransportBoxItem("PROD-001", "", 10, DateTime.Now, "user"));
    }

    [Test]
    public void Create_WithZeroAmount_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            new TransportBoxItem("PROD-001", "Product", 0, DateTime.Now, "user"));
    }

    [Test]
    public void Create_WithNegativeAmount_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            new TransportBoxItem("PROD-001", "Product", -10, DateTime.Now, "user"));
    }
}
```

#### TransportBoxStateLog Entity Tests

```csharp
[TestFixture]
public class TransportBoxStateLogTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateSuccessfully()
    {
        // Arrange
        var state = TransportBoxState.Opened;
        var timestamp = DateTime.UtcNow;
        var userName = "test-user";
        var description = "Test state change";

        // Act
        var log = new TransportBoxStateLog(state, timestamp, userName, description);

        // Assert
        Assert.That(log.State, Is.EqualTo(state));
        Assert.That(log.Timestamp, Is.EqualTo(timestamp));
        Assert.That(log.UserName, Is.EqualTo(userName));
        Assert.That(log.Description, Is.EqualTo(description));
    }

    [Test]
    public void Create_WithoutDescription_ShouldCreateSuccessfully()
    {
        // Arrange
        var state = TransportBoxState.Closed;
        var timestamp = DateTime.UtcNow;
        var userName = "test-user";

        // Act
        var log = new TransportBoxStateLog(state, timestamp, userName, null);

        // Assert
        Assert.That(log.State, Is.EqualTo(state));
        Assert.That(log.Description, Is.Null);
    }
}
```

### Application Service Tests

#### TransportBoxAppService Tests

```csharp
[TestFixture]
public class TransportBoxAppServiceTests
{
    private Mock<IRepository<TransportBox, int>> _mockRepository;
    private Mock<IClock> _mockClock;
    private Mock<ICurrentUser> _mockUserProvider;
    private Mock<IEshopStockTakingDomainService> _mockStockUpService;
    private Mock<ICatalogRepository> _mockCatalogRepository;
    private Mock<ILogger<TransportBoxAppService>> _mockLogger;
    private TransportBoxAppService _service;

    [SetUp]
    public void SetUp()
    {
        _mockRepository = new Mock<IRepository<TransportBox, int>>();
        _mockClock = new Mock<IClock>();
        _mockUserProvider = new Mock<ICurrentUser>();
        _mockStockUpService = new Mock<IEshopStockTakingDomainService>();
        _mockCatalogRepository = new Mock<ICatalogRepository>();
        _mockLogger = new Mock<ILogger<TransportBoxAppService>>();

        _mockClock.Setup(x => x.Now).Returns(DateTime.UtcNow);
        _mockUserProvider.Setup(x => x.UserName).Returns("test-user");

        _service = new TransportBoxAppService(
            _mockRepository.Object,
            _mockClock.Object,
            _mockUserProvider.Object,
            _mockStockUpService.Object,
            _mockCatalogRepository.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task OpenAsync_WithValidCode_ShouldOpenBox()
    {
        // Arrange
        var box = new TransportBox();
        var dto = new OpenBoxDto { Id = 1, Code = "BOX-001" };

        _mockRepository.Setup(x => x.GetAsync(dto.Id, true, default))
            .ReturnsAsync(box);
        _mockRepository.Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, default))
            .ReturnsAsync((TransportBox)null);
        _mockRepository.Setup(x => x.GetListAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, default))
            .ReturnsAsync(new List<TransportBox>());
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), true, default))
            .ReturnsAsync((TransportBox b, bool autoSave, CancellationToken ct) => b);

        // Act
        var result = await _service.OpenAsync(dto);

        // Assert
        Assert.That(result.Code, Is.EqualTo(dto.Code));
        Assert.That(result.State, Is.EqualTo(TransportBoxState.Opened));
        
        _mockRepository.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), true, default), Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("opened")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public async Task OpenAsync_WithDuplicateCode_ShouldThrowValidationException()
    {
        // Arrange
        var box = new TransportBox();
        var existingBox = new TransportBox();
        existingBox.Open("BOX-001", DateTime.UtcNow, "other-user");
        
        var dto = new OpenBoxDto { Id = 1, Code = "BOX-001" };

        _mockRepository.Setup(x => x.GetAsync(dto.Id, true, default))
            .ReturnsAsync(box);
        _mockRepository.Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, default))
            .ReturnsAsync(existingBox);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<AbpValidationException>(() => _service.OpenAsync(dto));
        Assert.That(ex.Message, Does.Contain("Open box failed"));
        Assert.That(ex.ValidationErrors.First().ErrorMessage, Does.Contain("already box with same code"));
    }

    [Test]
    public async Task OpenAsync_ShouldAutoCloseStockedBoxesWithSameCode()
    {
        // Arrange
        var box = new TransportBox();
        var stockedBox1 = CreateStockedBox("BOX-001");
        var stockedBox2 = CreateStockedBox("BOX-001");
        
        var dto = new OpenBoxDto { Id = 1, Code = "BOX-001" };

        _mockRepository.Setup(x => x.GetAsync(dto.Id, true, default))
            .ReturnsAsync(box);
        _mockRepository.Setup(x => x.FindAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, default))
            .ReturnsAsync((TransportBox)null);
        _mockRepository.Setup(x => x.GetListAsync(It.IsAny<Expression<Func<TransportBox, bool>>>(), true, default))
            .ReturnsAsync(new List<TransportBox> { stockedBox1, stockedBox2 });
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), true, default))
            .ReturnsAsync((TransportBox b, bool autoSave, CancellationToken ct) => b);

        // Act
        await _service.OpenAsync(dto);

        // Assert
        Assert.That(stockedBox1.State, Is.EqualTo(TransportBoxState.Closed));
        Assert.That(stockedBox2.State, Is.EqualTo(TransportBoxState.Closed));
        
        _mockRepository.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), true, default), Times.Exactly(3));
    }

    [Test]
    public async Task AddItemsAsync_WithValidItems_ShouldAddSuccessfully()
    {
        // Arrange
        var box = CreateOpenedBox();
        var dto = new AddItemsDto
        {
            BoxId = 1,
            Items = new List<TransportBoxItemRequestDto>
            {
                new() { ProductCode = "PROD-001", ProductName = "Product 1", Amount = 10 },
                new() { ProductCode = "PROD-002", ProductName = "Product 2", Amount = 20 }
            }
        };

        _mockRepository.Setup(x => x.GetAsync(dto.BoxId, true, default))
            .ReturnsAsync(box);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), true, default))
            .ReturnsAsync((TransportBox b, bool autoSave, CancellationToken ct) => b);

        // Act
        var result = await _service.AddItemsAsync(dto);

        // Assert
        Assert.That(result.Items.Count, Is.EqualTo(2));
        Assert.That(result.Items[0].ProductCode, Is.EqualTo("PROD-001"));
        Assert.That(result.Items[0].Amount, Is.EqualTo(10));
        Assert.That(result.Items[1].ProductCode, Is.EqualTo("PROD-002"));
        Assert.That(result.Items[1].Amount, Is.EqualTo(20));
    }

    [Test]
    public async Task ToTransitAsync_WithCorrectCode_ShouldTransition()
    {
        // Arrange
        var box = CreateOpenedBoxWithItems();
        var dto = new ToTransitBoxDto { Id = 1, Code = box.Code };

        _mockRepository.Setup(x => x.GetAsync(dto.Id, true, default))
            .ReturnsAsync(box);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), true, default))
            .ReturnsAsync((TransportBox b, bool autoSave, CancellationToken ct) => b);

        // Act
        var result = await _service.ToTransitAsync(dto);

        // Assert
        Assert.That(result.State, Is.EqualTo(TransportBoxState.InTransit));
    }

    [Test]
    public async Task ToTransitAsync_WithWrongCode_ShouldThrowValidationException()
    {
        // Arrange
        var box = CreateOpenedBoxWithItems();
        var dto = new ToTransitBoxDto { Id = 1, Code = "WRONG-CODE" };

        _mockRepository.Setup(x => x.GetAsync(dto.Id, true, default))
            .ReturnsAsync(box);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<AbpValidationException>(() => _service.ToTransitAsync(dto));
        Assert.That(ex.Message, Does.Contain("Close box failed"));
    }

    [Test]
    public async Task ToTransitAsync_WithEmptyBox_ShouldThrowUserFriendlyException()
    {
        // Arrange
        var box = CreateOpenedBox(); // No items
        var dto = new ToTransitBoxDto { Id = 1, Code = box.Code };

        _mockRepository.Setup(x => x.GetAsync(dto.Id, true, default))
            .ReturnsAsync(box);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UserFriendlyException>(() => _service.ToTransitAsync(dto));
        Assert.That(ex.Message, Does.Contain("Cannot transit empty box"));
    }

    [Test]
    public async Task ReceiveAsync_ShouldUpdateBoxAndLogWeight()
    {
        // Arrange
        var box = CreateBoxInTransit();
        var dto = new ReceiveBoxDto 
        { 
            Id = 1, 
            Weight = 15.5,
            ReceiveState = TransportBoxState.InSwap 
        };

        _mockRepository.Setup(x => x.GetAsync(dto.Id, true, default))
            .ReturnsAsync(box);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), true, default))
            .ReturnsAsync((TransportBox b, bool autoSave, CancellationToken ct) => b);

        // Act
        var result = await _service.ReceiveAsync(dto);

        // Assert
        Assert.That(result.State, Is.EqualTo(TransportBoxState.Received));
        Assert.That(result.DefaultReceiveState, Is.EqualTo(TransportBoxState.InSwap));
        Assert.That(box.Description, Does.Contain("Weight: 15.5kg"));
    }

    [Test]
    public async Task ExecuteStockUpAsync_WithValidBox_ShouldUpdateStock()
    {
        // Arrange
        var box = CreateReceivedBoxWithItems();
        var dto = new ExecuteStockUpDto { BoxId = 1 };

        _mockRepository.Setup(x => x.GetAsync(dto.BoxId, true, default))
            .ReturnsAsync(box);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), true, default))
            .ReturnsAsync((TransportBox b, bool autoSave, CancellationToken ct) => b);

        foreach (var item in box.Items)
        {
            _mockCatalogRepository.Setup(x => x.GetAsync(item.ProductCode, true, default))
                .ReturnsAsync(new CatalogAggregate { ProductCode = item.ProductCode });
            
            _mockStockUpService.Setup(x => x.StockUpAsync(item.ProductCode, item.Amount, "test-user", default))
                .ReturnsAsync(new StockTakingResult());
        }

        // Act
        var result = await _service.ExecuteStockUpAsync(dto);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.ItemsProcessed, Is.EqualTo(box.Items.Count));
        Assert.That(result.StockUpdates.Count, Is.EqualTo(box.Items.Count));
        Assert.That(box.State, Is.EqualTo(TransportBoxState.Stocked));
    }

    [Test]
    public async Task ExecuteStockUpAsync_WithInvalidState_ShouldThrowException()
    {
        // Arrange
        var box = CreateOpenedBox(); // Wrong state
        var dto = new ExecuteStockUpDto { BoxId = 1 };

        _mockRepository.Setup(x => x.GetAsync(dto.BoxId, true, default))
            .ReturnsAsync(box);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UserFriendlyException>(() => _service.ExecuteStockUpAsync(dto));
        Assert.That(ex.Message, Does.Contain("must be in Received or InSwap state"));
    }

    [Test]
    public async Task ExecuteStockUpAsync_WithMissingProduct_ShouldHandleGracefully()
    {
        // Arrange
        var box = CreateReceivedBoxWithItems();
        var dto = new ExecuteStockUpDto { BoxId = 1 };

        _mockRepository.Setup(x => x.GetAsync(dto.BoxId, true, default))
            .ReturnsAsync(box);

        // First product exists, second doesn't
        _mockCatalogRepository.Setup(x => x.GetAsync(box.Items.First().ProductCode, true, default))
            .ReturnsAsync(new CatalogAggregate { ProductCode = box.Items.First().ProductCode });
        _mockCatalogRepository.Setup(x => x.GetAsync(box.Items.Last().ProductCode, true, default))
            .ReturnsAsync((CatalogAggregate)null);

        _mockStockUpService.Setup(x => x.StockUpAsync(It.IsAny<string>(), It.IsAny<double>(), "test-user", default))
            .ReturnsAsync(new StockTakingResult());

        // Act
        var result = await _service.ExecuteStockUpAsync(dto);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ItemsProcessed, Is.EqualTo(1));
        Assert.That(result.Errors.Count, Is.EqualTo(1));
        Assert.That(result.Errors.First(), Does.Contain("not found in catalog"));
    }

    private TransportBox CreateOpenedBox()
    {
        var box = new TransportBox();
        box.Open("BOX-TEST", DateTime.UtcNow, "test-user");
        return box;
    }

    private TransportBox CreateOpenedBoxWithItems()
    {
        var box = CreateOpenedBox();
        box.AddItem("PROD-001", "Product 1", 10, DateTime.UtcNow, "test-user");
        box.AddItem("PROD-002", "Product 2", 20, DateTime.UtcNow, "test-user");
        return box;
    }

    private TransportBox CreateBoxInTransit()
    {
        var box = CreateOpenedBoxWithItems();
        box.ToTransit(DateTime.UtcNow, "test-user");
        return box;
    }

    private TransportBox CreateReceivedBoxWithItems()
    {
        var box = CreateBoxInTransit();
        box.Receive(DateTime.UtcNow, "test-user");
        return box;
    }

    private TransportBox CreateStockedBox(string code)
    {
        var box = new TransportBox();
        box.Open(code, DateTime.UtcNow, "test-user");
        box.AddItem("PROD-001", "Product", 10, DateTime.UtcNow, "test-user");
        box.ToTransit(DateTime.UtcNow, "test-user");
        box.Receive(DateTime.UtcNow, "test-user");
        box.ToSwap(DateTime.UtcNow, "test-user");
        box.ToPick(DateTime.UtcNow, "test-user");
        return box;
    }
}
```

### State Machine Tests

#### State Transition Tests

```csharp
[TestFixture]
public class TransportBoxStateMachineTests
{
    [Test]
    public void StateMachine_ShouldDefineAllStates()
    {
        // Arrange
        var allStates = Enum.GetValues<TransportBoxState>();

        // Act & Assert
        foreach (var state in allStates)
        {
            var box = new TransportBox();
            // Should not throw when accessing transition node
            Assert.DoesNotThrow(() => 
            {
                var node = box.TransitionNode;
            });
        }
    }

    [Test]
    public void ValidTransitions_ShouldSucceed()
    {
        // Arrange
        var validTransitions = new[]
        {
            (From: TransportBoxState.New, To: TransportBoxState.Opened),
            (From: TransportBoxState.Opened, To: TransportBoxState.InTransit),
            (From: TransportBoxState.Opened, To: TransportBoxState.Reserve),
            (From: TransportBoxState.InTransit, To: TransportBoxState.Received),
            (From: TransportBoxState.Received, To: TransportBoxState.InSwap),
            (From: TransportBoxState.Received, To: TransportBoxState.Stocked),
            (From: TransportBoxState.InSwap, To: TransportBoxState.Stocked),
            (From: TransportBoxState.Reserve, To: TransportBoxState.Received),
            (From: TransportBoxState.Stocked, To: TransportBoxState.Closed)
        };

        foreach (var (from, to) in validTransitions)
        {
            // Act & Assert
            var box = CreateBoxInState(from);
            Assert.DoesNotThrow(() => TransitionToState(box, to), 
                $"Transition from {from} to {to} should succeed");
        }
    }

    [Test]
    public void InvalidTransitions_ShouldThrow()
    {
        // Arrange
        var invalidTransitions = new[]
        {
            (From: TransportBoxState.New, To: TransportBoxState.InTransit),
            (From: TransportBoxState.New, To: TransportBoxState.Received),
            (From: TransportBoxState.Opened, To: TransportBoxState.Stocked),
            (From: TransportBoxState.InTransit, To: TransportBoxState.Stocked),
            (From: TransportBoxState.Stocked, To: TransportBoxState.Opened),
            (From: TransportBoxState.Closed, To: TransportBoxState.Opened)
        };

        foreach (var (from, to) in invalidTransitions)
        {
            // Act & Assert
            var box = CreateBoxInState(from);
            Assert.Throws<AbpValidationException>(() => TransitionToState(box, to), 
                $"Transition from {from} to {to} should fail");
        }
    }

    [Test]
    public void ClosedState_ShouldBeTerminal()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.Closed);

        // Act & Assert
        Assert.That(box.NextState, Is.Null);
        Assert.That(box.PreviousState, Is.Null);
    }

    [Test]
    public void ErrorState_CanBeReachedFromAnyState()
    {
        // Arrange
        var allStates = Enum.GetValues<TransportBoxState>();

        foreach (var state in allStates.Where(s => s != TransportBoxState.Error))
        {
            // Act
            var box = CreateBoxInState(state);
            box.Error(DateTime.UtcNow, "test-user", "Test error");

            // Assert
            Assert.That(box.State, Is.EqualTo(TransportBoxState.Error));
        }
    }

    private TransportBox CreateBoxInState(TransportBoxState state)
    {
        var box = new TransportBox();
        var date = DateTime.UtcNow;
        var user = "test-user";

        // Navigate to desired state using valid transitions
        switch (state)
        {
            case TransportBoxState.New:
                break;
            case TransportBoxState.Opened:
                box.Open("BOX-001", date, user);
                break;
            case TransportBoxState.InTransit:
                box.Open("BOX-001", date, user);
                box.AddItem("PROD-001", "Product", 10, date, user);
                box.ToTransit(date, user);
                break;
            case TransportBoxState.Received:
                box.Open("BOX-001", date, user);
                box.AddItem("PROD-001", "Product", 10, date, user);
                box.ToTransit(date, user);
                box.Receive(date, user);
                break;
            case TransportBoxState.InSwap:
                box.Open("BOX-001", date, user);
                box.AddItem("PROD-001", "Product", 10, date, user);
                box.ToTransit(date, user);
                box.Receive(date, user);
                box.ToSwap(date, user);
                break;
            case TransportBoxState.Stocked:
                box.Open("BOX-001", date, user);
                box.AddItem("PROD-001", "Product", 10, date, user);
                box.ToTransit(date, user);
                box.Receive(date, user);
                box.ToSwap(date, user);
                box.ToPick(date, user);
                break;
            case TransportBoxState.Reserve:
                box.Open("BOX-001", date, user);
                box.ToReserve(date, user, TransportBoxLocation.Reserve);
                break;
            case TransportBoxState.Closed:
                box.Close(date, user);
                break;
            case TransportBoxState.Error:
                box.Error(date, user, "Test error");
                break;
        }

        return box;
    }

    private void TransitionToState(TransportBox box, TransportBoxState targetState)
    {
        var date = DateTime.UtcNow;
        var user = "test-user";

        switch (targetState)
        {
            case TransportBoxState.Opened:
                box.Open("BOX-002", date, user);
                break;
            case TransportBoxState.InTransit:
                box.ToTransit(date, user);
                break;
            case TransportBoxState.Received:
                box.Receive(date, user);
                break;
            case TransportBoxState.InSwap:
                box.ToSwap(date, user);
                break;
            case TransportBoxState.Stocked:
                box.ToPick(date, user);
                break;
            case TransportBoxState.Reserve:
                box.ToReserve(date, user, TransportBoxLocation.Reserve);
                break;
            case TransportBoxState.Closed:
                box.Close(date, user);
                break;
            case TransportBoxState.Error:
                box.Error(date, user, "Error");
                break;
        }
    }
}
```

## Integration Test Scenarios

### Repository Integration Tests

```csharp
[TestFixture]
public class TransportBoxRepositoryIntegrationTests
{
    private DbContextOptions<LogisticsDbContext> _dbContextOptions;
    private LogisticsDbContext _dbContext;
    private IRepository<TransportBox, int> _repository;

    [SetUp]
    public void SetUp()
    {
        _dbContextOptions = new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new LogisticsDbContext(_dbContextOptions);
        _repository = new EfCoreRepository<LogisticsDbContext, TransportBox, int>(_dbContext);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task FindByCodeAsync_ShouldFindActiveBoxes()
    {
        // Arrange
        var activeBox = CreateOpenedBox("BOX-001");
        var closedBox = CreateClosedBox("BOX-001");
        
        await _repository.InsertAsync(activeBox);
        await _repository.InsertAsync(closedBox);
        await _dbContext.SaveChangesAsync();

        // Act
        var query = await _repository.GetQueryableAsync();
        var found = await query
            .Where(x => x.Code == "BOX-001" && 
                       x.State != TransportBoxState.Closed && 
                       x.State != TransportBoxState.Stocked)
            .FirstOrDefaultAsync();

        // Assert
        Assert.That(found, Is.Not.Null);
        Assert.That(found.Id, Is.EqualTo(activeBox.Id));
    }

    [Test]
    public async Task GetBoxesByState_ShouldFilterCorrectly()
    {
        // Arrange
        var openedBox = CreateOpenedBox("BOX-001");
        var transitBox = CreateTransitBox("BOX-002");
        var stockedBox = CreateStockedBox("BOX-003");
        
        await _repository.InsertManyAsync(new[] { openedBox, transitBox, stockedBox });
        await _dbContext.SaveChangesAsync();

        // Act
        var query = await _repository.GetQueryableAsync();
        var openedBoxes = await query
            .Where(x => x.State == TransportBoxState.Opened)
            .ToListAsync();

        // Assert
        Assert.That(openedBoxes.Count, Is.EqualTo(1));
        Assert.That(openedBoxes.First().Code, Is.EqualTo("BOX-001"));
    }

    [Test]
    public async Task GetBoxesRequiringAction_ShouldReturnCorrectBoxes()
    {
        // Arrange
        var newBox = new TransportBox();
        var receivedBox = CreateReceivedBox("BOX-001");
        var closedBox = CreateClosedBox("BOX-002");
        
        await _repository.InsertManyAsync(new[] { newBox, receivedBox, closedBox });
        await _dbContext.SaveChangesAsync();

        // Act
        var query = await _repository.GetQueryableAsync();
        var actionRequired = await query
            .Where(x => x.State == TransportBoxState.New || 
                       x.State == TransportBoxState.Received || 
                       x.State == TransportBoxState.InSwap)
            .ToListAsync();

        // Assert
        Assert.That(actionRequired.Count, Is.EqualTo(2));
    }

    private TransportBox CreateOpenedBox(string code)
    {
        var box = new TransportBox();
        box.Open(code, DateTime.UtcNow, "test-user");
        return box;
    }

    private TransportBox CreateTransitBox(string code)
    {
        var box = CreateOpenedBox(code);
        box.AddItem("PROD-001", "Product", 10, DateTime.UtcNow, "test-user");
        box.ToTransit(DateTime.UtcNow, "test-user");
        return box;
    }

    private TransportBox CreateReceivedBox(string code)
    {
        var box = CreateTransitBox(code);
        box.Receive(DateTime.UtcNow, "test-user");
        return box;
    }

    private TransportBox CreateStockedBox(string code)
    {
        var box = CreateReceivedBox(code);
        box.ToSwap(DateTime.UtcNow, "test-user");
        box.ToPick(DateTime.UtcNow, "test-user");
        return box;
    }

    private TransportBox CreateClosedBox(string code)
    {
        var box = new TransportBox();
        box.Open(code, DateTime.UtcNow, "test-user");
        box.Close(DateTime.UtcNow, "test-user");
        return box;
    }
}
```

### Stock Integration Tests

```csharp
[TestFixture]
public class TransportBoxStockIntegrationTests
{
    private TransportBoxAppService _service;
    private InMemoryRepository<TransportBox, int> _transportBoxRepository;
    private Mock<ICatalogRepository> _mockCatalogRepository;
    private Mock<IEshopStockTakingDomainService> _mockStockService;

    [SetUp]
    public void SetUp()
    {
        _transportBoxRepository = new InMemoryRepository<TransportBox, int>();
        _mockCatalogRepository = new Mock<ICatalogRepository>();
        _mockStockService = new Mock<IEshopStockTakingDomainService>();

        var clock = new TestClock { Now = DateTime.UtcNow };
        var userProvider = new TestCurrentUser { UserName = "integration-test" };

        _service = new TransportBoxAppService(
            _transportBoxRepository,
            clock,
            userProvider,
            _mockStockService.Object,
            _mockCatalogRepository.Object,
            NullLogger<TransportBoxAppService>.Instance);
    }

    [Test]
    public async Task CompleteWorkflow_FromOpenToStocked_ShouldWork()
    {
        // Arrange - Create new box
        var box = new TransportBox();
        await _transportBoxRepository.InsertAsync(box);

        // Act & Assert - Open box
        var openResult = await _service.OpenAsync(new OpenBoxDto { Id = box.Id, Code = "INT-001" });
        Assert.That(openResult.State, Is.EqualTo(TransportBoxState.Opened));

        // Add items
        var addItemsResult = await _service.AddItemsAsync(new AddItemsDto
        {
            BoxId = box.Id,
            Items = new List<TransportBoxItemRequestDto>
            {
                new() { ProductCode = "PROD-001", ProductName = "Product 1", Amount = 10 },
                new() { ProductCode = "PROD-002", ProductName = "Product 2", Amount = 20 }
            }
        });
        Assert.That(addItemsResult.Items.Count, Is.EqualTo(2));

        // Transit
        var transitResult = await _service.ToTransitAsync(new ToTransitBoxDto { Id = box.Id, Code = "INT-001" });
        Assert.That(transitResult.State, Is.EqualTo(TransportBoxState.InTransit));

        // Receive
        var receiveResult = await _service.ReceiveAsync(new ReceiveBoxDto 
        { 
            Id = box.Id, 
            Weight = 15.5,
            ReceiveState = TransportBoxState.InSwap 
        });
        Assert.That(receiveResult.State, Is.EqualTo(TransportBoxState.Received));

        // Setup mocks for stock up
        _mockCatalogRepository.Setup(x => x.GetAsync("PROD-001", true, default))
            .ReturnsAsync(new CatalogAggregate { ProductCode = "PROD-001" });
        _mockCatalogRepository.Setup(x => x.GetAsync("PROD-002", true, default))
            .ReturnsAsync(new CatalogAggregate { ProductCode = "PROD-002" });
        
        _mockStockService.Setup(x => x.StockUpAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<string>(), default))
            .ReturnsAsync(new StockTakingResult { Success = true });

        // Execute stock up
        var stockUpResult = await _service.ExecuteStockUpAsync(new ExecuteStockUpDto { BoxId = box.Id });
        Assert.That(stockUpResult.Success, Is.True);
        Assert.That(stockUpResult.ItemsProcessed, Is.EqualTo(2));

        // Verify final state
        var finalBox = await _transportBoxRepository.GetAsync(box.Id);
        Assert.That(finalBox.State, Is.EqualTo(TransportBoxState.Stocked));

        // Verify stock service was called
        _mockStockService.Verify(x => x.StockUpAsync("PROD-001", 10, "integration-test", default), Times.Once);
        _mockStockService.Verify(x => x.StockUpAsync("PROD-002", 20, "integration-test", default), Times.Once);
    }
}
```

## Performance Test Scenarios

### Large Scale Box Management Tests

```csharp
[TestFixture]
public class TransportBoxPerformanceTests
{
    [Test]
    public void CreateAndManageLargeNumberOfBoxes_ShouldPerformWell()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();
        var boxes = new List<TransportBox>();

        // Act - Create 1000 boxes
        for (int i = 0; i < 1000; i++)
        {
            var box = new TransportBox();
            box.Open($"PERF-{i:0000}", DateTime.UtcNow, "perf-test");
            
            // Add 10 items to each box
            for (int j = 0; j < 10; j++)
            {
                box.AddItem($"PROD-{j:00}", $"Product {j}", j * 10, DateTime.UtcNow, "perf-test");
            }
            
            boxes.Add(box);
        }

        stopwatch.Stop();

        // Assert
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000)); // Should complete within 5 seconds
        Assert.That(boxes.Count, Is.EqualTo(1000));
        Assert.That(boxes.All(b => b.Items.Count == 10), Is.True);
    }

    [Test]
    public void StateTransitions_UnderLoad_ShouldMaintainIntegrity()
    {
        // Arrange
        var box = new TransportBox();
        box.Open("LOAD-001", DateTime.UtcNow, "load-test");
        
        for (int i = 0; i < 100; i++)
        {
            box.AddItem($"PROD-{i:000}", $"Product {i}", i, DateTime.UtcNow, "load-test");
        }

        var stopwatch = Stopwatch.StartNew();

        // Act - Perform multiple state transitions
        box.ToTransit(DateTime.UtcNow, "load-test");
        box.Receive(DateTime.UtcNow, "load-test");
        box.ToSwap(DateTime.UtcNow, "load-test");
        box.ToPick(DateTime.UtcNow, "load-test");
        box.Close(DateTime.UtcNow, "load-test");

        stopwatch.Stop();

        // Assert
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100)); // Should be very fast
        Assert.That(box.State, Is.EqualTo(TransportBoxState.Closed));
        Assert.That(box.StateLog.Count, Is.EqualTo(6)); // All transitions logged
    }

    [Test]
    public async Task ConcurrentBoxOperations_ShouldHandleCorrectly()
    {
        // Arrange
        var repository = new InMemoryRepository<TransportBox, int>();
        var tasks = new List<Task>();

        // Act - Create 100 boxes concurrently
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var box = new TransportBox();
                box.Open($"CONCURRENT-{index:000}", DateTime.UtcNow, $"user-{index}");
                box.AddItem("PROD-001", "Product", 10, DateTime.UtcNow, $"user-{index}");
                await repository.InsertAsync(box);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        var allBoxes = await repository.GetListAsync();
        Assert.That(allBoxes.Count, Is.EqualTo(100));
        Assert.That(allBoxes.Select(b => b.Code).Distinct().Count(), Is.EqualTo(100)); // All unique codes
    }
}
```

## End-to-End Test Scenarios

### Complete Logistics Workflow

```csharp
[TestFixture]
public class TransportBoxE2ETests
{
    [Test]
    public async Task CompleteLogisticsWorkflow_WithMultipleBoxes_ShouldWork()
    {
        // Arrange - Setup complete service stack
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        var appService = serviceProvider.GetRequiredService<ITransportBoxAppService>();

        // Act - Simulate complete workflow
        // 1. Create and open multiple boxes
        var box1Id = await CreateAndOpenBox(appService, "E2E-001");
        var box2Id = await CreateAndOpenBox(appService, "E2E-002");
        var box3Id = await CreateAndOpenBox(appService, "E2E-003");

        // 2. Add items to boxes
        await AddItemsToBox(appService, box1Id, 5);
        await AddItemsToBox(appService, box2Id, 10);
        await AddItemsToBox(appService, box3Id, 3);

        // 3. Send boxes to transit
        await TransitBox(appService, box1Id, "E2E-001");
        await TransitBox(appService, box2Id, "E2E-002");
        
        // Box 3 goes to reserve instead
        await ReserveBox(appService, box3Id);

        // 4. Receive boxes
        await ReceiveBox(appService, box1Id);
        await ReceiveBox(appService, box2Id);
        await ReceiveBox(appService, box3Id);

        // 5. Stock up boxes
        await StockUpBox(appService, box1Id);
        await StockUpBox(appService, box2Id);

        // 6. Close stocked boxes
        await CloseBox(appService, box1Id);
        await CloseBox(appService, box2Id);

        // Assert - Verify final states
        var finalBox1 = await appService.GetAsync(box1Id);
        var finalBox2 = await appService.GetAsync(box2Id);
        var finalBox3 = await appService.GetAsync(box3Id);

        Assert.That(finalBox1.State, Is.EqualTo(TransportBoxState.Closed));
        Assert.That(finalBox2.State, Is.EqualTo(TransportBoxState.Closed));
        Assert.That(finalBox3.State, Is.EqualTo(TransportBoxState.Received)); // Still in received

        // Verify audit trails
        Assert.That(finalBox1.StateLog.Count, Is.GreaterThan(5));
        Assert.That(finalBox2.StateLog.Count, Is.GreaterThan(5));
        Assert.That(finalBox3.StateLog.Count, Is.GreaterThan(3));
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configure all required services
        services.AddSingleton<IRepository<TransportBox, int>, InMemoryRepository<TransportBox, int>>();
        services.AddSingleton<IClock, TestClock>();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser { UserName = "e2e-test" });
        services.AddSingleton<ICatalogRepository>(CreateMockCatalogRepository());
        services.AddSingleton<IEshopStockTakingDomainService>(CreateMockStockService());
        services.AddSingleton<ILogger<TransportBoxAppService>>(NullLogger<TransportBoxAppService>.Instance);
        services.AddTransient<ITransportBoxAppService, TransportBoxAppService>();
    }

    private async Task<int> CreateAndOpenBox(ITransportBoxAppService service, string code)
    {
        var createDto = new TransportBoxCreateDto();
        var created = await service.CreateAsync(createDto);
        
        var openDto = new OpenBoxDto { Id = created.Id, Code = code };
        await service.OpenAsync(openDto);
        
        return created.Id;
    }

    private async Task AddItemsToBox(ITransportBoxAppService service, int boxId, int itemCount)
    {
        var items = new List<TransportBoxItemRequestDto>();
        
        for (int i = 0; i < itemCount; i++)
        {
            items.Add(new TransportBoxItemRequestDto
            {
                ProductCode = $"PROD-{i:000}",
                ProductName = $"Product {i}",
                Amount = (i + 1) * 10
            });
        }

        await service.AddItemsAsync(new AddItemsDto { BoxId = boxId, Items = items });
    }

    // Additional helper methods...
}
```

## Test Data Builders

### Transport Box Test Builder

```csharp
public class TransportBoxTestBuilder
{
    private string _code = "TEST-BOX";
    private TransportBoxState _targetState = TransportBoxState.New;
    private List<(string ProductCode, string ProductName, double Amount)> _items = new();
    private DateTime _date = DateTime.UtcNow;
    private string _user = "test-builder";

    public TransportBoxTestBuilder WithCode(string code)
    {
        _code = code;
        return this;
    }

    public TransportBoxTestBuilder InState(TransportBoxState state)
    {
        _targetState = state;
        return this;
    }

    public TransportBoxTestBuilder WithItem(string productCode, string productName, double amount)
    {
        _items.Add((productCode, productName, amount));
        return this;
    }

    public TransportBoxTestBuilder WithItems(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _items.Add(($"PROD-{i:000}", $"Product {i}", (i + 1) * 10));
        }
        return this;
    }

    public TransportBoxTestBuilder AtDate(DateTime date)
    {
        _date = date;
        return this;
    }

    public TransportBoxTestBuilder ByUser(string user)
    {
        _user = user;
        return this;
    }

    public TransportBox Build()
    {
        var box = new TransportBox();

        // Navigate to target state
        switch (_targetState)
        {
            case TransportBoxState.New:
                break;
                
            case TransportBoxState.Opened:
                box.Open(_code, _date, _user);
                AddItemsToBox(box);
                break;
                
            case TransportBoxState.InTransit:
                box.Open(_code, _date, _user);
                AddItemsToBox(box);
                if (!box.Items.Any())
                    box.AddItem("DEFAULT", "Default Item", 1, _date, _user);
                box.ToTransit(_date, _user);
                break;
                
            case TransportBoxState.Received:
                box.Open(_code, _date, _user);
                AddItemsToBox(box);
                if (!box.Items.Any())
                    box.AddItem("DEFAULT", "Default Item", 1, _date, _user);
                box.ToTransit(_date, _user);
                box.Receive(_date, _user);
                break;
                
            case TransportBoxState.InSwap:
                BuildToReceived(box);
                box.ToSwap(_date, _user);
                break;
                
            case TransportBoxState.Stocked:
                BuildToReceived(box);
                box.ToSwap(_date, _user);
                box.ToPick(_date, _user);
                break;
                
            case TransportBoxState.Reserve:
                box.Open(_code, _date, _user);
                AddItemsToBox(box);
                box.ToReserve(_date, _user, TransportBoxLocation.Reserve);
                break;
                
            case TransportBoxState.Closed:
                box.Open(_code, _date, _user);
                box.Close(_date, _user);
                break;
                
            case TransportBoxState.Error:
                box.Error(_date, _user, "Test error");
                break;
        }

        return box;
    }

    private void BuildToReceived(TransportBox box)
    {
        box.Open(_code, _date, _user);
        AddItemsToBox(box);
        if (!box.Items.Any())
            box.AddItem("DEFAULT", "Default Item", 1, _date, _user);
        box.ToTransit(_date, _user);
        box.Receive(_date, _user);
    }

    private void AddItemsToBox(TransportBox box)
    {
        foreach (var (productCode, productName, amount) in _items)
        {
            box.AddItem(productCode, productName, amount, _date, _user);
        }
    }
}
```

## Test Configuration

### Test Infrastructure Setup

```csharp
public class TransportBoxTestFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; private set; }
    public IRepository<TransportBox, int> Repository { get; private set; }
    public ITransportBoxAppService AppService { get; private set; }
    public TestClock Clock { get; private set; }
    public TestCurrentUser CurrentUser { get; private set; }

    public TransportBoxTestFixture()
    {
        var services = new ServiceCollection();
        ConfigureTestServices(services);
        ServiceProvider = services.BuildServiceProvider();
        
        Repository = ServiceProvider.GetRequiredService<IRepository<TransportBox, int>>();
        AppService = ServiceProvider.GetRequiredService<ITransportBoxAppService>();
        Clock = ServiceProvider.GetRequiredService<IClock>() as TestClock;
        CurrentUser = ServiceProvider.GetRequiredService<ICurrentUser>() as TestCurrentUser;
    }

    private void ConfigureTestServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IRepository<TransportBox, int>, InMemoryRepository<TransportBox, int>>();
        services.AddSingleton<IClock>(new TestClock { Now = DateTime.UtcNow });
        services.AddSingleton<ICurrentUser>(new TestCurrentUser { UserName = "test-fixture" });
        
        // Mock services
        var mockCatalogRepository = new Mock<ICatalogRepository>();
        mockCatalogRepository.Setup(x => x.GetAsync(It.IsAny<string>(), true, default))
            .ReturnsAsync((string code, bool includeDetails, CancellationToken ct) => 
                new CatalogAggregate { ProductCode = code });
        services.AddSingleton(mockCatalogRepository.Object);
        
        var mockStockService = new Mock<IEshopStockTakingDomainService>();
        mockStockService.Setup(x => x.StockUpAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<string>(), default))
            .ReturnsAsync(new StockTakingResult { Success = true });
        services.AddSingleton(mockStockService.Object);
        
        // Logging
        services.AddSingleton<ILogger<TransportBoxAppService>>(NullLogger<TransportBoxAppService>.Instance);
        
        // Application service
        services.AddTransient<ITransportBoxAppService, TransportBoxAppService>();
    }

    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

public class TestClock : IClock
{
    public DateTime Now { get; set; }
    public DateTimeKind Kind => DateTimeKind.Utc;
    public bool SupportsMultipleTimezone => false;
    public DateTime Normalize(DateTime dateTime) => dateTime;
}

public class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid? Id => Guid.NewGuid();
    public string UserName { get; set; }
    public string Name => UserName;
    public string SurName => "Test";
    public string PhoneNumber => "123456789";
    public bool PhoneNumberVerified => true;
    public string Email => $"{UserName}@test.com";
    public bool EmailVerified => true;
    public Guid? TenantId => null;
    public string[] Roles => new[] { "admin" };
    
    public Claim FindClaim(string claimType) => null;
    public Claim[] FindClaims(string claimType) => Array.Empty<Claim>();
    public Claim[] GetAllClaims() => Array.Empty<Claim>();
    public bool IsInRole(string roleName) => true;
}
```

## Continuous Integration Test Pipeline

### Test Categories and Execution

```bash
# Unit tests - Fast domain logic tests
dotnet test --filter "Category=Unit" --no-build --logger "console;verbosity=normal"

# Integration tests - Service and repository tests
dotnet test --filter "Category=Integration" --no-build --logger "console;verbosity=normal"

# State Machine tests - Transition validation
dotnet test --filter "Category=StateMachine" --no-build --logger "console;verbosity=normal"

# Performance tests - Load and scalability
dotnet test --filter "Category=Performance" --no-build --logger "console;verbosity=normal"

# E2E tests - Complete workflow validation
dotnet test --filter "Category=E2E" --no-build --logger "console;verbosity=normal"

# All tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

### Test Attributes

```csharp
[Category("Unit")]
[Category("Integration")]
[Category("StateMachine")]
[Category("Performance")]
[Category("E2E")]
```