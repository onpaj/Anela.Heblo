# Task Plan: CancellationToken Consistency for IBankStatementImportRepository

## Overview
`IBankStatementImportRepository.GetByIdAsync`, `AddAsync`, and `UpdateAsync` are the only three methods on the interface that don't accept a `CancellationToken`, even though EF Core's underlying `FindAsync`/`SaveChangesAsync` calls support cancellation and every other method on this interface already threads a token through. This plan adds `CancellationToken cancellationToken = default` to all three methods on the interface and its EF Core implementation, then wires the token through `ImportBankStatementHandler` and `GetBankStatementByIdHandler` so MediatR's pipeline-supplied token actually reaches the database calls. It also updates the Moq-based unit tests whose single-argument `Setup`/`Verify` expressions would otherwise silently stop matching once production code starts calling the two-argument overloads.

This is a single, tightly-coupled backend plumbing change: the interface (Domain), its implementation (Persistence), and both call sites (Application) must compile together, and the test doubles must be updated in the same change so the suite reflects real invocation shapes rather than reporting false negatives. It is implemented as **one task** — splitting the interface change from the implementation/call-site changes would leave the solution in a non-compiling intermediate state, and splitting test updates out would leave `Times.Once`/`Times.Never` assertions silently invalid. Everything lands in one commit.

No new files, no schema changes, no new dependencies. Files touched:
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`

`backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` is verified to compile unchanged (integration-style test calling the real repository positionally); no edits are made there per the spec's explicit Out-of-Scope section.

---

### task: add-cancellationtoken-to-bank-repository-and-call-sites

**Goal**
Add `CancellationToken cancellationToken = default` to `IBankStatementImportRepository.GetByIdAsync`/`AddAsync`/`UpdateAsync`, update the EF Core implementation to pass the token into `FindAsync`/`SaveChangesAsync`, thread the token through `ImportBankStatementHandler` and `GetBankStatementByIdHandler` call sites, and update the two Moq-based test files so their `Setup`/`Verify` expressions match the new two-argument overloads.

**Context**
`BankStatementImportRepository` wraps EF Core access to the `BankStatements` table. Four of its seven methods already accept and forward a `CancellationToken` (`GetFilteredAsync`, `GetExistingResultsByTransferIdsAsync`, `GetMaxStatementDateAsync`, `GetByTransferIdAsync`, `GetDailyStatisticsAsync`). `GetByIdAsync`, `AddAsync`, `UpdateAsync` do not, which means `ImportBankStatementHandler.ProcessStatementAsync` (a per-statement loop) and `GetBankStatementByIdHandler.Handle` cannot propagate MediatR's pipeline cancellation token into these three operations. This is a pure interface-consistency and plumbing fix — no behavioral change to import logic, dedup logic, or error handling.

Key facts confirmed directly from the files in this worktree:
- `IBankStatementImportRepository.cs` line 15: `Task<BankStatementImport?> GetByIdAsync(int id);` — no token.
- `IBankStatementImportRepository.cs` line 16: `Task<BankStatementImport> AddAsync(BankStatementImport bankStatement);` — no token.
- `IBankStatementImportRepository.cs` line 25: `Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement);` — no token.
- `BankStatementImportRepository.cs` lines 80–83: `GetByIdAsync` calls `_context.BankStatements.FindAsync(id)` — the single-key overload has no `CancellationToken` overload in EF Core; the array-of-keys overload (`FindAsync(object[], CancellationToken)`) must be used instead.
- `BankStatementImportRepository.cs` lines 85–100 (`AddAsync`) and 128–141 (`UpdateAsync`) call parameterless `_context.SaveChangesAsync()` inside a `try/catch (DbUpdateException)` block that sets `entry.State = EntityState.Detached` before rethrowing — this exception handling must be preserved verbatim.
- `ImportBankStatementHandler.cs` line 186: `ProcessStatementAsync` calls `InsertNewAsync(statement, accountSetting, itemCount, resultStatus)` without a token; `InsertNewAsync` itself (lines 193–207) has no `CancellationToken` parameter and calls `_repository.AddAsync(import)` (line 206) without one.
- `ImportBankStatementHandler.cs` lines 209–224 (`UpsertExistingAsync`): already has a `cancellationToken` parameter (used at line 216 for `GetByTransferIdAsync`), but the fallback call to `InsertNewAsync(statement, accountSetting, itemCount, resultStatus)` (line 218) and `_repository.UpdateAsync(existing)` (line 223) do not forward it.
- `GetBankStatementByIdHandler.cs` line 29: `var entity = await _repository.GetByIdAsync(request.Id);` — drops the handler's `cancellationToken` parameter (available from `Handle`'s signature on line 25).
- `GetBankStatementByIdHandlerTests.cs`: single-argument Moq `Setup`/`Verify` calls for `GetByIdAsync` at lines 42, 63, 78, 85, 100.
- `ImportBankStatementHandlerTests.cs`: single-argument Moq `Setup`/`Verify` calls for `AddAsync`/`UpdateAsync` at lines 139, 163, 177, 178, 220, 314, 328, 350, 362.
- `BankStatementImportRepositoryTests.cs`: ~30+ direct (non-Moq) calls to the real repository (e.g. `_repository.AddAsync(import)`, `_repository.GetByIdAsync(savedImport.Id)`, `_repository.UpdateAsync(existing)`) — these remain unchanged since they resolve to `cancellationToken = default`, which is behaviorally identical to today.

Because the new parameter has a `= default` value, the production code changes are source-compatible for any caller not yet updated — but Moq matches overloads by full parameter list, so once `GetBankStatementByIdHandler`/`ImportBankStatementHandler` call the two-argument overloads, existing single-argument `Setup`/`Verify` expressions in the two unit test files stop matching and would silently report `Times.Never` for calls that did happen. Both test files must be updated in this same change.

**Files to create/modify**

| Action | File |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` |

