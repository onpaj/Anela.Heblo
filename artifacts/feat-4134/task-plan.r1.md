# Implementation Plan: Remove Redundant UpdateAsync Calls in Purchase Order Handlers

**Goal:** Delete the redundant `await _repository.UpdateAsync(purchaseOrder, cancellationToken);` call from `UpdatePurchaseOrderStatusHandler` and `UpdatePurchaseOrderInvoiceAcquiredHandler` (both in `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/`), leaving `SaveChangesAsync` as the sole persistence call, and adjust the two handlers' existing Moq-based unit tests so they no longer assert the now-removed `UpdateAsync` call. This is a pure internal persistence-efficiency fix — no contract, DTO, controller, or behavior change. Covers spec requirements FR-1 (status handler), FR-2 (invoice-acquired handler), and FR-3 (test updates for both).

**Architecture summary:** .NET 8 Clean Architecture / Vertical Slice monorepo. MediatR command handlers live under `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/<UseCase>/<UseCase>Handler.cs`; each implements `IRequestHandler<TRequest, TResponse>`, loads an entity via `IPurchaseOrderRepository` (EF Core-backed `BaseRepository<TEntity, TKey>`, which tracks entities loaded via `GetByIdAsync`/`DbSet.FindAsync`), mutates it through a domain method, persists via `SaveChangesAsync`, and maps to a response DTO. Corresponding unit tests live under `backend/test/Anela.Heblo.Tests/Features/Purchase/<UseCase>HandlerTests.cs` using xUnit + Moq + FluentAssertions, mocking `IPurchaseOrderRepository`.

**Tech stack:** .NET 8, MediatR, EF Core, xUnit, Moq, FluentAssertions.

---

### task: remove-redundant-updateasync-in-status-handler

**Self-contained context:** This task removes one redundant line from `UpdatePurchaseOrderStatusHandler.Handle` and updates the one existing unit test whose assertion would otherwise fail as a result. `IPurchaseOrderRepository.GetByIdAsync` loads the `PurchaseOrder` entity through EF Core's `DbSet.FindAsync`, which attaches and tracks the entity (`EntityState.Unchanged`). The handler then mutates it via the domain method `purchaseOrder.ChangeStatus(newStatus, updatedBy)`. EF Core's change tracker automatically detects this in-memory mutation and will emit a minimal `UPDATE` (only the columns actually changed) when `SaveChangesAsync` runs — the explicit `_repository.UpdateAsync(purchaseOrder, cancellationToken)` call immediately before it is redundant and actually harmful: it calls `DbSet.Update(entity)` internally, which marks **every** scalar property as modified, producing an all-columns `UPDATE` instead of a minimal one. This call is being deleted; `SaveChangesAsync` remains, unchanged, as the sole persistence call.

#### Step 1 — Update the existing test assertion first

