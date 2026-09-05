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