**Implementation steps**

1. **Confirm the current test suite passes before making changes**

Run the existing Bank test suite to establish a clean baseline before editing anything:

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"
```

Expected: all tests pass (this establishes the pre-change baseline; do not proceed to interpret failures here as caused by this change).

2. **Update `IBankStatementImportRepository` interface signatures**

In `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`, change lines 15–16 and line 25:

```csharp
    Task<BankStatementImport?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<BankStatementImport> AddAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);
```

and:

```csharp
    Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);
```

No other lines in this file change.

3. **Update `BankStatementImportRepository` implementation to match and forward the token**

In `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`, change `GetByIdAsync` (lines 80–83):

```csharp
    public async Task<BankStatementImport?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.BankStatements.FindAsync(new object[] { id }, cancellationToken);
    }
```

Change `AddAsync` (lines 85–100), keeping the `try/catch (DbUpdateException)` block and detach behavior exactly as-is:

```csharp
    public async Task<BankStatementImport> AddAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default)
    {
        var entry = _context.BankStatements.Add(bankStatement);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A failed INSERT leaves the entity tracked as Added; detach it so it cannot
            // be re-attempted by a later SaveChanges on the shared scoped DbContext.
            entry.State = EntityState.Detached;
            throw;
        }
        return bankStatement;
    }
```

Change `UpdateAsync` (lines 128–141), same pattern:

```csharp
    public async Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default)
    {
        var entry = _context.BankStatements.Update(bankStatement);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            entry.State = EntityState.Detached;
            throw;
        }
        return bankStatement;
    }
```

No other method bodies in this file change.

4. **Build to confirm the Domain/Persistence layers compile**

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync/backend
dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: build fails at this point — `ImportBankStatementHandler.cs` and `GetBankStatementByIdHandler.cs` still call the old signatures indirectly through the interface, but since the new parameter has a default value, those callers still compile fine. The build should actually succeed here. If it does not succeed, stop and investigate before proceeding — do not paper over an unexpected compile error.

5. **Thread the token through `GetBankStatementByIdHandler`**

In `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs`, change line 29:

```csharp
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
```

No other line in this file changes.

6. **Thread the token through `ImportBankStatementHandler.InsertNewAsync`, `UpsertExistingAsync`, and their call sites**

In `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`, change `InsertNewAsync`'s signature and body (lines 193–207):

```csharp
    private async Task<BankStatementImport> InsertNewAsync(
        BankStatementHeader statement,
        BankAccountConfiguration accountSetting,
        int itemCount,
        string resultStatus,
        CancellationToken cancellationToken)
    {
        var import = new BankStatementImport(statement.StatementId, statement.Date)
        {
            Account = accountSetting.Name,
            Currency = accountSetting.Currency,
            ItemCount = itemCount,
            ImportResult = resultStatus,
        };
        return await _repository.AddAsync(import, cancellationToken);
    }
