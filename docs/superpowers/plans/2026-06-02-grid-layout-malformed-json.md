# Graceful Handling of Malformed `LayoutJson` in `GetGridLayoutHandler` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wrap `JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson)` in `GetGridLayoutHandler` with a typed `try`/`catch (JsonException)` that logs a warning and returns `{ Layout = null }`, so corrupt `LayoutJson` rows fall back to "no saved layout" instead of surfacing a 500.

**Architecture:** Single MediatR handler edit (`GetGridLayoutHandler.Handle`). An inner `try` around just the deserialize call catches `JsonException` and emits a `LogWarning` with `UserId` + `GridKey`; a `null` deserialization result (legal JSON `"null"`) is also folded into the same fallback but without a log. The outer `PostgresException`/`NpgsqlException` block and the "entity is null" early return are preserved untouched. Frontend already handles `{ layout: null }` so no FE change is needed.

**Tech Stack:** .NET 8, C#, xUnit, Moq, MediatR, `System.Text.Json`, `Microsoft.Extensions.Logging`.

---

## File Structure

- **Modify:** `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`
  - One handler method (`Handle`). Add inner `try`/`catch (JsonException)` around the deserialize call. Add explicit `null` check after deserialize. Remove the existing `?? new GridLayoutDto()` fallback.

- **Modify:** `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs`
  - Add three new `[Fact]` tests:
    - `Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning`
    - `Handle_WhenLayoutJsonIsEmpty_ReturnsNullLayoutAndLogsWarning`
    - `Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog`
  - Reuse existing `Mock<IGridLayoutRepository>`, `Mock<ICurrentUserService>`, `Mock<ILogger<GetGridLayoutHandler>>` setup.

No other files are touched. No new packages, no DI changes, no schema or contract changes.

---

## Task 1: TDD — Malformed JSON path (catch `JsonException`, log warning, return null)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` (append a new `[Fact]`)
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` (replace the deserialize line with an inner try/catch)

- [ ] **Step 1: Write the failing test**

Append this `[Fact]` to `GetGridLayoutHandlerTests.cs` immediately after the existing `Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError` test (just before the closing `}` of the test class):

```csharp
    [Fact]
    public async Task Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync(new GridLayout
        {
            UserId = "user-1",
            GridKey = "test-grid",
            LayoutJson = "{not json",
            LastModified = DateTime.UtcNow
        });

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.Null(response.Layout);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Malformed LayoutJson")),
                It.IsAny<JsonException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the new test to verify it fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning"
```

Expected: **FAIL.** The handler currently lets `JsonException` propagate past the inner code (the outer catch only filters `PostgresException`/`NpgsqlException`), so the test will fail with a `JsonException` ("'{' is invalid after a property name") instead of asserting null.

- [ ] **Step 3: Implement the inner try/catch in the handler**

Open `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`.

Replace the single line:

```csharp
            var dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson) ?? new GridLayoutDto();
```

with the following block (note: `?? new GridLayoutDto()` is deliberately dropped — the spec/arch review treat literal-null JSON as "no usable layout"; that branch is added in Task 3):

```csharp
            GridLayoutDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout",
                    userId, request.GridKey);
                return new GetGridLayoutResponse { Layout = null };
            }
```

After this edit, the lines `dto.GridKey = entity.GridKey;` and `dto.LastModified = entity.LastModified;` will produce a CS8602 nullable warning (because `dto` is now `GridLayoutDto?`). That warning is fixed in Task 3 by adding the explicit `if (dto is null)` guard. For this task it is acceptable to leave the warning temporarily — only run the targeted test from Step 4, not the full build. The warning will be resolved before any commit-blocking validation (Task 4 step 4 runs `dotnet build`).

- [ ] **Step 4: Run the new test to verify it now passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning"
```

Expected: **PASS.**

