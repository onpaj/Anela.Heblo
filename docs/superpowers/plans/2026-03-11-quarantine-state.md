# Quarantine State Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `Karantena` location option with a dedicated `Quarantine` transport box state and expose a `Quarantine` stock metric in Catalog and Manufacturing views.

**Architecture:** Add `Quarantine` as a first-class `TransportBoxState` enum value with domain methods (`ToQuarantine`), extend `StockData` with a `Quarantine` property populated by `CatalogRepository`, and mirror Reserve UI patterns in the frontend for the new state.

**Tech Stack:** .NET 8, C# (xUnit + Moq + FluentAssertions), React + TypeScript (Jest + React Testing Library), OpenAPI TypeScript client (auto-generated on build).

---

## File Map

### Backend — files to modify
| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxState.cs` | Add `Quarantine` after `Reserve` |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxLocation.cs` | Remove `Karantena` |
| `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs` | Add `Quarantine` property; update `Total` |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs` | Add `ToQuarantine()`, `IsInQuarantine*`, update `Receive()`, `RevertToOpened()`, add `_transitions` entries |
| `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/TransportBoxDto.cs` | Add `IsInQuarantine` |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxById/GetTransportBoxByIdHandler.cs` | Add Czech label for `Quarantine` |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` | Add `HandleOpenToQuarantine`, add `CallBackMap` entry for `Quarantine→Received` |
| `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs` | Add `QuarantineLoadDate` property |
| `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` | Add `GetProductsInQuarantine`, update `RefreshReserveData`, add `CachedInQuarantineData`, `QuarantineLoadDate`, update `ChangesPendingForMerge`, update merge |

### Backend — test files to modify/create
| File | Change |
|------|--------|
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateTransitionTests.cs` | Add Quarantine transition tests |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` | Add Quarantine handler tests |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByIdHandlerTests.cs` | **Create** — new file for handler tests (file does not exist yet) |
| `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` | Add Quarantine stock tests |

### Frontend — files to modify
| File | Change |
|------|--------|
| `frontend/src/components/pages/LocationSelectionModal.tsx` | Remove `Karantena` from `LOCATIONS` |
| `frontend/src/components/transport/box-detail/TransportBoxTypes.tsx` | Add `Quarantine` to `stateLabels` and `stateColors` |
| `frontend/src/components/pages/TransportBoxDetail.tsx` | **No change needed** — Quarantine falls through to direct state change (only `"Reserve"` is intercepted to open modal, line 346) |
| `frontend/src/components/transport/box-detail/TransportBoxInfo.tsx` | **No change needed** — location field only shows when `state === "Reserve"` (already hidden for Quarantine); label comes from `stateLabels` in `TransportBoxTypes.tsx` |
| `frontend/src/components/pages/InventoryList.tsx` | Add quarantine column, click handler, sortable header |
| `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` | Add `Quarantine` to `ManufacturingStockSortBy` |
| `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` | Add quarantine column in table and sub-items |

### Frontend — API client
| Action | Detail |
|--------|--------|
| Regenerate TypeScript client | Run `npm run build` in `frontend/` after backend changes are complete |

---

## Chunk 1: Domain Layer

### Task 1: Add `Quarantine` to `TransportBoxState` enum

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxState.cs`

- [ ] **Step 1: Add the enum value**

  Open `TransportBoxState.cs`. Current content:
  ```csharp
  public enum TransportBoxState
  {
      New, Opened, InTransit, Received, InSwap, Stocked, Closed, Error, Reserve
  }
  ```
  Add `Quarantine` after `Reserve`:
  ```csharp
  public enum TransportBoxState
  {
      New,
      Opened,
      InTransit,
      Received,
      InSwap,
      Stocked,
      Closed,
      Error,
      Reserve,
      Quarantine
  }
  ```

- [ ] **Step 2: Build backend to confirm no errors**

  Run from `backend/`:
  ```bash
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxState.cs
  git commit -m "feat: add Quarantine to TransportBoxState enum"
  ```

---

### Task 2: Remove `Karantena` from `TransportBoxLocation` enum

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxLocation.cs`

- [ ] **Step 1: Remove `Karantena`**

  Current content:
  ```csharp
  public enum TransportBoxLocation { Kumbal, Relax, SkladSkla, Karantena }
  ```
  Replace with:
  ```csharp
  public enum TransportBoxLocation { Kumbal, Relax, SkladSkla }
  ```

- [ ] **Step 2: Build and run all backend tests**

  ```bash
  dotnet build && dotnet test
  ```
  Expected: All tests pass. If any test references `Karantena`, update it to a different location value.

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxLocation.cs
  git commit -m "feat: remove Karantena from TransportBoxLocation enum"
  ```

---

### Task 3: Add `Quarantine` to `StockData` and update `Total`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs`

