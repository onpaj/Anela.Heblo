### task: refactor-service-remove-current-user-dependency

**Context:** `GiftPackageManufactureService` (Application layer, in `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`) currently injects `Anela.Heblo.Domain.Features.Users.ICurrentUserService` and calls `_currentUserService.GetCurrentUser().Name ?? "System"` inside both `CreateManufactureAsync` and `DisassembleGiftPackageAsync` to populate the `createdBy` argument of `GiftPackageManufactureLog`. This violates ADR-005 (`docs/architecture/development_guidelines.md`, §"User Identity Resolution"), which requires identity resolution to happen only inside MediatR handlers, never inside Application-layer services. This task removes that dependency from the service and its interface `IGiftPackageManufactureService`, replacing the internally-resolved value with a `string userName` parameter supplied by the caller. It also updates the one existing unit test file that constructs `GiftPackageManufactureService` directly and calls these two methods.

`GiftPackageManufactureLog`'s constructors (unchanged by this task, in `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureLog.cs`) are:
```csharp
public GiftPackageManufactureLog(
    string giftPackageCode,
    int quantityCreated,
    bool stockOverrideApplied,
    DateTime createdAt,
    string createdBy)

// Constructor for disassembly operations
public GiftPackageManufactureLog(
    string giftPackageCode,
    int quantityCreated,
    DateTime createdAt,
    string createdBy,
    GiftPackageOperationType operationType)
```

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs` (whole file, 23 lines)
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` (352 lines total; edits below touch the `using` block, the field list, the constructor, and the two methods `CreateManufactureAsync`/`DisassembleGiftPackageAsync`)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`

- [ ] **Step 1: Update the existing test to the target (post-refactor) shape — it will not compile yet**

  In `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`, the file currently starts with these `using` statements:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.Contracts;
  using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
  using Anela.Heblo.Domain.Features.Manufacture;
  using Anela.Heblo.Domain.Features.Users;
  using AutoMapper;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Moq;
  ```
  Remove the `using Anela.Heblo.Domain.Features.Users;` line (nothing else in this file uses that namespace once `ICurrentUserService`/`CurrentUser` references below are removed).

  The field declarations currently are:
  ```csharp
      private readonly Mock<IManufactureClient> _manufactureClientMock;
      private readonly Mock<IGiftPackageManufactureRepository> _giftPackageRepositoryMock;
      private readonly Mock<ILogisticsCatalogSource> _catalogSourceMock;
      private readonly Mock<ICurrentUserService> _currentUserServiceMock;
      private readonly Mock<ILogisticsStockOperationService> _stockOperationServiceMock;
      private readonly Mock<IMapper> _mapperMock;
      private readonly Mock<TimeProvider> _timeProviderMock;
      private readonly Mock<ILogger<GiftPackageManufactureService>> _loggerMock;
      private readonly GiftPackageManufactureService _service;
      private readonly DateTime _testDateTime = new DateTime(2024, 6, 15);
  ```
  Remove the `_currentUserServiceMock` field line entirely.

  The constructor body currently is:
  ```csharp
      public GiftPackageManufactureServiceTests()
      {
          _manufactureClientMock = new Mock<IManufactureClient>();
          _giftPackageRepositoryMock = new Mock<IGiftPackageManufactureRepository>();
          _catalogSourceMock = new Mock<ILogisticsCatalogSource>();
          _currentUserServiceMock = new Mock<ICurrentUserService>();
          _stockOperationServiceMock = new Mock<ILogisticsStockOperationService>();
          _mapperMock = new Mock<IMapper>();
          _timeProviderMock = new Mock<TimeProvider>();
          _loggerMock = new Mock<ILogger<GiftPackageManufactureService>>();

          _timeProviderMock.Setup(x => x.GetUtcNow())
              .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

          _service = new GiftPackageManufactureService(
              _manufactureClientMock.Object,
              _giftPackageRepositoryMock.Object,
              _catalogSourceMock.Object,
              _currentUserServiceMock.Object,
              _stockOperationServiceMock.Object,
              _mapperMock.Object,
              _timeProviderMock.Object,
              _loggerMock.Object);
      }
  ```
  Replace it with:
  ```csharp
      public GiftPackageManufactureServiceTests()
      {
          _manufactureClientMock = new Mock<IManufactureClient>();
          _giftPackageRepositoryMock = new Mock<IGiftPackageManufactureRepository>();
          _catalogSourceMock = new Mock<ILogisticsCatalogSource>();
          _stockOperationServiceMock = new Mock<ILogisticsStockOperationService>();
          _mapperMock = new Mock<IMapper>();
          _timeProviderMock = new Mock<TimeProvider>();
          _loggerMock = new Mock<ILogger<GiftPackageManufactureService>>();

          _timeProviderMock.Setup(x => x.GetUtcNow())
              .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

          _service = new GiftPackageManufactureService(
              _manufactureClientMock.Object,
              _giftPackageRepositoryMock.Object,
              _catalogSourceMock.Object,
              _stockOperationServiceMock.Object,
              _mapperMock.Object,
              _timeProviderMock.Object,
              _loggerMock.Object);
      }
  ```

  In the `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems` test method, this block currently is:
  ```csharp
          _mapperMock.Setup(x => x.Map<GiftPackageManufactureDto>(It.IsAny<GiftPackageManufactureLog>()))
              .Returns(expectedManufactureDto);

          _currentUserServiceMock.Setup(x => x.GetCurrentUser())
              .Returns(new CurrentUser(Id: "test-user-id", Name: userId, Email: "test@example.com", IsAuthenticated: true));

          _stockOperationServiceMock
              .Setup(x => x.CreateOperationAsync(
                  It.IsAny<string>(),
                  It.IsAny<string>(),
                  It.IsAny<int>(),
                  It.IsAny<LogisticsStockOperationSource>(),
                  It.IsAny<int>(),
                  It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

          // Act
          var result = await _service.CreateManufactureAsync(giftPackageCode, quantity, false, CancellationToken.None);
  ```
  Replace it with:
  ```csharp
          _mapperMock.Setup(x => x.Map<GiftPackageManufactureDto>(It.IsAny<GiftPackageManufactureLog>()))
              .Returns(expectedManufactureDto);

          _stockOperationServiceMock
              .Setup(x => x.CreateOperationAsync(
                  It.IsAny<string>(),
                  It.IsAny<string>(),
                  It.IsAny<int>(),
                  It.IsAny<LogisticsStockOperationSource>(),
                  It.IsAny<int>(),
                  It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

          // Act
          var result = await _service.CreateManufactureAsync(giftPackageCode, quantity, false, userId, CancellationToken.None);
  ```
  (The `userId` local variable is already declared earlier in this test as `var userId = "testUser";` — it is now passed directly as the literal `userName` argument instead of being wired through a mocked `ICurrentUserService`. The existing assertions `result.CreatedBy.Should().Be(userId);` and the `_giftPackageRepositoryMock.Verify(... log.CreatedBy == userId ...)` call further down in the same test are unaffected and need no changes.)