File: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs`

The test `Handle_ShouldCallRepositoryMethods` (currently lines 155–178) contains this block, which asserts the handler calls `UpdateAsync` exactly once — an assertion that must be removed because after this task's Step 3 the handler will no longer call `UpdateAsync` at all:

```csharp
        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(purchaseOrder, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldLogInformationMessages()
```

Apply this exact change (delete the `UpdateAsync` verification line only — everything else in the test, including its name, request/setup, and the `GetByIdAsync`/`SaveChangesAsync` verifications, stays as-is):

```csharp
        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldLogInformationMessages()
```

Leave the `_repositoryMock.Setup(x => x.UpdateAsync(...))` calls elsewhere in this file (e.g. inside `Handle_ShouldCallRepositoryMethods` itself, and in other tests such as `Handle_WithValidRequestAndDraftOrder_ShouldUpdateStatusToInTransit`) untouched — they become harmless no-op setups once the handler stops calling `UpdateAsync`, and removing them is out of scope for this surgical change.

#### Step 2 — Verify the test fails first (expected, since production code hasn't changed yet)

Run:

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderStatusHandlerTests.Handle_ShouldCallRepositoryMethods"
```

Expected result at this point: the test **passes** trivially (the handler still calls `UpdateAsync`, and the test file no longer asserts on it, so nothing contradicts). This is expected — per the spec, there are no new failing-first tests for this change; the adaptation in Step 1 is what keeps the suite green after Step 3 removes the production call. Do not expect a red bar here; proceed to Step 3.

#### Step 3 — Remove the redundant production code line

File: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusHandler.cs`

Current content of the `try` block (lines 44–65):

```csharp
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var updatedBy = currentUser.Name ?? "System";

            purchaseOrder.ChangeStatus(newStatus, updatedBy);

            await _repository.UpdateAsync(purchaseOrder, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} status updated to {Status}",
                purchaseOrder.OrderNumber, newStatus);

            return new UpdatePurchaseOrderStatusResponse
            {
                Id = purchaseOrder.Id,
                OrderNumber = purchaseOrder.OrderNumber,
                Status = purchaseOrder.Status.ToString(),
                UpdatedAt = purchaseOrder.UpdatedAt,
                UpdatedBy = purchaseOrder.UpdatedBy
            };
        }
```

Apply this exact change (delete only the `UpdateAsync` line):

```csharp
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var updatedBy = currentUser.Name ?? "System";

            purchaseOrder.ChangeStatus(newStatus, updatedBy);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} status updated to {Status}",
                purchaseOrder.OrderNumber, newStatus);

            return new UpdatePurchaseOrderStatusResponse
            {
                Id = purchaseOrder.Id,
                OrderNumber = purchaseOrder.OrderNumber,
                Status = purchaseOrder.Status.ToString(),
                UpdatedAt = purchaseOrder.UpdatedAt,
                UpdatedBy = purchaseOrder.UpdatedBy
            };
        }
```

No other line in this file changes. The rest of the handler (constructor/DI, the not-found branch at lines 31–35, the `Enum.TryParse` validation branch at lines 37–42, the `catch (InvalidOperationException ex)` block at lines 66–71) is untouched.

#### Step 4 — Verify all tests in this file pass

Run:

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderStatusHandlerTests"
```

Expected output: all tests in `UpdatePurchaseOrderStatusHandlerTests` pass (11 tests: `Handle_WithValidRequestAndDraftOrder_ShouldUpdateStatusToInTransit`, `Handle_WithValidRequestAndReceivedOrder_ShouldUpdateStatusToCompleted`, `Handle_WithNonExistentOrder_ShouldReturnError`, `Handle_WithInvalidStatus_ShouldReturnError`, `Handle_WithInvalidStatusTransition_ShouldReturnError`, `Handle_ShouldCallRepositoryMethods`, `Handle_ShouldLogInformationMessages`, `Handle_WhenOrderNotFound_ShouldLogWarning`, `Handle_WithInvalidStatus_ShouldLogWarning`, `Handle_WithInvalidTransition_ShouldLogWarning`, `Handle_TransitionFromInTransitToReceived_Succeeds`, `Handle_TransitionFromReceivedToCompleted_Succeeds`, `Handle_TransitionFromReceivedToDraft_ShouldReturnError`, `Handle_TransitionFromInTransitToCompleted_Succeeds` — 14 total), with output ending in a line such as `Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14`. No test in this file may reference `Times.Once` for `UpdateAsync` after this task.

#### Step 5 — Build the whole solution

Run:

```bash
cd backend
dotnet build
dotnet format --verify-no-changes
```

Expected: both commands exit 0 with no errors. If `dotnet format --verify-no-changes` reports formatting differences, run `dotnet format` (without `--verify-no-changes`) to fix, then re-verify.

#### Step 6 — Commit

Stage exactly the two files touched in this task:

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusHandler.cs backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs
git commit -m "$(cat <<'EOF'
fix: remove redundant UpdateAsync call in UpdatePurchaseOrderStatusHandler

