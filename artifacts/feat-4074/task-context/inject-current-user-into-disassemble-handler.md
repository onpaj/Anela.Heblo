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