- [ ] **Step 2: Run the test project and confirm it currently fails to compile**
  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
  ```
  Expect a build error, e.g. `CS7036: There is no argument given that corresponds to the required parameter 'userName'` (or a constructor-argument-count mismatch), because `GiftPackageManufactureService` and `IGiftPackageManufactureService` have not been updated yet. This confirms the test now encodes the target behavior.

- [ ] **Step 3: Update `IGiftPackageManufactureService` to add the `userName` parameter to both methods**

  Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs` with:
  ```csharp
  using System.ComponentModel;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

  namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;

  public interface IGiftPackageManufactureService
  {
      Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);

      Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);

      [DisplayName("GiftPackageManufacture-{0}-{1}x")]
      Task<GiftPackageManufactureDto> CreateManufactureAsync(
          string giftPackageCode,
          int quantity,
          bool allowStockOverride,
          string userName,
          CancellationToken cancellationToken = default);

      Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
          string giftPackageCode,
          int quantity,
          string userName,
          CancellationToken cancellationToken = default);
  }
  ```
  (Only change from the original: `string userName,` inserted before `CancellationToken cancellationToken = default` in both method signatures. The `[DisplayName("GiftPackageManufacture-{0}-{1}x")]` attribute is left exactly as-is — its `{0}`/`{1}` placeholders index `giftPackageCode`/`quantity`, the first two parameters, which are unaffected by this insertion.)

