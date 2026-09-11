# GiftPackageManufactureService Identity Resolution Refactor — Implementation Plan

**Goal:** Remove `ICurrentUserService` from `GiftPackageManufactureService` and move identity resolution into `CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler`, so the Application-layer service only receives a plain `userName` string, per ADR-005.
**Architecture:** `IGiftPackageManufactureService.CreateManufactureAsync` and `DisassembleGiftPackageAsync` each gain a `string userName` parameter inserted immediately before `CancellationToken cancellationToken = default`. `GiftPackageManufactureService` drops its `ICurrentUserService` constructor dependency entirely and uses the incoming `userName` verbatim (no internal fallback). Both handlers gain an `ICurrentUserService` constructor dependency, call `GetCurrentUser()` exactly once at the top of `Handle()`, and pass `user.Name ?? "System"` into the service call. No HTTP contract, controller, DI registration, or domain entity changes.
**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions

---

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

### task: inject-current-user-into-create-handler

**Context:** `IGiftPackageManufactureService.CreateManufactureAsync` now has the signature `Task<GiftPackageManufactureDto> CreateManufactureAsync(string giftPackageCode, int quantity, bool allowStockOverride, string userName, CancellationToken cancellationToken = default)` (this is assumed already done by a prior task in this plan — `GiftPackageManufactureService` no longer resolves identity internally). `CreateGiftPackageManufactureHandler` (in `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureHandler.cs`) currently calls the old 4-argument overload and does not resolve identity at all. This task makes the handler the ADR-005-compliant identity-resolution boundary for this use case: it injects `Anela.Heblo.Domain.Features.Users.ICurrentUserService`, resolves `_currentUserService.GetCurrentUser()` exactly once at the top of `Handle()`, and passes `user.Name ?? "System"` as the new `userName` argument. This mirrors the established pattern in `CreateNewTransportBoxHandler` (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/CreateNewTransportBox/CreateNewTransportBoxHandler.cs`), which does `var currentUser = _currentUserService.GetCurrentUser(); var userName = currentUser.Name;` at the top of its `Handle()`.

There is currently no test file for `CreateGiftPackageManufactureHandler` anywhere under `backend/test/Anela.Heblo.Tests/`. This task adds one.

Relevant unchanged types, for reference:
```csharp
// backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureRequest.cs
public class CreateGiftPackageManufactureRequest : IRequest<CreateGiftPackageManufactureResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public int Quantity { get; set; }
    public bool AllowStockOverride { get; set; }
}

// backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureResponse.cs
public class CreateGiftPackageManufactureResponse : BaseResponse // BaseResponse.Success defaults to true
{
    public GiftPackageManufactureDto Manufacture { get; set; } = null!;
}

// backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Contracts/GiftPackageManufactureDto.cs
public class GiftPackageManufactureDto
{
    public int Id { get; set; }
    public string GiftPackageCode { get; set; } = null!;
    public int QuantityCreated { get; set; }
    public bool StockOverrideApplied { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public List<GiftPackageManufactureItemDto> ConsumedItems { get; set; } = new();
}

// backend/src/Anela.Heblo.Domain/Features/Users/CurrentUser.cs
public record CurrentUser(
    string? Id,
    string? Name,
    string? Email,
    bool IsAuthenticated
);

// backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs
public interface ICurrentUserService
{
    CurrentUser GetCurrentUser();
    bool IsInRole(string role);
}
```

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureHandler.cs` (whole file, currently 28 lines)
- Create: `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/CreateGiftPackageManufactureHandlerTests.cs`

- [ ] **Step 1: Write the failing test for the new handler behavior**

  Create `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/CreateGiftPackageManufactureHandlerTests.cs` with:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;
  using Anela.Heblo.Domain.Features.Users;
  using FluentAssertions;
  using Moq;

  namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

  public class CreateGiftPackageManufactureHandlerTests
  {
      private readonly Mock<IGiftPackageManufactureService> _serviceMock = new();
      private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