- [ ] **Step 1: Add `Quarantine` property and update `Total`**

  Current:
  ```csharp
  public decimal Reserve { get; set; }
  // ...
  public decimal Total => Available + Reserve;
  ```
  After change:
  ```csharp
  public decimal Reserve { get; set; }
  public decimal Quarantine { get; set; }
  // ...
  public decimal Total => Available + Reserve + Quarantine;
  ```

- [ ] **Step 2: Build**

  ```bash
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs
  git commit -m "feat: add Quarantine property to StockData, include in Total"
  ```

---

### Task 4: Add Quarantine domain behaviour to `TransportBox`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs`
- Test: `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateTransitionTests.cs`

- [ ] **Step 1: Write failing tests**

  Open `TransportBoxStateTransitionTests.cs`. Add the following tests inside the existing test class:

  ```csharp
  [Fact]
  public void ToQuarantine_FromOpened_SetsStateToQuarantine()
  {
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");

      box.ToQuarantine(DateTime.UtcNow, "user");

      box.State.Should().Be(TransportBoxState.Quarantine);
  }

  [Fact]
  public void ToQuarantine_ClearsLocation()
  {
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");
      box.Location = "SkladSkla"; // stale value from a prior operation

      box.ToQuarantine(DateTime.UtcNow, "user");

      box.Location.Should().BeNull();
  }

  [Fact]
  public void ToQuarantine_FromNew_ThrowsValidationException()
  {
      var box = new TransportBox(); // State is New

      var act = () => box.ToQuarantine(DateTime.UtcNow, "user");

      act.Should().Throw<ValidationException>();
  }

  [Fact]
  public void IsInQuarantine_WhenQuarantine_ReturnsTrue()
  {
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");
      box.ToQuarantine(DateTime.UtcNow, "user");

      box.IsInQuarantine.Should().BeTrue();
  }

  [Fact]
  public void IsInQuarantine_WhenNotQuarantine_ReturnsFalse()
  {
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");

      box.IsInQuarantine.Should().BeFalse();
  }

  [Fact]
  public void Receive_FromQuarantine_Succeeds()
  {
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");
      box.ToQuarantine(DateTime.UtcNow, "user");

      box.Receive(DateTime.UtcNow, "user");

      box.State.Should().Be(TransportBoxState.Received);
  }

  [Fact]
  public void RevertToOpened_FromQuarantine_Succeeds()
  {
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");
      box.ToQuarantine(DateTime.UtcNow, "user");

      box.RevertToOpened(DateTime.UtcNow, "user");

      box.State.Should().Be(TransportBoxState.Opened);
  }

  [Fact]
  public void TransitionNode_FromQuarantine_HasReceivedAndOpenedTransitions()
  {
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");
      box.ToQuarantine(DateTime.UtcNow, "user");

      var transitions = box.TransitionNode.GetAllTransitions().Select(t => t.NewState).ToList();

      transitions.Should().Contain(TransportBoxState.Received);
      transitions.Should().Contain(TransportBoxState.Opened);
  }

  [Fact]
  public void TransitionNode_FromOpened_HasQuarantineTransition()
  {
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");

      var transitions = box.TransitionNode.GetAllTransitions().Select(t => t.NewState).ToList();

      transitions.Should().Contain(TransportBoxState.Quarantine);
  }
  ```

- [ ] **Step 2: Run tests to confirm they fail**

  ```bash
  dotnet test --filter "TransportBoxStateTransitionTests"
  ```
  Expected: FAIL — `ToQuarantine` method does not exist yet.

- [ ] **Step 3: Implement changes in `TransportBox.cs`**

  **3a. Add `IsInQuarantine` predicate and property** (after the `IsInReserve` block):
  ```csharp
  public static Expression<Func<TransportBox, bool>> IsInQuarantinePredicate =
      b => b.State == TransportBoxState.Quarantine;
  public static Func<TransportBox, bool> IsInQuarantineFunc = IsInQuarantinePredicate.Compile();
  public bool IsInQuarantine => IsInQuarantineFunc(this);
  ```

  **3b. Add `ToQuarantine()` method** (after `ToReserve()`):
  ```csharp
  public void ToQuarantine(DateTime date, string userName)
  {
      Location = null;
      ChangeState(TransportBoxState.Quarantine, date, userName, TransportBoxState.Opened);
  }
  ```

  **3c. Update `Receive()`** — add `TransportBoxState.Quarantine` as an allowed source:
  ```csharp
  public void Receive(DateTime date, string userName, TransportBoxState receiveState = TransportBoxState.Stocked)
  {
      Location = null;
      DefaultReceiveState = receiveState;
      ChangeState(TransportBoxState.Received, date, userName,
          TransportBoxState.InTransit, TransportBoxState.Reserve, TransportBoxState.Quarantine);
  }
  ```

  **3d. Update `RevertToOpened()`** — allow Quarantine as a source:
  ```csharp
  public void RevertToOpened(DateTime date, string userName)
  {
      if (State != TransportBoxState.InTransit
          && State != TransportBoxState.Reserve
          && State != TransportBoxState.Quarantine)
      {
          throw new ValidationException(
              $"Cannot revert to Opened from {State} state. Only InTransit, Reserve and Quarantine states can be reverted.");
      }

      if (string.IsNullOrEmpty(Code))
      {
          throw new ValidationException("Cannot revert to Opened: Box code is required");
      }

      Location = null;
      ChangeState(TransportBoxState.Opened, date, userName,
          TransportBoxState.InTransit, TransportBoxState.Reserve, TransportBoxState.Quarantine);
  }
  ```

  **3e. Update the static constructor** — add Quarantine node and `Opened → Quarantine` transition.

  In the `openedNode` block, add after the existing `Reserve` transition:
  ```csharp
  openedNode.AddTransition(new TransportBoxTransition(
      TransportBoxState.Quarantine, TransitionType.Next,
      (box, time, userName) => box.ToQuarantine(time, userName)));
  ```

  After the `_transitions.Add(TransportBoxState.Reserve, reserveNode);` line, add:
  ```csharp
  // Quarantine → Received, Opened (revert)
  var quarantineNode = new TransportBoxStateNode();
  quarantineNode.AddTransition(new TransportBoxTransition(
      TransportBoxState.Received, TransitionType.Next,
      (box, time, userName) => box.Receive(time, userName)));
  quarantineNode.AddTransition(new TransportBoxTransition(
      TransportBoxState.Opened, TransitionType.Previous,
      (box, time, userName) => box.RevertToOpened(time, userName)));
  _transitions.Add(TransportBoxState.Quarantine, quarantineNode);
  ```