- [ ] **Step 4: Update `GiftPackageManufactureService` — remove `ICurrentUserService`, add `userName` parameters**

  In `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`:

  a) The `using` block currently is:
  ```csharp
  using System.ComponentModel;
  using Anela.Heblo.Application.Features.Logistics.Contracts;
  using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
  using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
  using Anela.Heblo.Domain.Features.Manufacture;
  using Anela.Heblo.Domain.Features.Users;
  using AutoMapper;
  using Microsoft.Extensions.Logging;
  ```
  Remove the `using Anela.Heblo.Domain.Features.Users;` line.

  b) The field list currently is:
  ```csharp
      private readonly IManufactureClient _manufactureClient;
      private readonly IGiftPackageManufactureRepository _giftPackageRepository;
      private readonly ILogisticsCatalogSource _catalogSource;
      private readonly ICurrentUserService _currentUserService;
      private readonly ILogisticsStockOperationService _stockOperationService;
      private readonly IMapper _mapper;
      private readonly TimeProvider _timeProvider;
      private readonly ILogger<GiftPackageManufactureService> _logger;
  ```
  Remove the `_currentUserService` field line.

  c) The constructor currently is:
  ```csharp
      public GiftPackageManufactureService(
          IManufactureClient manufactureClient,
          IGiftPackageManufactureRepository giftPackageRepository,
          ILogisticsCatalogSource catalogSource,
          ICurrentUserService currentUserService,
          ILogisticsStockOperationService stockOperationService,
          IMapper mapper,
          TimeProvider timeProvider,
          ILogger<GiftPackageManufactureService> logger)
      {
          _manufactureClient = manufactureClient;
          _giftPackageRepository = giftPackageRepository;
          _catalogSource = catalogSource;
          _currentUserService = currentUserService;
          _stockOperationService = stockOperationService;
          _mapper = mapper;
          _timeProvider = timeProvider;
          _logger = logger;
      }
  ```
  Replace it with:
  ```csharp
      public GiftPackageManufactureService(
          IManufactureClient manufactureClient,
          IGiftPackageManufactureRepository giftPackageRepository,
          ILogisticsCatalogSource catalogSource,
          ILogisticsStockOperationService stockOperationService,
          IMapper mapper,
          TimeProvider timeProvider,
          ILogger<GiftPackageManufactureService> logger)
      {
          _manufactureClient = manufactureClient;
          _giftPackageRepository = giftPackageRepository;
          _catalogSource = catalogSource;
          _stockOperationService = stockOperationService;
          _mapper = mapper;
          _timeProvider = timeProvider;
          _logger = logger;
      }
  ```

  d) `CreateManufactureAsync`'s signature and log construction currently are:
  ```csharp
      [DisplayName("GiftPackageManufacture-{0}-{1}")]
      public async Task<GiftPackageManufactureDto> CreateManufactureAsync(
          string giftPackageCode,
          int quantity,
          bool allowStockOverride,
          CancellationToken cancellationToken = default)
      {
          // Create the manufacture log
          var manufactureLog = new GiftPackageManufactureLog(
              giftPackageCode,
              quantity,
              allowStockOverride,
              _timeProvider.GetUtcNow().DateTime,
              _currentUserService.GetCurrentUser().Name ?? "System");
  ```
  Replace it with:
  ```csharp
      [DisplayName("GiftPackageManufacture-{0}-{1}")]
      public async Task<GiftPackageManufactureDto> CreateManufactureAsync(
          string giftPackageCode,
          int quantity,
          bool allowStockOverride,
          string userName,
          CancellationToken cancellationToken = default)
      {
          // Create the manufacture log
          var manufactureLog = new GiftPackageManufactureLog(
              giftPackageCode,
              quantity,
              allowStockOverride,
              _timeProvider.GetUtcNow().DateTime,
              userName);
  ```
  (Everything else in this method — from `// CRITICAL: Save the log FIRST...` through the closing `return _mapper.Map<GiftPackageManufactureDto>(manufactureLog);` — is unchanged.)

  e) `DisassembleGiftPackageAsync`'s signature and log construction currently are:
  ```csharp
      public async Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
          string giftPackageCode,
          int quantity,
          CancellationToken cancellationToken = default)
      {
          // 1. Validate quantity
          if (quantity <= 0)
          {
              throw new ArgumentException("Množství musí být větší než 0", nameof(quantity));
          }

          // Get gift package details with current stock
          var giftPackage = await GetGiftPackageDetailAsync(giftPackageCode, 1.0m, null, null, cancellationToken);

          // Validate quantity against available stock
          if (quantity > giftPackage.AvailableStock)
          {
              throw new InvalidOperationException(
                  $"Nelze rozebrat {quantity} ks. Dostupné množství: {giftPackage.AvailableStock} ks");
          }

          // 2. Create log entry with OperationType.Disassembly
          var disassemblyLog = new GiftPackageManufactureLog(
              giftPackageCode,
              quantity,
              _timeProvider.GetUtcNow().DateTime,
              _currentUserService.GetCurrentUser().Name ?? "System",
              GiftPackageOperationType.Disassembly);
  ```
  Replace it with:
  ```csharp
      public async Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
          string giftPackageCode,
          int quantity,
          string userName,
          CancellationToken cancellationToken = default)
      {
          // 1. Validate quantity
          if (quantity <= 0)
          {
              throw new ArgumentException("Množství musí být větší než 0", nameof(quantity));
          }

          // Get gift package details with current stock
          var giftPackage = await GetGiftPackageDetailAsync(giftPackageCode, 1.0m, null, null, cancellationToken);

          // Validate quantity against available stock
          if (quantity > giftPackage.AvailableStock)
          {
              throw new InvalidOperationException(
                  $"Nelze rozebrat {quantity} ks. Dostupné množství: {giftPackage.AvailableStock} ks");
          }

          // 2. Create log entry with OperationType.Disassembly
          var disassemblyLog = new GiftPackageManufactureLog(
              giftPackageCode,
              quantity,
              _timeProvider.GetUtcNow().DateTime,
              userName,
              GiftPackageOperationType.Disassembly);
  ```
  (Everything else in this method — from `// CRITICAL: Save the log FIRST...` through the closing `return new GiftPackageDisassemblyDto { ... };` — is unchanged.)

  No other part of the file (`GetAvailableGiftPackagesAsync`, `GetGiftPackageDetailAsync`, `ResolveDateRange`, `ComputePackageMetrics`, `CalculateSeverity`, `CalculateStockCoveragePercent`) changes.