      private CreateGiftPackageManufactureHandler CreateSut() =>
          new(_serviceMock.Object, _currentUserServiceMock.Object);

      [Fact]
      public async Task Handle_ForwardsResolvedUserName_ToCreateManufactureAsync()
      {
          // Arrange
          _currentUserServiceMock
              .Setup(x => x.GetCurrentUser())
              .Returns(new CurrentUser(Id: "user-1", Name: "jane.doe", Email: "jane.doe@example.com", IsAuthenticated: true));

          var manufacture = new GiftPackageManufactureDto
          {
              GiftPackageCode = "SET001",
              QuantityCreated = 3,
              CreatedBy = "jane.doe",
              CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
          };

          _serviceMock
              .Setup(s => s.CreateManufactureAsync("SET001", 3, false, "jane.doe", It.IsAny<CancellationToken>()))
              .ReturnsAsync(manufacture);

          var request = new CreateGiftPackageManufactureRequest
          {
              GiftPackageCode = "SET001",
              Quantity = 3,
              AllowStockOverride = false
          };

          // Act
          var result = await CreateSut().Handle(request, CancellationToken.None);

          // Assert
          result.Manufacture.Should().BeSameAs(manufacture);
          _serviceMock.Verify(
              s => s.CreateManufactureAsync("SET001", 3, false, "jane.doe", It.IsAny<CancellationToken>()),
              Times.Once);
          _currentUserServiceMock.Verify(x => x.GetCurrentUser(), Times.Once);
      }