- [ ] **Step 4: Run tests to confirm they pass**

  ```bash
  dotnet test --filter "TransportBoxStateTransitionTests"
  ```
  Expected: All new tests PASS.

- [ ] **Step 5: Run full backend test suite**

  ```bash
  dotnet test
  ```
  Expected: All tests pass.

- [ ] **Step 6: Run dotnet format**

  ```bash
  dotnet format backend/
  ```
  Expected: No changes (code is already formatted), or only whitespace fixes.

- [ ] **Step 7: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs \
          backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateTransitionTests.cs
  git commit -m "feat: add Quarantine state to TransportBox domain — ToQuarantine, transitions, IsInQuarantine"
  ```

---

## Chunk 2: Application Layer

### Task 5: Add `IsInQuarantine` to `TransportBoxDto` and `GetTransportBoxByIdHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/TransportBoxDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxById/GetTransportBoxByIdHandler.cs`
- **Create**: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByIdHandlerTests.cs`

- [ ] **Step 1: Create test file with scaffolding and failing tests**

  Create `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByIdHandlerTests.cs`:

  ```csharp
  using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
  using Anela.Heblo.Domain.Features.Logistics.Transport;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Logistics.Transport;

  public class GetTransportBoxByIdHandlerTests
  {
      private readonly Mock<ITransportBoxRepository> _repositoryMock;
      private readonly Mock<ILogger<GetTransportBoxByIdHandler>> _loggerMock;
      private readonly GetTransportBoxByIdHandler _handler;

      public GetTransportBoxByIdHandlerTests()
      {
          _repositoryMock = new Mock<ITransportBoxRepository>();
          _loggerMock = new Mock<ILogger<GetTransportBoxByIdHandler>>();
          _handler = new GetTransportBoxByIdHandler(_loggerMock.Object, _repositoryMock.Object);
      }

      [Fact]
      public async Task Handle_QuarantineBox_SetsIsInQuarantineTrue()
      {
          // Arrange — use public API to get box into Quarantine state
          var box = new TransportBox();
          box.Open("B001", DateTime.UtcNow, "user");
          box.ToQuarantine(DateTime.UtcNow, "user");

          _repositoryMock
              .Setup(x => x.GetByIdWithDetailsAsync(1))
              .ReturnsAsync(box);

          // Act
          var result = await _handler.Handle(
              new GetTransportBoxByIdRequest { Id = 1 }, CancellationToken.None);

          // Assert
          result.TransportBox.Should().NotBeNull();
          result.TransportBox!.IsInQuarantine.Should().BeTrue();
          result.TransportBox.IsInReserve.Should().BeFalse();
      }

      [Fact]
      public async Task Handle_QuarantineBox_AllowedTransitionsIncludeVKaranteneLabel()
      {
          // Arrange
          var box = new TransportBox();
          box.Open("B001", DateTime.UtcNow, "user");
          // The Quarantine transition is available from Opened state (before calling ToQuarantine)
          // Test the label by checking the allowed transitions on an Opened box

          _repositoryMock
              .Setup(x => x.GetByIdWithDetailsAsync(1))
              .ReturnsAsync(box);

          // Act
          var result = await _handler.Handle(
              new GetTransportBoxByIdRequest { Id = 1 }, CancellationToken.None);

          // Assert — Quarantine transition should have Czech label
          var quarantineTransition = result.TransportBox!.AllowedTransitions
              .FirstOrDefault(t => t.NewState == "Quarantine");
          quarantineTransition.Should().NotBeNull();
          quarantineTransition!.Label.Should().Be("V karanténě");
      }
  }
  ```