```

Change `UpsertExistingAsync`'s body (lines 209–224) to forward `cancellationToken` to both the fallback `InsertNewAsync` call and to `UpdateAsync`:

```csharp
    private async Task<BankStatementImport> UpsertExistingAsync(
        BankStatementHeader statement,
        BankAccountConfiguration accountSetting,
        int itemCount,
        string resultStatus,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByTransferIdAsync(statement.StatementId, cancellationToken);
        if (existing == null)
            return await InsertNewAsync(statement, accountSetting, itemCount, resultStatus, cancellationToken);

        existing.Account = accountSetting.Name;
        existing.Currency = accountSetting.Currency;
        existing.UpdateImportOutcome(itemCount, resultStatus);
        return await _repository.UpdateAsync(existing, cancellationToken);
    }
```

Change `ProcessStatementAsync`'s call site (line 186) so the non-retry branch also forwards the token:

```csharp
        var saved = isRetry
            ? await UpsertExistingAsync(statement, accountSetting, itemCount, resultStatus, cancellationToken)
            : await InsertNewAsync(statement, accountSetting, itemCount, resultStatus, cancellationToken);
```

No other lines in this file change — control flow, retry/`isRetry` logic, logging, the "persist exactly once" comment (lines 182–183), and the `CancellationToken.None` usage in `Handle`'s catch block (line 148) are all left untouched.

7. **Build the full backend solution to confirm everything compiles**

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync/backend
dotnet build
```

Expected: build succeeds with no errors.

8. **Run the Bank test suite to see which Moq assertions now fail**

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"
```

Expected: `GetBankStatementByIdHandlerTests` and `ImportBankStatementHandlerTests` fail — specifically `Handle_WithExistingId_ReturnsMappedDto`, `Handle_WithMissingId_ReturnsNull`, `Handle_CallsRepositoryGetByIdExactlyOnce_WithTheRequestId`, `Handle_ProducesSameDtoAsListHandlerMapping_ForSameEntity` (repository setup no longer matches, so `ReturnsAsync` values are never returned, or the `Verify` call reports zero matching invocations), and any `ImportBankStatementHandlerTests` test whose assertions depend on `AddAsync`/`UpdateAsync` setups/verifies (e.g. `Handle_RetriesPreviouslyFailedStatement_ViaUpdateNotAdd`, `Handle_DoesNotDoubleInsert_WhenPersistenceFails`). `BankStatementImportRepositoryTests` should still pass unchanged. This confirms the Moq mismatch predicted by the spec/design and justifies step 9–10.

9. **Update `GetBankStatementByIdHandlerTests.cs` Moq expressions to match the two-argument overload**

In `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs`, update each single-argument `Setup`/`Verify` call for `GetByIdAsync` to include `It.IsAny<CancellationToken>()` as the second argument.

Line 42:
```csharp
        _repository
            .Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
```

Line 63:
```csharp
        _repository
            .Setup(r => r.GetByIdAsync(99999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankStatementImport?)null);
```

Line 78:
```csharp
        _repository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankStatementImport?)null);
```

Line 85:
```csharp
        _repository.Verify(r => r.GetByIdAsync(123, It.IsAny<CancellationToken>()), Times.Once);
```

Line 100:
```csharp
        _repository.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