The PurchaseOrder entity is already tracked via GetByIdAsync (DbSet.FindAsync),
so calling UpdateAsync (DbSet.Update) before SaveChangesAsync only downgrades
EF Core's per-property change tracking to a blanket all-columns UPDATE. Removing
it lets SaveChangesAsync emit a minimal UPDATE containing only the columns
ChangeStatus actually mutated, matching the pattern already used by
UpdatePurchaseOrderHandler.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01WEhargbLZsqsuwtiormovr
EOF
)"
```

**Verifiable check for this task:** `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderStatusHandlerTests"` passes with 0 failures; `UpdatePurchaseOrderStatusHandler.cs` no longer contains the string `UpdateAsync`; `dotnet build` succeeds.

---

### task: remove-redundant-updateasync-in-invoice-acquired-handler

**Self-contained context:** This task removes one redundant line from `UpdatePurchaseOrderInvoiceAcquiredHandler.Handle` and updates the two existing unit tests whose assertions/setups would otherwise fail or become meaningless as a result. Same EF Core mechanism as the status handler: `IPurchaseOrderRepository.GetByIdAsync` loads the `PurchaseOrder` entity through `DbSet.FindAsync`, which attaches and tracks it. The handler mutates it via `purchaseOrder.SetInvoiceAcquired(request.InvoiceAcquired, updatedBy)`. EF Core's change tracker detects this automatically; the explicit `_repository.UpdateAsync(purchaseOrder, cancellationToken)` call immediately before `SaveChangesAsync` is redundant and forces an all-columns `UPDATE`. This call is being deleted; `SaveChangesAsync` remains, unchanged, as the sole persistence call. One existing test (`Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError`) exercises the handler's generic `catch (Exception ex)` / `ErrorCodes.PurchaseOrderUpdateFailed` path by making the mocked `UpdateAsync` throw; since `UpdateAsync` will no longer be called, this test is rewritten to make the mocked `SaveChangesAsync` throw instead, keeping that exception path covered with the same exception type/message and the same expected error code.

#### Step 1 — Update the existing test assertions first