- [ ] **Step 2: Run tests to confirm they fail**

  ```bash
  dotnet test --filter "GetTransportBoxByIdHandlerTests"
  ```
  Expected: FAIL — `ToQuarantine` exists but `IsInQuarantine` not yet in DTO; `"V karanténě"` label not yet in handler.

- [ ] **Step 3: Add `IsInQuarantine` property to `TransportBoxDto`**

  After the `IsInReserve` line:
  ```csharp
  public bool IsInReserve { get; set; }
  public bool IsInQuarantine { get; set; }
  ```

- [ ] **Step 4: Populate `IsInQuarantine` and add Czech label in `GetTransportBoxByIdHandler`**

  In the `dto` initializer inside `Handle()`, after `IsInReserve = transportBox.IsInReserve,`:
  ```csharp
  IsInQuarantine = transportBox.IsInQuarantine,
  ```

  In `GetStateLabel()`, after the `Reserve` case:
  ```csharp
  TransportBoxState.Reserve => "V rezervě",
  TransportBoxState.Quarantine => "V karanténě",
  ```

- [ ] **Step 5: Run tests to confirm they pass**

  ```bash
  dotnet test --filter "GetTransportBoxByIdHandlerTests"
  ```
  Expected: All new tests PASS.

- [ ] **Step 6: Run full test suite and format**

  ```bash
  dotnet test && dotnet format backend/
  ```

- [ ] **Step 7: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/TransportBoxDto.cs \
          backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxById/GetTransportBoxByIdHandler.cs \
          backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByIdHandlerTests.cs
  git commit -m "feat: add IsInQuarantine to TransportBoxDto and Quarantine Czech label"
  ```

---

### Task 6: Add Quarantine handling to `ChangeTransportBoxStateHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

  Open `ChangeTransportBoxStateHandlerTests.cs`. Add:

  ```csharp
  [Fact]
  public async Task Handle_OpenedToQuarantine_NoLocationRequired_ReturnsSuccess()
  {
      // Arrange — box in Opened state
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");
      _repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);
      _mediatorMock
          .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GetTransportBoxByIdResponse { TransportBox = new TransportBoxDto() });

      var request = new ChangeTransportBoxStateRequest
      {
          BoxId = 1,
          NewState = TransportBoxState.Quarantine
          // Location intentionally omitted
      };

      // Act
      var result = await _handler.Handle(request, CancellationToken.None);

      // Assert
      result.Success.Should().BeTrue();
  }

  [Fact]
  public async Task Handle_OpenedToQuarantine_DoesNotCreateStockUpOperations()
  {
      // Arrange
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");
      box.AddItem("PROD001", "Test Product", 10, DateTime.UtcNow, "user");
      _repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);
      _mediatorMock
          .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GetTransportBoxByIdResponse { TransportBox = new TransportBoxDto() });

      var request = new ChangeTransportBoxStateRequest
      {
          BoxId = 1,
          NewState = TransportBoxState.Quarantine
      };

      // Act
      await _handler.Handle(request, CancellationToken.None);

      // Assert — no stock operations created (that only happens on Received)
      _stockUpProcessingServiceMock.Verify(
          x => x.CreateOperationAsync(
              It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
              It.IsAny<StockUpSourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.Never);
  }

  [Fact]
  public async Task Handle_QuarantineToReceived_CreatesStockUpOperations()
  {
      // Arrange — box in Quarantine state (use reflection)
      var box = new TransportBox();
      box.Open("B001", DateTime.UtcNow, "user");
      box.ToQuarantine(DateTime.UtcNow, "user");
      // Add items via reflection
      var itemsField = typeof(TransportBox).GetField("_items",
          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
      var items = (List<TransportBoxItem>)itemsField.GetValue(box)!;
      var item = (TransportBoxItem)Activator.CreateInstance(
          typeof(TransportBoxItem), "PROD001", "Test", 5.0, DateTime.UtcNow, "user")!;
      items.Add(item);

      _repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);
      _mediatorMock
          .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GetTransportBoxByIdResponse { TransportBox = new TransportBoxDto() });

      var request = new ChangeTransportBoxStateRequest
      {
          BoxId = 1,
          NewState = TransportBoxState.Received
      };

      // Act
      var result = await _handler.Handle(request, CancellationToken.None);

      // Assert
      result.Success.Should().BeTrue();
      _stockUpProcessingServiceMock.Verify(
          x => x.CreateOperationAsync(
              It.IsAny<string>(), "PROD001", 5,
              StockUpSourceType.TransportBox, It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.Once);
  }
  ```

- [ ] **Step 2: Run tests to confirm they fail**

  ```bash
  dotnet test --filter "ChangeTransportBoxStateHandlerTests"
  ```
  Expected: FAIL — `HandleOpenToQuarantine` not yet added; Quarantine→Received not in `CallBackMap`.