```

No other lines in this file change.

10. **Update `ImportBankStatementHandlerTests.cs` Moq expressions to match the two-argument overload**

In `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`, update each single-argument `Setup`/`Verify` call for `AddAsync`/`UpdateAsync` to include `It.IsAny<CancellationToken>()` as the second argument.

Line 139:
```csharp
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()), Times.Never);
```

Lines 163–164:
```csharp
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankStatementImport b) => b);
```

Lines 177–178:
```csharp
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()), Times.Never);
```

Line 220 (part of `Handle_RecordsFailureWatermark_WhenStatementFails`):
```csharp
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankStatementImport b) => b);
```

Line 314 (part of `Handle_CollapsesDuplicateStatementIdsInResponse_ProcessesOnce`):
```csharp
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankStatementImport b) => b);
```

Line 328:
```csharp
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()), Times.Once);
```

Line 350–351 (part of `Handle_DoesNotDoubleInsert_WhenPersistenceFails`):
```csharp
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("duplicate key", (Exception?)null));
```

Line 362:
```csharp
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>(), It.IsAny<CancellationToken>()), Times.Once);
```

No other lines in this file change — `GetByTransferIdAsync` and `GetExistingResultsByTransferIdsAsync` setups already include `It.IsAny<CancellationToken>()` and are untouched.

11. **Run the full Bank test suite again to confirm all tests pass**

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"
```

Expected: all tests in `GetBankStatementByIdHandlerTests`, `ImportBankStatementHandlerTests`, and `BankStatementImportRepositoryTests` pass.

12. **Run `dotnet format` and the full solution build**

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync/backend
dotnet format
dotnet build
```

Expected: `dotnet format` reports no changes needed beyond whitespace already matching style (or applies only formatting normalization, no logic changes); `dotnet build` succeeds with zero errors/warnings introduced.

13. **Run the full backend test suite (not just Bank) to catch any unforeseen regressions**

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync/backend
dotnet test
```

Expected: all tests across `Anela.Heblo.Tests` and `Anela.Heblo.Adapters.*.Tests` pass — no regressions outside the Bank module, since no other module's interfaces or call sites were touched.

14. **Commit**

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync
git add backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs \
        backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs
git commit -m "Add CancellationToken to IBankStatementImportRepository GetByIdAsync/AddAsync/UpdateAsync

Brings the three outlier methods in line with the other four methods on
the interface, and threads the MediatR pipeline token through
ImportBankStatementHandler and GetBankStatementByIdHandler so in-flight
inserts/updates/reads can be cancelled."
```

**Verification**

Full verification for this task (run in order):

```bash
cd /home/user/worktrees/feature-3445-Arch-Review-Bank-Getbyidasync-Addasync-Updateasync/backend
dotnet build
dotnet format --verify-no-changes
dotnet test --filter "FullyQualifiedName~Features.Bank"
dotnet test
```

Expected: `dotnet build` succeeds with no errors; `dotnet format --verify-no-changes` reports no formatting diffs; both `dotnet test` invocations report 100% pass with no skipped/failed tests, and no test count regression compared to the pre-change baseline captured in step 1.

**Self-review (spec coverage check)**

- FR-1 (interface signatures) — covered by step 2.
- FR-2 (repository implementation: `FindAsync(new object[]{id}, cancellationToken)`, `SaveChangesAsync(cancellationToken)`, exception handling preserved) — covered by step 3.
- FR-3 (`ImportBankStatementHandler`: `InsertNewAsync` gains token param, `UpsertExistingAsync` forwards to `UpdateAsync` and fallback `InsertNewAsync`, `ProcessStatementAsync` call site updated) — covered by step 6.
- FR-4 (`GetBankStatementByIdHandler` line 29 forwards `cancellationToken`) — covered by step 5.
- FR-5 (test doubles updated; `BankStatementImportRepositoryTests.cs` left unchanged and still compiles) — covered by steps 9, 10; step 11 confirms `BankStatementImportRepositoryTests` still passes without edits.
- NFR-1 (no performance change) — no action needed; confirmed by design, no new round trips introduced.
- NFR-2 (no security surface change) — no action needed; no new inputs or trust boundaries.
- Out of Scope items (no changes to `BankStatementImportRepositoryTests.cs` call sites, no new explicit `ThrowIfCancellationRequested()` checks, no change to `CancellationToken.None` in `Handle`'s catch block, no broader repository audit) — explicitly respected: no such edits appear in any step above.