      [Fact]
      public async Task Handle_FallsBackToSystem_WhenCurrentUserNameIsNull()
      {
          // Arrange
          _currentUserServiceMock
              .Setup(x => x.GetCurrentUser())
              .Returns(new CurrentUser(Id: "user-1", Name: null, Email: null, IsAuthenticated: true));

          var manufacture = new GiftPackageManufactureDto
          {
              GiftPackageCode = "SET001",
              QuantityCreated = 1,
              CreatedBy = "System",
              CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
          };

          _serviceMock
              .Setup(s => s.CreateManufactureAsync("SET001", 1, true, "System", It.IsAny<CancellationToken>()))
              .ReturnsAsync(manufacture);

          var request = new CreateGiftPackageManufactureRequest
          {
              GiftPackageCode = "SET001",
              Quantity = 1,
              AllowStockOverride = true
          };

          // Act
          var result = await CreateSut().Handle(request, CancellationToken.None);

          // Assert
          result.Manufacture.Should().BeSameAs(manufacture);
          _serviceMock.Verify(
              s => s.CreateManufactureAsync("SET001", 1, true, "System", It.IsAny<CancellationToken>()),
              Times.Once);
      }
  }
  ```

- [ ] **Step 2: Run the new test and confirm it fails to compile**
  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateGiftPackageManufactureHandlerTests"
  ```
  Expect a build error: `CreateGiftPackageManufactureHandler` has no constructor overload taking two arguments (`_serviceMock.Object, _currentUserServiceMock.Object`) yet.

- [ ] **Step 3: Update `CreateGiftPackageManufactureHandler` to inject `ICurrentUserService` and resolve identity**

  Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureHandler.cs` with:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using Anela.Heblo.Domain.Features.Users;
  using MediatR;

  namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;

  public class CreateGiftPackageManufactureHandler : IRequestHandler<CreateGiftPackageManufactureRequest, CreateGiftPackageManufactureResponse>
  {
      private readonly IGiftPackageManufactureService _giftPackageService;
      private readonly ICurrentUserService _currentUserService;

      public CreateGiftPackageManufactureHandler(
          IGiftPackageManufactureService giftPackageService,
          ICurrentUserService currentUserService)
      {
          _giftPackageService = giftPackageService;
          _currentUserService = currentUserService;
      }

      public async Task<CreateGiftPackageManufactureResponse> Handle(CreateGiftPackageManufactureRequest request, CancellationToken cancellationToken)
      {
          var user = _currentUserService.GetCurrentUser();
          var manufacture = await _giftPackageService.CreateManufactureAsync(
              request.GiftPackageCode,
              request.Quantity,
              request.AllowStockOverride,
              user.Name ?? "System",
              cancellationToken);

          return new CreateGiftPackageManufactureResponse
          {
              Manufacture = manufacture
          };
      }
  }
  ```
  No changes to `CreateGiftPackageManufactureRequest`, `CreateGiftPackageManufactureResponse`, or the controller that dispatches this request via MediatR — this is a purely internal handler change. No DI registration change is needed: `ICurrentUserService` is already registered by `UsersModule.AddUsersModule()` in `Anela.Heblo.API`.

- [ ] **Step 4: Run the tests and confirm they pass**
  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateGiftPackageManufactureHandlerTests"
  ```
  Expect both `Handle_ForwardsResolvedUserName_ToCreateManufactureAsync` and `Handle_FallsBackToSystem_WhenCurrentUserNameIsNull` to pass.

- [ ] **Step 5: Commit**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureHandler.cs \
          backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/CreateGiftPackageManufactureHandlerTests.cs
  git commit -m "$(cat <<'EOF'
  feat(gift-package-manufacture): resolve identity in CreateGiftPackageManufactureHandler

  Per ADR-005, identity resolution moves from GiftPackageManufactureService
  into the handler. CreateGiftPackageManufactureHandler now injects
  ICurrentUserService and forwards the resolved user name to the service.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BuEi7rZcmaqdArYLbFx8n1
  EOF
  )"
  ```

---

### task: inject-current-user-into-disassemble-handler

**Context:** `IGiftPackageManufactureService.DisassembleGiftPackageAsync` now has the signature `Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(string giftPackageCode, int quantity, string userName, CancellationToken cancellationToken = default)` (assumed already done by a prior task in this plan — `GiftPackageManufactureService` no longer resolves identity internally). `DisassembleGiftPackageHandler` (in `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageHandler.cs`) currently calls the old 2-argument overload and does not resolve identity. This task injects `Anela.Heblo.Domain.Features.Users.ICurrentUserService`, resolves `_currentUserService.GetCurrentUser()` exactly once at the top of `Handle()` (before the existing `try` block), and passes `user.Name ?? "System"` as the new `userName` argument — while preserving the existing `try`/`catch` blocks for `InvalidOperationException` and `ArgumentException` unchanged. It also updates the existing test file `DisassembleGiftPackageHandlerTests.cs`, which constructs the handler with only one constructor argument today and asserts against the old 3-argument service call.

Relevant unchanged types, for reference:
```csharp
// backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageRequest.cs
public class DisassembleGiftPackageRequest : IRequest<DisassembleGiftPackageResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public int Quantity { get; set; }
}

// backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageResponse.cs
public class DisassembleGiftPackageResponse : BaseResponse // BaseResponse.Success defaults to true
{
    public GiftPackageDisassemblyDto Disassembly { get; set; } = null!;
}

// backend/src/Anela.Heblo.Domain/Features/Users/CurrentUser.cs
public record CurrentUser(
    string? Id,
    string? Name,
    string? Email,
    bool IsAuthenticated
);

// backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs
public interface ICurrentUserService
{
    CurrentUser GetCurrentUser();
    bool IsInRole(string role);
}

// backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs (enum; ErrorCodes.InvalidOperation and ErrorCodes.InvalidValue already used by this handler, unaffected by this task)
```

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageHandler.cs` (whole file, currently 56 lines)
- Modify: `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/DisassembleGiftPackageHandlerTests.cs`

- [ ] **Step 1: Update the existing test to the target (post-refactor) shape — it will not compile yet**

  The full current contents of `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/DisassembleGiftPackageHandlerTests.cs` are:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;
  using Anela.Heblo.Application.Shared;
  using FluentAssertions;
  using Moq;

  namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

  public class DisassembleGiftPackageHandlerTests
  {
      private readonly Mock<IGiftPackageManufactureService> _serviceMock = new();

      private DisassembleGiftPackageHandler CreateSut() =>
          new(_serviceMock.Object);

      [Fact]
      public async Task Handle_ReturnsSuccessWithDisassembly_WhenServiceSucceeds()
      {
          // Arrange
          var disassembly = new GiftPackageDisassemblyDto
          {
              GiftPackageCode = "SET001",
              QuantityDisassembled = 2,
              DisassembledAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
              DisassembledBy = "test-user",
              ReturnedComponents = new List<GiftPackageDisassemblyItemDto>()
          };

          _serviceMock
              .Setup(s => s.DisassembleGiftPackageAsync("SET001", 2, It.IsAny<CancellationToken>()))
              .ReturnsAsync(disassembly);

          var request = new DisassembleGiftPackageRequest
          {
              GiftPackageCode = "SET001",
              Quantity = 2
          };

          // Act
          var result = await CreateSut().Handle(request, CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          result.ErrorCode.Should().BeNull();
          result.Disassembly.GiftPackageCode.Should().Be("SET001");
          result.Disassembly.QuantityDisassembled.Should().Be(2);

          _serviceMock.Verify(
              s => s.DisassembleGiftPackageAsync("SET001", 2, It.IsAny<CancellationToken>()),
              Times.Once);
      }

      [Fact]
      public async Task Handle_ReturnsInvalidOperation_WhenServiceThrowsInvalidOperationException()
      {
          // Arrange
          _serviceMock
              .Setup(s => s.DisassembleGiftPackageAsync(
                  It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("Package SET001 does not exist"));

          var request = new DisassembleGiftPackageRequest
          {
              GiftPackageCode = "SET001",
              Quantity = 2
          };

          // Act
          var result = await CreateSut().Handle(request, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
          result.Params.Should().ContainKey("ErrorMessage")
              .WhoseValue.Should().Be("Package SET001 does not exist");
      }

      [Fact]
      public async Task Handle_ReturnsInvalidValue_WhenServiceThrowsArgumentException()
      {
          // Arrange
          // Use single-argument constructor — two-argument ctor appends " (Parameter 'name')" to Message.
          _serviceMock
              .Setup(s => s.DisassembleGiftPackageAsync(
                  It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new ArgumentException("Quantity must be greater than zero"));

          var request = new DisassembleGiftPackageRequest
          {
              GiftPackageCode = "SET001",
              Quantity = -1
          };

          // Act
          var result = await CreateSut().Handle(request, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
          result.ErrorCode.Should().NotBe(ErrorCodes.InvalidOperation);
          result.Params.Should().ContainKey("ErrorMessage")
              .WhoseValue.Should().Be("Quantity must be greater than zero");
      }
  }
  ```