- [ ] **Step 3: Implement changes in handler**

  **3a. Add `HandleOpenToQuarantine` to `CallBackMap`** and the `Quarantine→Received` entry.

  In `CallBackMap`, after the `Opened→Reserve` entry:
  ```csharp
  { new Tuple<TransportBoxState, TransportBoxState>(TransportBoxState.Opened, TransportBoxState.Quarantine), h => h.HandleOpenToQuarantine },
  { new Tuple<TransportBoxState, TransportBoxState>(TransportBoxState.Quarantine, TransportBoxState.Received), h => h.HandleReceived },
  ```

  **3b. Add `HandleOpenToQuarantine` method** (after `HandleOpenToReserve`):
  ```csharp
  private async Task<ChangeTransportBoxStateResponse?> HandleOpenToQuarantine(
      TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
  {
      // No location required for Quarantine — ToQuarantine() clears Location = null
      await Task.CompletedTask;
      return null;
  }
  ```

  > **Note on `async`:** Match the signature of `HandleOpenToReserve` which is `async`. The `await Task.CompletedTask` keeps the compiler happy. Alternatively, omit `async` and return `Task.FromResult<ChangeTransportBoxStateResponse?>(null)` — either style is fine.

- [ ] **Step 4: Run tests to confirm they pass**

  ```bash
  dotnet test --filter "ChangeTransportBoxStateHandlerTests"
  ```
  Expected: All new tests PASS.

- [ ] **Step 5: Run full test suite and format**

  ```bash
  dotnet test && dotnet format backend/
  ```

- [ ] **Step 6: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs \
          backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs
  git commit -m "feat: handle Opened→Quarantine and Quarantine→Received in ChangeTransportBoxStateHandler"
  ```

---

### Task 7: Expose `QuarantineLoadDate` in `ICatalogRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs`

- [ ] **Step 1: Add `QuarantineLoadDate` to the interface**

  After `DateTime? ReserveLoadDate { get; }`:
  ```csharp
  DateTime? ReserveLoadDate { get; }
  DateTime? QuarantineLoadDate { get; }
  ```

- [ ] **Step 2: Build (will fail until CatalogRepository implements it)**

  ```bash
  dotnet build
  ```
  Expected: Build error — `CatalogRepository` does not implement `QuarantineLoadDate`. Proceed to Task 8 immediately.

---

### Task 8: Add quarantine data to `CatalogRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`
- Test: `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

  Open `CatalogRepositoryTests.cs`. Add:

  ```csharp
  [Fact]
  public async Task RefreshReserveData_WithQuarantineBoxes_PopulatesQuarantineStock()
  {
      // Arrange
      SetupEmptyMocks();

      // Create a transport box in Quarantine state with an item
      var quarantineBox = new TransportBox();
      quarantineBox.Open("B001", DateTime.UtcNow, "user");
      quarantineBox.ToQuarantine(DateTime.UtcNow, "user");
      var item = (TransportBoxItem)Activator.CreateInstance(
          typeof(TransportBoxItem), "TEST001", "Test Product", 15.0, DateTime.UtcNow, "user")!;
      var itemsField = typeof(TransportBox).GetField("_items",
          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
      ((List<TransportBoxItem>)itemsField.GetValue(quarantineBox)!).Add(item);

      _transportBoxRepositoryMock
          .Setup(x => x.FindAsync(
              It.Is<Expression<Func<TransportBox, bool>>>(expr =>
                  // matches IsInQuarantinePredicate — boxes in Quarantine state
                  expr.Compile()(quarantineBox) && !expr.Compile()(new TransportBox())),
              It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TransportBox> { quarantineBox });

      // Also stub the Reserve call to return empty
      _transportBoxRepositoryMock
          .Setup(x => x.FindAsync(
              It.Is<Expression<Func<TransportBox, bool>>>(expr =>
                  !expr.Compile()(quarantineBox)),
              It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TransportBox>());

      // Act
      await _repository.RefreshReserveData(CancellationToken.None);
      var products = await _repository.GetAllAsync(CancellationToken.None);

      // Assert
      var product = products.FirstOrDefault(p => p.ProductCode == "TEST001");
      product.Should().NotBeNull();
      product!.Stock.Quarantine.Should().Be(15);
  }

  [Fact]
  public async Task QuarantineLoadDate_IsNullBeforeRefresh_AndSetAfterRefresh()
  {
      // Arrange
      SetupEmptyMocks();
      _repository.QuarantineLoadDate.Should().BeNull(); // before any refresh

      // Act
      await _repository.RefreshReserveData(CancellationToken.None);

      // Assert
      _repository.QuarantineLoadDate.Should().NotBeNull();
  }
  ```

  > **Note on `FindAsync` mock matching:** Moq cannot directly match `Expression<Func<T,bool>>` by value. Use `It.IsAny<Expression<Func<TransportBox, bool>>>()` for all `FindAsync` setups in this test class, then verify results by checking the product's `Stock.Quarantine` value. If multiple `FindAsync` calls exist (one for Reserve, one for Quarantine), use a `SetupSequence` or separate mocks for each predicate — or return a combined collection and let the predicates filter:
  >
  > ```csharp
  > // Simplest approach: stub FindAsync to return boxes based on call order
  > _transportBoxRepositoryMock
  >     .SetupSequence(x => x.FindAsync(
  >         It.IsAny<Expression<Func<TransportBox, bool>>>(),
  >         It.IsAny<bool>(), It.IsAny<CancellationToken>()))
  >     .ReturnsAsync(new List<TransportBox>())          // 1st call: GetProductsInReserve → empty
  >     .ReturnsAsync(new List<TransportBox> { quarantineBox }); // 2nd call: GetProductsInQuarantine
  > ```