File: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderInvoiceAcquiredHandlerTests.cs`

**Edit 1 of 2** — `Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist` (currently lines 59–88) asserts `UpdateAsync` is called once. Current content:

```csharp
    [Fact]
    public async Task Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist()
    {
        var request = new UpdatePurchaseOrderInvoiceAcquiredRequest { Id = ValidOrderId, InvoiceAcquired = true };
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Id.Should().Be(purchaseOrder.Id);
        result.InvoiceAcquired.Should().BeTrue();

        purchaseOrder.InvoiceAcquired.Should().BeTrue();

        _repositoryMock.Verify(x => x.UpdateAsync(purchaseOrder, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

Apply this exact change (delete only the `UpdateAsync` verification line; leave the harmless `UpdateAsync` setup in place since it becomes a no-op):

```csharp
    [Fact]
    public async Task Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist()
    {
        var request = new UpdatePurchaseOrderInvoiceAcquiredRequest { Id = ValidOrderId, InvoiceAcquired = true };
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Id.Should().Be(purchaseOrder.Id);
        result.InvoiceAcquired.Should().BeTrue();

        purchaseOrder.InvoiceAcquired.Should().BeTrue();

        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

**Edit 2 of 2** — `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError` (currently lines 90–113) mocks `UpdateAsync` to throw, to exercise the handler's `catch (Exception ex)` block. Since `UpdateAsync` will no longer be called, this must be rewritten so `SaveChangesAsync` throws instead. Current content:

```csharp
    [Fact]
    public async Task Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError()
    {
        var request = new UpdatePurchaseOrderInvoiceAcquiredRequest { Id = ValidOrderId, InvoiceAcquired = true };
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PurchaseOrderUpdateFailed);
        result.Params.Should().ContainKey("OrderNumber");
        result.Params["OrderNumber"].Should().Be(ValidOrderNumber);
        result.Params.Should().ContainKey("Message");
        result.Params["Message"].Should().Be("db unavailable");
    }
```

Apply this exact change (rename the test and swap which mocked method throws; expected error code, message, and all other assertions stay identical):

```csharp
    [Fact]
    public async Task Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError()
    {
        var request = new UpdatePurchaseOrderInvoiceAcquiredRequest { Id = ValidOrderId, InvoiceAcquired = true };
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PurchaseOrderUpdateFailed);
        result.Params.Should().ContainKey("OrderNumber");
        result.Params["OrderNumber"].Should().Be(ValidOrderNumber);
        result.Params.Should().ContainKey("Message");
        result.Params["Message"].Should().Be("db unavailable");
    }
```

Note: the `UpdateAsync` throw-setup is intentionally removed (not left in place) in this specific test, because after Step 3 the handler no longer calls `UpdateAsync` at all — a stale `UpdateAsync` throw-setup would never fire and would be misleading dead setup. It is the `SaveChangesAsync` setup that must throw to reach the `catch` block. (This differs from Edit 1, where the passing-case `UpdateAsync` setup is harmless to leave in place since it is simply never invoked either way.)

`Handle_WithNonExistentOrder_ShouldReturnError` (lines 38–57) already asserts `Times.Never` for `UpdateAsync` — leave it completely unchanged.

#### Step 2 — Verify the affected tests still pass at this intermediate point (expected, production code not yet changed)

Run:

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderInvoiceAcquiredHandlerTests"
```

Expected: `Handle_WithNonExistentOrder_ShouldReturnError` and `Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist` pass (handler behavior unchanged so far). `Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError` (the rewritten test) should also **pass already** at this point, because the handler still calls `UpdateAsync` (a no-op against the mock, which has no setup and returns `Task.CompletedTask` by default for a `Task`-returning method) followed by `SaveChangesAsync`, which is now mocked to throw — so the `catch (Exception ex)` block is reached the same way it will be after Step 3. This is expected; the test rewrite anticipates the Step 3 change and does not require an interim red bar. Proceed to Step 3.

#### Step 3 — Remove the redundant production code line

File: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/UpdatePurchaseOrderInvoiceAcquiredHandler.cs`

Current content of the `try` block (lines 37–55):

```csharp
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var updatedBy = currentUser.Name ?? "System";

            purchaseOrder.SetInvoiceAcquired(request.InvoiceAcquired, updatedBy);

            await _repository.UpdateAsync(purchaseOrder, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} invoice acquired updated to {InvoiceAcquired}",
                purchaseOrder.OrderNumber, request.InvoiceAcquired);

            return new UpdatePurchaseOrderInvoiceAcquiredResponse
            {
                Id = purchaseOrder.Id,
                InvoiceAcquired = purchaseOrder.InvoiceAcquired
            };
        }
```

Apply this exact change (delete only the `UpdateAsync` line):

```csharp
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var updatedBy = currentUser.Name ?? "System";

            purchaseOrder.SetInvoiceAcquired(request.InvoiceAcquired, updatedBy);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} invoice acquired updated to {InvoiceAcquired}",
                purchaseOrder.OrderNumber, request.InvoiceAcquired);

            return new UpdatePurchaseOrderInvoiceAcquiredResponse
            {
                Id = purchaseOrder.Id,
                InvoiceAcquired = purchaseOrder.InvoiceAcquired
            };
        }
```

No other line in this file changes. The rest of the handler (constructor/DI, the not-found branch at lines 31–35, the generic `catch (Exception ex)` block at lines 56–61) is untouched.

#### Step 4 — Verify all tests in this file pass

Run:

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderInvoiceAcquiredHandlerTests"
```

Expected output: all 3 tests pass (`Handle_WithNonExistentOrder_ShouldReturnError`, `Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist`, `Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError`), with output ending in a line such as `Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3`. No test in this file may reference `UpdateAsync` being thrown-from or verified with `Times.Once` after this task.