- [ ] **Step 5: Run the test project and confirm it now builds and passes**
  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
  ```
  Expect all tests in `GiftPackageManufactureServiceTests` to pass (9 tests: `GetAvailableGiftPackagesAsync_ShouldReturnGiftPackagesWithCorrectDailySales`, `GetAvailableGiftPackagesAsync_WithNoSetProducts_ShouldReturnEmptyList`, `GetGiftPackageDetailAsync_ShouldReturnGiftPackageWithIngredients`, `GetGiftPackageDetailAsync_WithNonExistentProduct_ShouldThrowArgumentException`, `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems`, `GetAvailableGiftPackagesAsync_WithZeroDaysDiff_ShouldUseDaysDiffAsOne`, `GetAvailableGiftPackagesAsync_WithCustomDateRange_ShouldUseSpecifiedDates`, `GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates`, `GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient`, `GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage`).

  Also confirm the whole solution still builds (the two handlers still call the old 4-arg/3-arg overloads and will now fail — this is expected and fixed in the next two tasks):
  ```bash
  dotnet build backend/Anela.Heblo.sln
  ```
  Expect build errors only in `CreateGiftPackageManufactureHandler.cs` and `DisassembleGiftPackageHandler.cs` (missing-argument errors for `CreateManufactureAsync`/`DisassembleGiftPackageAsync`). No other errors should appear.

- [ ] **Step 6: Commit**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs \
          backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs \
          backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs
  git commit -m "$(cat <<'EOF'
  refactor(gift-package-manufacture): remove ICurrentUserService from service, accept userName parameter

  ADR-005 requires identity resolution to happen only inside MediatR handlers.
  GiftPackageManufactureService resolved the current user internally; it now
  receives a plain userName string from its callers instead.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BuEi7rZcmaqdArYLbFx8n1
  EOF
  )"
  ```

---