- [ ] **Step 5: Run the three pre-existing tests to confirm no regression**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGridLayoutHandlerTests"
```

Expected: **All 4 tests pass** (`Handle_WhenNoSavedLayout_ReturnsNull`, `Handle_WhenSavedLayoutExists_ReturnsDeserializedDto`, `Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError`, `Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning`). Spec FR-3 requires the original three to continue passing without modification.

Do **not** commit yet — the file still has a nullable-warning and the literal-null path is not handled. Continue to Task 2.

---

## Task 2: TDD — Empty-string `LayoutJson` (covered by the same catch)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` (append a new `[Fact]`)

- [ ] **Step 1: Write the test**

Append this `[Fact]` immediately after `Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning`:

```csharp
    [Fact]
    public async Task Handle_WhenLayoutJsonIsEmpty_ReturnsNullLayoutAndLogsWarning()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync(new GridLayout
        {
            UserId = "user-1",
            GridKey = "test-grid",
            LayoutJson = string.Empty,
            LastModified = DateTime.UtcNow
        });

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.Null(response.Layout);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Malformed LayoutJson")),
                It.IsAny<JsonException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the new test to verify it passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_WhenLayoutJsonIsEmpty_ReturnsNullLayoutAndLogsWarning"
```

Expected: **PASS.** `JsonSerializer.Deserialize<GridLayoutDto>(string.Empty)` throws `JsonException` ("The input does not contain any JSON tokens"), which is caught by the inner block added in Task 1. No additional implementation change is required.

- [ ] **Step 3: Run all `GetGridLayoutHandlerTests` to confirm no regression**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGridLayoutHandlerTests"
```

Expected: **All 5 tests pass.**

Do **not** commit yet — proceed to Task 3 to handle the literal-null branch and clear the nullable warning.

---

## Task 3: TDD — Literal JSON `"null"` returns `Layout = null` and does **not** log

**Why:** `JsonSerializer.Deserialize<GridLayoutDto>("null")` returns `null` without throwing. The pre-change code papered this over with `?? new GridLayoutDto()`, which would emit a synthesized DTO with the entity's `GridKey` and zero columns — the FE would interpret that as "explicit layout with no columns", a worse failure mode than "no saved layout". Per arch-review Decision 2 and Amendment 1, the new behavior is `Layout = null` **with no log** (this is a degenerate-but-valid JSON value, not a corruption event).

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` (append a new `[Fact]`)
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` (add `if (dto is null) return null` guard)

- [ ] **Step 1: Write the failing test**

Append this `[Fact]` immediately after `Handle_WhenLayoutJsonIsEmpty_ReturnsNullLayoutAndLogsWarning`:

```csharp
    [Fact]
    public async Task Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync(new GridLayout
        {
            UserId = "user-1",
            GridKey = "test-grid",
            LayoutJson = "null",
            LastModified = DateTime.UtcNow
        });

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.Null(response.Layout);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the new test to verify it fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog"
```

Expected: **FAIL.** After Task 1 the handler holds `dto = null` (the `?? new GridLayoutDto()` fallback was removed) and proceeds to `dto.GridKey = entity.GridKey;`, which throws `NullReferenceException`. The test will fail with NRE (not with an assertion mismatch), confirming the null-deserialize branch is unhandled.

- [ ] **Step 3: Add the explicit null guard after deserialize**

Open `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` and locate the inner try/catch block added in Task 1.

Insert this block immediately after the closing `}` of the `catch (JsonException ex)` and before the `dto.GridKey = entity.GridKey;` line:

```csharp
            if (dto is null)
            {
                return new GetGridLayoutResponse { Layout = null };
            }
```

After this edit, the `Handle` method body should read (the inner try/catch and null-guard are the only new code; everything else is unchanged from the original):

```csharp
    public async Task<GetGridLayoutResponse> Handle(GetGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email
            ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

        try
        {
            var entity = await _repository.GetAsync(userId, request.GridKey, cancellationToken);

            if (entity is null)
            {
                return new GetGridLayoutResponse { Layout = null };
            }

            GridLayoutDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout",
                    userId, request.GridKey);
                return new GetGridLayoutResponse { Layout = null };
            }

            if (dto is null)
            {
                return new GetGridLayoutResponse { Layout = null };
            }

            dto.GridKey = entity.GridKey;
            dto.LastModified = entity.LastModified;

            return new GetGridLayoutResponse { Layout = dto };
        }
        catch (Exception ex) when (ex is PostgresException or NpgsqlException)
        {
            var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
            _logger.LogError(ex,
                "Database error reading GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, pgEx?.SqlState);
            return new GetGridLayoutResponse { Layout = null };
        }
    }