#### Step 5 — Build the whole solution and run the full backend test suite

Run:

```bash
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: `dotnet build` and `dotnet format --verify-no-changes` exit 0 with no errors (if formatting differs, run `dotnet format` without the flag, then re-verify). `dotnet test` reports 0 failures for the full `Anela.Heblo.Tests` project (this also re-confirms Task 1's `UpdatePurchaseOrderStatusHandlerTests` still pass alongside this task's changes). This is the final validation for the whole feature — both handlers and all four affected tests are covered by this run.

#### Step 6 — Commit

Stage exactly the two files touched in this task:

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/UpdatePurchaseOrderInvoiceAcquiredHandler.cs backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderInvoiceAcquiredHandlerTests.cs
git commit -m "$(cat <<'EOF'
fix: remove redundant UpdateAsync call in UpdatePurchaseOrderInvoiceAcquiredHandler

Same rationale as UpdatePurchaseOrderStatusHandler: the PurchaseOrder entity is
already tracked via GetByIdAsync, so the explicit UpdateAsync call before
SaveChangesAsync only forces an all-columns UPDATE instead of letting EF Core's
change tracker emit a minimal one. Rewrote
Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError (renamed to
Handle_WhenSaveChangesThrows_ShouldReturnUpdateFailedError) to trigger the
handler's catch block via a failing SaveChangesAsync instead of UpdateAsync,
since UpdateAsync is no longer called.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01WEhargbLZsqsuwtiormovr
EOF
)"
```

**Verifiable check for this task:** `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderInvoiceAcquiredHandlerTests"` passes with 0 failures; `UpdatePurchaseOrderInvoiceAcquiredHandler.cs` no longer contains the string `UpdateAsync`; the full `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` run (covering both this task and Task 1) passes with 0 failures; `dotnet build` succeeds.

---

## Self-review against spec requirements

- **FR-1** (remove redundant `UpdateAsync` in `UpdatePurchaseOrderStatusHandler`, keep `SaveChangesAsync`, no other line changed, response/error codes/logs unchanged, `IPurchaseOrderRepository`/`BaseRepository` untouched) → covered by task `remove-redundant-updateasync-in-status-handler`, Step 3.
- **FR-2** (remove redundant `UpdateAsync` in `UpdatePurchaseOrderInvoiceAcquiredHandler`, keep `SaveChangesAsync`, no other line changed, response/error codes unchanged, the `Handle_WhenUpdateAsyncThrows...` test adapted to throw from `SaveChangesAsync`) → covered by task `remove-redundant-updateasync-in-invoice-acquired-handler`, Steps 1 (Edit 2) and 3.
- **FR-3** (update both test files so no test asserts `UpdateAsync` is called, all tests compile/pass, exception path in invoice-acquired handler stays covered via `SaveChangesAsync` failure, `dotnet build`/`dotnet test` succeed) → covered by task `remove-redundant-updateasync-in-status-handler` Step 1 (status test file) and task `remove-redundant-updateasync-in-invoice-acquired-handler` Step 1 (both edits in the invoice-acquired test file), with both tasks' Step 4/5 running the actual build/test verification.
- **NFR-1** (SQL shape only, no functional/behavioral change) → satisfied by design: only the two `UpdateAsync` lines are deleted; no other logic, validation, or response mapping is touched in either handler.
- **NFR-2** (no security-relevant change) → satisfied: no auth/permission/input/output changes in either task.
- **Out of Scope items** (no change to `BaseRepository.UpdateAsync`/`IRepository.UpdateAsync`, no change to `UpdatePurchaseOrderHandler`, no optimistic-concurrency/interceptor work, no sweep of other modules' handlers, no DB migrations, no E2E changes) → respected: neither task touches `BaseRepository.cs`, `IPurchaseOrderRepository.cs`, `UpdatePurchaseOrderHandler.cs`, any migration, or any file outside the four listed (two handlers, two test files).