- [ ] **Step 2: Run tests to confirm they fail**

  ```bash
  dotnet test --filter "CatalogRepositoryTests"
  ```
  Expected: FAIL — `QuarantineLoadDate` doesn't exist yet (build error).

- [ ] **Step 3: Implement changes in `CatalogRepository`**

  **3a. Add `CachedInQuarantineData` property** (after `CachedInReserveData`, same pattern):
  ```csharp
  private IDictionary<string, int> CachedInQuarantineData
  {
      get => _cache.Get<Dictionary<string, int>>(nameof(CachedInQuarantineData)) ?? new Dictionary<string, int>();
      set
      {
          _cache.Set(nameof(CachedInQuarantineData), value);
          InvalidateSourceData(nameof(CachedInQuarantineData));
          SetLoadDateInCache(nameof(CachedInQuarantineData));
      }
  }
  ```

  **3b. Add `QuarantineLoadDate` property** (after `ReserveLoadDate`):
  ```csharp
  public DateTime? QuarantineLoadDate => GetLoadDateFromCache(nameof(CachedInQuarantineData));
  ```

  **3c. Add `GetProductsInQuarantine()` private method** (after `GetProductsInReserve()`):
  ```csharp
  private async Task<Dictionary<string, int>> GetProductsInQuarantine(CancellationToken ct)
  {
      var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInQuarantinePredicate, includeDetails: true, cancellationToken: ct);
      return boxes.SelectMany(s => s.Items)
          .GroupBy(g => g.ProductCode)
          .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
  }
  ```

  **3d. Update `RefreshReserveData()`** — call quarantine refresh inside it:
  ```csharp
  public async Task RefreshReserveData(CancellationToken ct)
  {
      var reserveData = await GetProductsInReserve(ct);
      CachedInReserveData = reserveData;

      var quarantineData = await GetProductsInQuarantine(ct);
      CachedInQuarantineData = quarantineData;
  }
  ```

  **3e. Update `ChangesPendingForMerge`** — add `QuarantineLoadDate` to the `loadDates` array.

  Find the `loadDates` array inside `ChangesPendingForMerge`. It currently includes `ReserveLoadDate`. Add `QuarantineLoadDate`:
  ```csharp
  var loadDates = new DateTime?[]
  {
      TransportLoadDate,
      ReserveLoadDate,
      QuarantineLoadDate,   // <-- add this line
      OrderedLoadDate,
      // ... rest of existing entries ...
  };
  ```

  **3f. Update the merge process** — set `product.Stock.Quarantine` per product.

  Find the block where `product.Stock.Reserve` is set (around `product.Stock.Reserve = CachedInReserveData...`). Add the quarantine assignment immediately after:
  ```csharp
  product.Stock.Reserve = CachedInReserveData.ContainsKey(product.ProductCode)
      ? CachedInReserveData[product.ProductCode] : 0;
  product.Stock.Quarantine = CachedInQuarantineData.ContainsKey(product.ProductCode)
      ? CachedInQuarantineData[product.ProductCode] : 0;
  ```

- [ ] **Step 4: Build to confirm interface is satisfied**

  ```bash
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run catalog repository tests**

  ```bash
  dotnet test --filter "CatalogRepositoryTests"
  ```
  Expected: Tests pass (adjust mock setup if needed based on `FindAsync` matching).

- [ ] **Step 6: Run full test suite and format**

  ```bash
  dotnet test && dotnet format backend/
  ```

- [ ] **Step 7: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs \
          backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs \
          backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs
  git commit -m "feat: add Quarantine stock data to CatalogRepository — GetProductsInQuarantine, QuarantineLoadDate, merge"
  ```

---

## Chunk 3: Frontend Layer

### Task 9: Remove `Karantena` from `LocationSelectionModal`

**Files:**
- Modify: `frontend/src/components/pages/LocationSelectionModal.tsx`

