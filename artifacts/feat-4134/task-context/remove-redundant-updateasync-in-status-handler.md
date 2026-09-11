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