  Replace the entire file with:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.Users;
  using FluentAssertions;
  using Moq;

  namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

  public class DisassembleGiftPackageHandlerTests
  {
      private readonly Mock<IGiftPackageManufactureService> _serviceMock = new();
      private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

      private DisassembleGiftPackageHandler CreateSut()
      {
          _currentUserServiceMock
              .Setup(x => x.GetCurrentUser())
              .Returns(new CurrentUser(Id: "user-1", Name: "test-user", Email: "test-user@example.com", IsAuthenticated: true));
          return new(_serviceMock.Object, _currentUserServiceMock.Object);
      }

      [Fact]
      public async Task Handle_ReturnsSuccessWithDisassembly_WhenServiceSucceeds()
      {
          // Arrange
          var disassembly = new GiftPackageDisassemblyDto
          {
              GiftPackageCode = "SET001",
              QuantityDisassembled = 2,
              DisassembledAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
              DisassembledBy = "test-user",
              ReturnedComponents = new List<GiftPackageDisassemblyItemDto>()
          };

          _serviceMock
              .Setup(s => s.DisassembleGiftPackageAsync("SET001", 2, "test-user", It.IsAny<CancellationToken>()))
              .ReturnsAsync(disassembly);

          var request = new DisassembleGiftPackageRequest
          {
              GiftPackageCode = "SET001",
              Quantity = 2
          };

          // Act
          var result = await CreateSut().Handle(request, CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          result.ErrorCode.Should().BeNull();
          result.Disassembly.GiftPackageCode.Should().Be("SET001");
          result.Disassembly.QuantityDisassembled.Should().Be(2);

          _serviceMock.Verify(
              s => s.DisassembleGiftPackageAsync("SET001", 2, "test-user", It.IsAny<CancellationToken>()),
              Times.Once);
      }

      [Fact]
      public async Task Handle_ReturnsInvalidOperation_WhenServiceThrowsInvalidOperationException()
      {
          // Arrange
          _serviceMock
              .Setup(s => s.DisassembleGiftPackageAsync(
                  It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("Package SET001 does not exist"));

          var request = new DisassembleGiftPackageRequest
          {
              GiftPackageCode = "SET001",
              Quantity = 2
          };

          // Act
          var result = await CreateSut().Handle(request, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
          result.Params.Should().ContainKey("ErrorMessage")
              .WhoseValue.Should().Be("Package SET001 does not exist");
      }

      [Fact]
      public async Task Handle_ReturnsInvalidValue_WhenServiceThrowsArgumentException()
      {
          // Arrange
          // Use single-argument constructor — two-argument ctor appends " (Parameter 'name')" to Message.
          _serviceMock
              .Setup(s => s.DisassembleGiftPackageAsync(
                  It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new ArgumentException("Quantity must be greater than zero"));

          var request = new DisassembleGiftPackageRequest
          {
              GiftPackageCode = "SET001",
              Quantity = -1
          };

          // Act
          var result = await CreateSut().Handle(request, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
          result.ErrorCode.Should().NotBe(ErrorCodes.InvalidOperation);
          result.Params.Should().ContainKey("ErrorMessage")
              .WhoseValue.Should().Be("Quantity must be greater than zero");
      }

      [Fact]
      public async Task Handle_ForwardsSystemFallback_WhenCurrentUserNameIsNull()
      {
          // Arrange
          _currentUserServiceMock
              .Setup(x => x.GetCurrentUser())
              .Returns(new CurrentUser(Id: "user-1", Name: null, Email: null, IsAuthenticated: true));

          var disassembly = new GiftPackageDisassemblyDto
          {
              GiftPackageCode = "SET001",
              QuantityDisassembled = 1,
              DisassembledAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
              DisassembledBy = "System",
              ReturnedComponents = new List<GiftPackageDisassemblyItemDto>()
          };

          _serviceMock
              .Setup(s => s.DisassembleGiftPackageAsync("SET001", 1, "System", It.IsAny<CancellationToken>()))
              .ReturnsAsync(disassembly);

          var request = new DisassembleGiftPackageRequest
          {
              GiftPackageCode = "SET001",
              Quantity = 1
          };

          // Act
          var handler = new DisassembleGiftPackageHandler(_serviceMock.Object, _currentUserServiceMock.Object);
          var result = await handler.Handle(request, CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          _serviceMock.Verify(
              s => s.DisassembleGiftPackageAsync("SET001", 1, "System", It.IsAny<CancellationToken>()),
              Times.Once);
      }
  }
  ```
  (Changes: added `using Anela.Heblo.Domain.Features.Users;`; added `_currentUserServiceMock` field; `CreateSut()` now sets up `GetCurrentUser()` to return a `CurrentUser` with `Name: "test-user"` and passes `_currentUserServiceMock.Object` as the second constructor argument; all three existing `DisassembleGiftPackageAsync` `Setup`/`Verify` call sites gain a fourth argument — `"test-user"` where a literal userName is expected, `It.IsAny<string>()` where only the exception path matters; a new `Handle_ForwardsSystemFallback_WhenCurrentUserNameIsNull` test is added, constructing the handler directly since it needs a different `GetCurrentUser()` setup than `CreateSut()`'s default.)

- [ ] **Step 2: Run the test project and confirm it currently fails to compile**
  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DisassembleGiftPackageHandlerTests"
  ```
  Expect a build error: `DisassembleGiftPackageHandler` has no constructor overload taking two arguments yet, and `DisassembleGiftPackageAsync` has no overload taking 4 arguments (`string, int, string, CancellationToken`) as called from the test — the interface/service side of this was already fixed in a prior task, but the handler itself still only calls the old 3-argument overload internally, so the handler-side change below is what's missing.

- [ ] **Step 3: Update `DisassembleGiftPackageHandler` to inject `ICurrentUserService` and resolve identity**

  Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageHandler.cs` with:
  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.Users;
  using MediatR;

  namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;

  public class DisassembleGiftPackageHandler : IRequestHandler<DisassembleGiftPackageRequest, DisassembleGiftPackageResponse>
  {
      private readonly IGiftPackageManufactureService _giftPackageService;
      private readonly ICurrentUserService _currentUserService;

      public DisassembleGiftPackageHandler(
          IGiftPackageManufactureService giftPackageService,
          ICurrentUserService currentUserService)
      {
          _giftPackageService = giftPackageService;
          _currentUserService = currentUserService;
      }

      public async Task<DisassembleGiftPackageResponse> Handle(DisassembleGiftPackageRequest request, CancellationToken cancellationToken)
      {
          var user = _currentUserService.GetCurrentUser();

          try
          {
              var disassembly = await _giftPackageService.DisassembleGiftPackageAsync(
                  request.GiftPackageCode,
                  request.Quantity,
                  user.Name ?? "System",
                  cancellationToken);

              return new DisassembleGiftPackageResponse
              {
                  Disassembly = disassembly
              };
          }
          catch (InvalidOperationException ex)
          {
              return new DisassembleGiftPackageResponse
              {
                  Success = false,
                  ErrorCode = ErrorCodes.InvalidOperation,
                  Params = new Dictionary<string, string>
                  {
                      { "ErrorMessage", ex.Message }
                  }
              };
          }
          catch (ArgumentException ex)
          {
              return new DisassembleGiftPackageResponse
              {
                  Success = false,
                  ErrorCode = ErrorCodes.InvalidValue,
                  Params = new Dictionary<string, string>
                  {
                      { "ErrorMessage", ex.Message }
                  }
              };
          }
      }
  }
  ```
  (Identity is resolved once, before the `try` block — `GetCurrentUser()` cannot itself throw `InvalidOperationException`/`ArgumentException`, so this placement does not change the existing error-handling behavior for those two exception types. The `try`/`catch` structure and both `catch` bodies are byte-for-byte unchanged from the original.)

- [ ] **Step 4: Run the tests and confirm they pass**
  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DisassembleGiftPackageHandlerTests"
  ```
  Expect all four tests (`Handle_ReturnsSuccessWithDisassembly_WhenServiceSucceeds`, `Handle_ReturnsInvalidOperation_WhenServiceThrowsInvalidOperationException`, `Handle_ReturnsInvalidValue_WhenServiceThrowsArgumentException`, `Handle_ForwardsSystemFallback_WhenCurrentUserNameIsNull`) to pass.

- [ ] **Step 5: Run the full backend build, the full test suite, and `dotnet format` to verify the whole refactor**
  ```bash
  cd backend
  dotnet format --verify-no-changes
  dotnet build Anela.Heblo.sln
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```
  Expect a clean build (zero errors/warnings introduced by this change) and the entire `Anela.Heblo.Tests` suite to pass, including `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (run implicitly as part of the full suite) — confirm it still passes unmodified; no changes to that file are made or required by this plan.

- [ ] **Step 6: Commit**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageHandler.cs \
          backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/DisassembleGiftPackageHandlerTests.cs
  git commit -m "$(cat <<'EOF'
  feat(gift-package-manufacture): resolve identity in DisassembleGiftPackageHandler

  Per ADR-005, identity resolution moves from GiftPackageManufactureService
  into the handler. DisassembleGiftPackageHandler now injects
  ICurrentUserService and forwards the resolved user name to the service,
  completing the GiftPackageManufactureService identity-resolution refactor.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BuEi7rZcmaqdArYLbFx8n1
  EOF
  )"
  ```