- [ ] **Step 1: Remove `Karantena` from `LOCATIONS` array**

  Current:
  ```typescript
  export const LOCATIONS = [
    { value: "Kumbal", label: "Kumbal" },
    { value: "Relax", label: "Relax" },
    { value: "SkladSkla", label: "Sklad Skla" },
    { value: "Karantena", label: "Karantena" },
  ];
  ```
  Replace with:
  ```typescript
  export const LOCATIONS = [
    { value: "Kumbal", label: "Kumbal" },
    { value: "Relax", label: "Relax" },
    { value: "SkladSkla", label: "Sklad Skla" },
  ];
  ```

- [ ] **Step 2: Run frontend tests**

  From `frontend/`:
  ```bash
  npm test -- --watchAll=false
  ```
  Expected: All tests pass. If any test used `Karantena` as a valid location value, update it to `Kumbal`.

- [ ] **Step 3: Commit**

  ```bash
  git add frontend/src/components/pages/LocationSelectionModal.tsx
  git commit -m "feat: remove Karantena from location selection modal"
  ```

---

### Task 10: Add Quarantine state labels and colors

**Files:**
- Modify: `frontend/src/components/transport/box-detail/TransportBoxTypes.tsx`

- [ ] **Step 1: Add `Quarantine` to `stateLabels`**

  After `Reserve: "V rezervě",`:
  ```typescript
  Reserve: "V rezervě",
  Quarantine: "V karanténě",
  ```

- [ ] **Step 2: Add `Quarantine` to `stateColors`**

  After `Reserve: "bg-indigo-100 text-indigo-800",`:
  ```typescript
  Reserve: "bg-indigo-100 text-indigo-800",
  Quarantine: "bg-orange-100 text-orange-800",
  ```

  > **Color choice:** Orange conveys a warning/hold state, distinguishing Quarantine visually from Reserve (indigo) and Error (red).

- [ ] **Step 3: Run frontend tests**

  ```bash
  npm test -- --watchAll=false
  ```
  Expected: All tests pass.

- [ ] **Step 4: Commit**

  ```bash
  git add frontend/src/components/transport/box-detail/TransportBoxTypes.tsx
  git commit -m "feat: add Quarantine state label (V karanténě) and orange color"
  ```

---

### Task 11: Regenerate TypeScript API client

The backend now has `StockData.Quarantine` and `TransportBoxState.Quarantine`. The frontend TypeScript client must be regenerated to pick these up.

**Files:**
- Regenerated: `frontend/src/api/generated/api-client.ts` (auto-generated, do not edit manually)

- [ ] **Step 1: Build the frontend (triggers OpenAPI client generation)**

  From `frontend/`:
  ```bash
  npm run build
  ```
  Expected: Build succeeds. The generated `api-client.ts` now includes `quarantine?: number` in `StockData` and `Quarantine = "Quarantine"` in `TransportBoxState`.

- [ ] **Step 2: Verify the generated client**

  Check that `api-client.ts` contains the new types:
  ```bash
  grep -n "Quarantine" frontend/src/api/generated/api-client.ts
  ```
  Expected: Lines referencing `Quarantine` in the enum and `quarantine` in stock data.

- [ ] **Step 3: Run frontend tests**

  ```bash
  npm test -- --watchAll=false
  ```
  Expected: All tests pass.

- [ ] **Step 4: Commit**

  ```bash
  git add frontend/src/api/generated/api-client.ts
  git commit -m "feat: regenerate TypeScript API client with Quarantine state and stock data"
  ```

---

### Task 12: Add Quarantine column to `InventoryList`

**Files:**
- Modify: `frontend/src/components/pages/InventoryList.tsx`

**Context:** The existing Reserve column is a sortable column with a badge that navigates to transport boxes filtered by `state=Reserve`. The Quarantine column follows the same pattern but uses `state=Quarantine`. The spec's "filter option" is satisfied by the `SortableHeader` (clicking the header sorts by quarantine quantity, and clicking the badge navigates to filtered transport boxes).

- [ ] **Step 1: Add `handleQuarantineClick` handler**

  Find `handleReserveClick` (around line 236). Add an identical handler below it:
  ```typescript
  const handleQuarantineClick = (event: React.MouseEvent, productCode: string | undefined) => {
    event.stopPropagation();
    if (!productCode) return;
    navigate(`/logistics/transport-boxes?productCode=${encodeURIComponent(productCode)}&state=Quarantine`);
  };
  ```

- [ ] **Step 2: Add quarantine calculation**

  Find where `const reserve = Math.round(...)` is calculated (around line 444). Add below it:
  ```typescript
  const quarantine = Math.round((item.stock?.quarantine || 0) * 100) / 100;
  ```

- [ ] **Step 3: Add column header**

  Find the `<SortableHeader column="reserve">Rezerva</SortableHeader>` header. Add a Quarantine header immediately after:
  ```tsx
  <SortableHeader column="quarantine">Karantena</SortableHeader>
  ```