```

- [ ] **Step 4: Run the new test to verify it now passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog"
```

Expected: **PASS.**

- [ ] **Step 5: Run all `GetGridLayoutHandlerTests` to confirm full coverage**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGridLayoutHandlerTests"
```

Expected: **All 6 tests pass** (the original 3 + the 3 new ones).

---

## Task 4: Project-level validation — build, format, full test suite

**Files:** (no edits — verification only, except for any auto-formatting performed by `dotnet format`)

- [ ] **Step 1: Build the backend solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **Build succeeded** with zero new warnings. The CS8602 nullable warning that existed transiently after Task 1 must be gone (the `if (dto is null)` guard added in Task 3 narrows `dto` to non-null before the property assignments).

- [ ] **Step 2: Run `dotnet format` on the touched files**

Run:
```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs \
            backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs
```

Expected: Exits cleanly. If it rewrites whitespace, re-run the targeted tests from Step 3 below to confirm nothing broke.

- [ ] **Step 3: Run the targeted test class one more time**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGridLayoutHandlerTests"
```

Expected: **All 6 tests pass.**

- [ ] **Step 4: Sanity-check the diff is confined to the two files**

Run:
```bash
git status --short
git diff --stat
```

Expected: Exactly two modified files:
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs`

If any other file appears (in particular `SaveGridLayoutHandler.cs`, `ResetGridLayoutHandler.cs`, `GridLayoutDto.cs`, `IGridLayoutRepository.cs`, or any contract DTO), revert it — spec FR-5 requires the diff to be confined to these two files.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs
git commit -m "$(cat <<'EOF'
fix: gracefully handle malformed LayoutJson in GetGridLayoutHandler

Wrap JsonSerializer.Deserialize in a typed try/catch (JsonException);
log a Warning with UserId + GridKey and return Layout = null instead of
letting the exception surface as a 500. Treat a null deserialization
result (literal JSON "null") the same way but without a log, since it is
a degenerate-but-valid value and not a corruption event.

Adds three xUnit tests covering the malformed, empty, and literal-null
payloads. No public contract, schema, or sibling-handler changes.
EOF
)"
```

---

## Spec Coverage Map

| Spec requirement | Task(s) |
|------------------|---------|
| FR-1: Catch `JsonException` during deserialization | Task 1, Step 3 |
| FR-1 (arch amendment): `dto is null` → `Layout = null` without log | Task 3, Step 3 |
| FR-2: Warning log with phrase "Malformed LayoutJson", `UserId`, `GridKey`, exception attached | Task 1, Step 3 (implementation) + Task 1, Step 1 (test pin) |
| FR-3: No regression in the existing three paths (missing row / happy / DB error) | Task 1, Step 5; Task 4, Step 3 |
| FR-4: Test `Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning` | Task 1, Step 1 |
| FR-4: Test `Handle_WhenLayoutJsonIsEmpty_ReturnsNullLayoutAndLogsWarning` | Task 2, Step 1 |
| FR-4 (arch amendment): Test `Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog` | Task 3, Step 1 |
| FR-5: Diff confined to `GetGridLayoutHandler.cs` + its test file | Task 4, Step 4 |
| NFR-1 Performance: no extra I/O, only try-block entry on happy path | Implicit in Task 1/3 implementation (no allocations added) |
| NFR-2 Security: no `LayoutJson` payload echo, only `UserId` + `GridKey` in log | Task 1, Step 3 (log template uses only `{UserId}` and `{GridKey}`) |
| NFR-3 Observability: warning-level, searchable phrase, exception attached | Task 1, Step 3 |
| NFR-4 Compatibility: no schema or contract change | Task 4, Step 4 (diff scope check) |