- [ ] **Step 4: Add column data cell**

  Find the Reserve `<td>` block (around line 511). Add an identical Quarantine cell immediately after it:
  ```tsx
  <td className="px-6 py-5 whitespace-nowrap text-center">
    {quarantine > 0 ? (
      <span
        className="inline-flex items-center px-4 py-2 rounded-full text-base font-semibold bg-orange-100 text-orange-800 justify-center inventory-badge hover:bg-orange-200 hover:text-orange-900"
        onClick={(e) => handleQuarantineClick(e, item.productCode)}
        title="Klikněte pro zobrazení karantény"
      >
        {quarantine}
      </span>
    ) : (
      <span className="text-gray-400 text-base">-</span>
    )}
  </td>
  ```

- [ ] **Step 5: Run frontend tests**

  ```bash
  npm test -- --watchAll=false
  ```
  Expected: All tests pass.

- [ ] **Step 6: Commit**

  ```bash
  git add frontend/src/components/pages/InventoryList.tsx
  git commit -m "feat: add Quarantine quantity column to InventoryList"
  ```

---

### Task 13: Add Quarantine column to `ManufacturingStockAnalysis`

**Files:**
- Modify: `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`
- Modify: `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`

**Context:** The Reserve column appears in three places in ManufacturingStockAnalysis: the column header, the main item rows, and the sub-item (group) rows. Each place needs a parallel Quarantine entry.

- [ ] **Step 1: Add `Quarantine` to `ManufacturingStockSortBy` enum**

  Open `useManufacturingStockAnalysis.ts`. After `Reserve = "Reserve",`:
  ```typescript
  Reserve = "Reserve",
  Quarantine = "Quarantine",
  ```

- [ ] **Step 2: Add quarantine column header in `ManufacturingStockAnalysis.tsx`**

  Find the Reserve `<SortableHeader>` block (around line 1149):
  ```tsx
  <SortableHeader
    column={ManufacturingStockSortBy.Reserve}
    className="text-right"
    style={{ minWidth: "60px", width: "10%" }}
  >
    Rezerv
  </SortableHeader>
  ```
  Add an identical block immediately after, using `Quarantine`:
  ```tsx
  <SortableHeader
    column={ManufacturingStockSortBy.Quarantine}
    className="text-right"
    style={{ minWidth: "60px", width: "10%" }}
  >
    Karantén
  </SortableHeader>
  ```

- [ ] **Step 3: Add quarantine cell for main item rows**

  Find the Reserve stock `<td>` for main items (around line 1324). Add an identical Quarantine cell immediately after:
  ```tsx
  {/* Quarantine Stock */}
  <td
    className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900"
    style={{ minWidth: "60px", width: "10%" }}
  >
    {(item.quarantine || 0) > 0 ? (
      <div className="font-bold">
        {formatNumber(item.quarantine, 0)}
      </div>
    ) : (
      <span className="text-gray-400">—</span>
    )}
  </td>
  ```

- [ ] **Step 4: Add quarantine cell for sub-item (group) rows**

  Find the Reserve stock `<td>` for sub-items (around line 453). Add an identical Quarantine cell immediately after:
  ```tsx
  {/* Quarantine Stock */}
  <td
    className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700"
    style={{ minWidth: "90px", width: "10%" }}
  >
    {(subItem.quarantine || 0) > 0 ? (
      <div className="font-medium">
        {formatNumber(subItem.quarantine, 0)}
      </div>
    ) : (
      <span className="text-gray-400">—</span>
    )}
  </td>
  ```

- [ ] **Step 5: Run frontend tests**

  ```bash
  npm test -- --watchAll=false
  ```
  Expected: All tests pass.

- [ ] **Step 6: Run frontend build to verify no TypeScript errors**

  ```bash
  npm run build
  ```
  Expected: Build succeeded, 0 TypeScript errors.

- [ ] **Step 7: Commit**

  ```bash
  git add frontend/src/api/hooks/useManufacturingStockAnalysis.ts \
          frontend/src/components/pages/ManufacturingStockAnalysis.tsx
  git commit -m "feat: add Quarantine column to ManufacturingStockAnalysis"
  ```

---

## Final Verification

- [ ] **Run full backend test suite**

  ```bash
  dotnet test
  ```
  Expected: All tests pass.

- [ ] **Run dotnet format (no violations)**

  ```bash
  dotnet format backend/ --verify-no-changes
  ```
  Expected: Exit code 0 (no formatting violations).

- [ ] **Run full frontend test suite**

  ```bash
  npm test -- --watchAll=false
  ```
  Expected: All tests pass.

- [ ] **Run frontend build (no TypeScript errors)**

  ```bash
  npm run build
  ```
  Expected: Build succeeded.

- [ ] **Verify success criteria from spec**
  - [ ] Transport boxes can transition to Quarantine from Opened without selecting a location
  - [ ] Quarantine boxes can transition to Received or back to Opened
  - [ ] `StockData.Quarantine` is populated from boxes in Quarantine state
  - [ ] Quarantine stock metric visible in Catalog (InventoryList) and Manufacturing Stock Analysis views
  - [ ] Karantena is no longer selectable as a location in the UI
  - [ ] Existing Reserve+Karantena boxes are unaffected (no migration needed)
