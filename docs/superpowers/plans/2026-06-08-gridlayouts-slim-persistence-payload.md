# GridLayouts Slim Persistence Payload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist only the column data inside `GridLayouts.LayoutJson`, eliminating the redundant `gridKey` and dead `lastModified` fields while keeping the API contract and existing rows readable.

**Architecture:** Introduce a single internal `GridLayoutPersistencePayload` record at the feature root (`backend/src/Anela.Heblo.Application/Features/GridLayouts/`). Both handlers reference this record — the save handler serializes it, the get handler deserializes into it and assembles the public `GridLayoutDto` from the deserialized columns plus `entity.GridKey` and `entity.LastModified`. `System.Text.Json` ignores unknown keys, so legacy rows (with embedded `gridKey`/`lastModified`) deserialize cleanly without a migration.

**Tech Stack:** .NET 8, C# 12, `System.Text.Json`, MediatR, xUnit, Moq.

---

## File Structure

**New file:**
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` — internal sealed record holding only `Columns`; used by both save and get handlers.

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — serialize the slim record instead of the full DTO.
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — deserialize into the slim record, assemble `GridLayoutDto` from the record plus entity columns.
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs` — tighten the existing JSON assertion to reject `gridKey`/`lastModified`; add a round-trip pin test.
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` — add a legacy-format read test and an empty-object (`{}`) read test.

**Unchanged (do not touch):**
- `Contracts/GridLayoutDto.cs`, `Contracts/GridColumnStateDto.cs` — public API surface.
- `GridLayoutsModule.cs` — no DI changes.
- Persistence layer, domain layer, controllers, migrations.

---

## Background notes for the implementer

If you have not worked in this codebase before, read these first:

- `docs/architecture/development_guidelines.md` — explains the contracts/DTOs ownership rule (the new persistence record must NOT live under `Contracts/`).
- The existing `GridLayoutDto` (at `Contracts/GridLayoutDto.cs`) uses **explicit `[JsonPropertyName]` attributes** (camelCase). The Save handler calls `JsonSerializer.Serialize(payload)` with **no options**. So legacy rows in the database contain lowercase `"columns"`. The new slim record **must** also emit lowercase `"columns"` or legacy rows will fail to deserialize — handled below by `[JsonPropertyName("columns")]`.
- The Get handler already catches `JsonException` for malformed JSON and treats `null` deserialization as "no layout" — preserve both branches.
- Use `dotnet format` after each implementation step (project hook may run it automatically).
- Tests use xUnit + Moq, mirror existing style (no FluentAssertions in this folder).

---

## Task 1: Create the slim persistence record

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs`

- [ ] **Step 1: Create the file**

Create `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` with the following content:

```csharp
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;

namespace Anela.Heblo.Application.Features.GridLayouts;

internal sealed record GridLayoutPersistencePayload(
    [property: JsonPropertyName("columns")] List<GridColumnStateDto> Columns);
```

Why each piece matters:
- `internal` — cannot be referenced from `Anela.Heblo.API` or other feature folders; enforces that this is a persistence-only shape.
- `sealed record` — value semantics, no inheritance.
- `[property: JsonPropertyName("columns")]` — **load-bearing**. Without this the serializer would emit PascalCase `"Columns"`, and legacy rows (containing lowercase `"columns"`) would no longer deserialize correctly.
- Property type `List<GridColumnStateDto>` — reuses the existing column DTO so a future change to the column shape propagates to both ends.

- [ ] **Step 2: Build and confirm the file compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with no warnings about the new file.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs
git commit -m "feat(gridlayouts): add slim persistence payload record"
```

---

## Task 2: Tighten the existing SaveGridLayoutHandler test (RED — will fail against current handler? No: assertion is positive; we need negative assertions to drive change)

The current `Handle_CallsUpsertWithSerializedColumns` test only asserts presence of `"columns"`. It would pass against both the buggy and fixed implementations. We tighten it first so the next step is a real RED → GREEN transition.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs:22-50`

- [ ] **Step 1: Replace the assertion block in `Handle_CallsUpsertWithSerializedColumns`**

Find this block (currently lines 47-49 of the test):

```csharp
        Assert.NotNull(capturedJson);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(capturedJson!);
        Assert.True(parsed!.ContainsKey("columns"));
```

Replace it with:

```csharp
        Assert.NotNull(capturedJson);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(capturedJson!);
        Assert.NotNull(parsed);
        Assert.True(parsed!.ContainsKey("columns"));
        Assert.False(parsed.ContainsKey("gridKey"),
            "LayoutJson must not contain 'gridKey' — it is stored in the GridKey column.");
        Assert.False(parsed.ContainsKey("lastModified"),
            "LayoutJson must not contain 'lastModified' — it is stored in the LastModified column.");
```

- [ ] **Step 2: Run the test — confirm it FAILS against the current handler**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SaveGridLayoutHandlerTests.Handle_CallsUpsertWithSerializedColumns"
```

Expected: FAIL. The current handler emits `gridKey` and `lastModified` in the JSON; both new `Assert.False` assertions fire.

- [ ] **Step 3: Do not commit yet** — leave the test in failing state; we will commit it together with the implementation that makes it pass (Task 3). This keeps the RED → GREEN transition on one commit boundary.

---

## Task 3: Switch SaveGridLayoutHandler to serialize the slim payload (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs:30-36`

- [ ] **Step 1: Replace the payload construction**

Find lines 30-36 of `SaveGridLayoutHandler.cs`:

```csharp
        var payload = new GridLayoutDto
        {
            GridKey = request.GridKey,
            Columns = request.Columns
        };

        var json = JsonSerializer.Serialize(payload);
```

Replace with:

```csharp
        var payload = new GridLayoutPersistencePayload(request.Columns);

        var json = JsonSerializer.Serialize(payload);
```

- [ ] **Step 2: Remove the now-unused `Contracts` import if no other reference remains**

Look at the top of `SaveGridLayoutHandler.cs`. Line 2 currently reads:

```csharp
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
```

After Step 1, `GridLayoutDto` is no longer referenced in this file (`GridColumnStateDto` lives under `Contracts/` but is only referenced via `request.Columns`, whose type is declared on `SaveGridLayoutRequest`). The `using` is still needed transitively for the request type's column generic argument resolution at compile time? **Verify before removing.** If the build fails without it, restore the using directive. If the build still succeeds, leave it removed.

A safe path: leave the `using` in place. It costs nothing and avoids a churn-rebuild cycle.

- [ ] **Step 3: Run the tightened test — confirm it PASSES**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SaveGridLayoutHandlerTests.Handle_CallsUpsertWithSerializedColumns"
```

Expected: PASS.

- [ ] **Step 4: Run all SaveGridLayoutHandler tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SaveGridLayoutHandlerTests"
```

Expected: all green (including the `Handle_WhenDatabaseThrows_ReturnsDatabaseErrorAndLogsError` test, which is unaffected by the serialization change).

- [ ] **Step 5: Format**

Run:
```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs
git commit -m "refactor(gridlayouts): persist only columns in LayoutJson"
```

---

## Task 4: Add a round-trip pin test for the slim payload

This locks the on-disk JSON shape and the slim record to each other, so a future rename of `Columns` or removal of `[JsonPropertyName("columns")]` can't pass tests.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs` (add a new test)

- [ ] **Step 1: Add the new test inside `SaveGridLayoutHandlerTests`**

Append the following `[Fact]` method to the class (just before the closing `}` of the class):

```csharp
    [Fact]
    public async Task Handle_PersistedJsonDeserializesBackToColumns()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        string? capturedJson = null;
        _repositoryMock
            .Setup(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default))
            .Callback<string, string, string, CancellationToken>((_, _, json, _) => capturedJson = json)
            .Returns(Task.CompletedTask);

        var inputColumns = new List<GridColumnStateDto>
        {
            new() { Id = "col1", Order = 0, Width = 150, Hidden = false },
            new() { Id = "col2", Order = 1, Width = null, Hidden = true }
        };

        var request = new SaveGridLayoutRequest
        {
            GridKey = "test-grid",
            Columns = inputColumns
        };

        var handler = CreateHandler();
        await handler.Handle(request, default);

        Assert.NotNull(capturedJson);

        // Round-trip through the slim shape using the same JSON property name contract.
        // We deserialize via a local mirror of the persistence record to avoid making the
        // internal type accessible to the test assembly — the JSON shape is the contract.
        var roundTrip = JsonSerializer.Deserialize<RoundTripShape>(capturedJson!);
        Assert.NotNull(roundTrip);
        Assert.NotNull(roundTrip!.Columns);
        Assert.Equal(2, roundTrip.Columns!.Count);
        Assert.Equal("col1", roundTrip.Columns[0].Id);
        Assert.Equal(0, roundTrip.Columns[0].Order);
        Assert.Equal(150, roundTrip.Columns[0].Width);
        Assert.False(roundTrip.Columns[0].Hidden);
        Assert.Equal("col2", roundTrip.Columns[1].Id);
        Assert.Equal(1, roundTrip.Columns[1].Order);
        Assert.Null(roundTrip.Columns[1].Width);
        Assert.True(roundTrip.Columns[1].Hidden);
    }

    private sealed class RoundTripShape
    {
        [System.Text.Json.Serialization.JsonPropertyName("columns")]
        public List<GridColumnStateDto>? Columns { get; set; }
    }
```

This local `RoundTripShape` is intentional: tests must not reference the `internal` `GridLayoutPersistencePayload`, so we pin the on-disk JSON contract (`"columns"` → `List<GridColumnStateDto>`) from the outside.

- [ ] **Step 2: Run the new test**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SaveGridLayoutHandlerTests.Handle_PersistedJsonDeserializesBackToColumns"
```

Expected: PASS.

- [ ] **Step 3: Format**

```bash
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs
git commit -m "test(gridlayouts): pin slim payload JSON shape with round-trip test"
```

---

## Task 5: Add the legacy-format read test (RED)

This is the FR-3 acceptance test: legacy rows containing `gridKey` and `lastModified` keys inside the JSON must still deserialize cleanly, with the embedded values **ignored** in favor of the entity's `GridKey` and `LastModified`.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` (add a new test)

- [ ] **Step 1: Add the new test inside `GetGridLayoutHandlerTests`**

Append the following `[Fact]` method to the class:

```csharp
    [Fact]
    public async Task Handle_WhenLegacyJsonContainsEmbeddedGridKeyAndLastModified_IgnoresThemAndUsesEntityValues()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        var entityLastModified = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        var embeddedLegacyLastModified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Legacy payload shape — what old rows look like in the DB.
        var legacyPayload = new
        {
            gridKey = "stale-embedded-key",
            columns = new[]
            {
                new { id = "col1", order = 0, width = 120, hidden = false },
                new { id = "col2", order = 1, width = (int?)null, hidden = true }
            },
            lastModified = embeddedLegacyLastModified
        };
        var json = JsonSerializer.Serialize(legacyPayload);

        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync(new GridLayout
        {
            UserId = "user-1",
            GridKey = "test-grid",
            LayoutJson = json,
            LastModified = entityLastModified
        });

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.NotNull(response.Layout);
        // Embedded values must be ignored; entity values win.
        Assert.Equal("test-grid", response.Layout!.GridKey);
        Assert.Equal(entityLastModified, response.Layout.LastModified);
        // Columns must still come through from the JSON payload.
        Assert.Equal(2, response.Layout.Columns.Count);
        Assert.Equal("col1", response.Layout.Columns[0].Id);
        Assert.Equal(120, response.Layout.Columns[0].Width);
        Assert.False(response.Layout.Columns[0].Hidden);
        Assert.Equal("col2", response.Layout.Columns[1].Id);
        Assert.Null(response.Layout.Columns[1].Width);
        Assert.True(response.Layout.Columns[1].Hidden);
    }
```

- [ ] **Step 2: Run the test against the current (unmodified) get handler**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetGridLayoutHandlerTests.Handle_WhenLegacyJsonContainsEmbeddedGridKeyAndLastModified_IgnoresThemAndUsesEntityValues"
```

Expected: PASS (the current Get handler already overwrites `GridKey` and `LastModified` from the entity at lines 56-57). The test is still valuable because we are about to swap the deserialization target — if the slim record is wired incorrectly the test will catch it.

- [ ] **Step 3: Do not commit yet** — bundle with Task 6 (get-handler change) so the commit captures the read-path refactor as a single, coherent unit.

---

## Task 6: Switch GetGridLayoutHandler to deserialize the slim payload

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs:38-59`

- [ ] **Step 1: Replace the deserialize-and-overwrite block**

Find lines 38-59 of `GetGridLayoutHandler.cs`:

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

            if (dto is null)
            {
                return new GetGridLayoutResponse { Layout = null };
            }

            dto.GridKey = entity.GridKey;
            dto.LastModified = entity.LastModified;

            return new GetGridLayoutResponse { Layout = dto };
```

Replace with:

```csharp
            GridLayoutPersistencePayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<GridLayoutPersistencePayload>(entity.LayoutJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout",
                    userId, request.GridKey);
                return new GetGridLayoutResponse { Layout = null };
            }

            if (payload is null)
            {
                return new GetGridLayoutResponse { Layout = null };
            }

            var dto = new GridLayoutDto
            {
                GridKey = entity.GridKey,
                Columns = payload.Columns ?? new List<GridColumnStateDto>(),
                LastModified = entity.LastModified
            };

            return new GetGridLayoutResponse { Layout = dto };
```

Why the `?? new List<GridColumnStateDto>()` guard:
- `GridLayoutPersistencePayload` is a positional record with `Columns` typed as a non-nullable `List<GridColumnStateDto>`.
- When the JSON is `{}` (no `columns` key), `System.Text.Json` returns a record where the `Columns` slot is `null!` despite the non-nullable type — the compiler trusts the constructor parameter but the deserializer bypasses it.
- The guard preserves the prior empty-layout semantics and prevents a downstream `NullReferenceException` if any caller iterates `Layout.Columns`.

- [ ] **Step 2: Run the legacy-format test added in Task 5**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetGridLayoutHandlerTests.Handle_WhenLegacyJsonContainsEmbeddedGridKeyAndLastModified_IgnoresThemAndUsesEntityValues"
```

Expected: PASS — proves legacy rows still work after the deserialization target changed.

- [ ] **Step 3: Run all GetGridLayoutHandler tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetGridLayoutHandlerTests"
```

Expected: all green. Note in particular:
- `Handle_WhenSavedLayoutExists_ReturnsDeserializedDto` — uses the new slim shape (lowercase `"columns"`) directly, still passes.
- `Handle_WhenLayoutJsonIsMalformed_ReturnsNullLayoutAndLogsWarning` — `JsonException` branch unchanged.
- `Handle_WhenLayoutJsonIsEmpty_ReturnsNullLayoutAndLogsWarning` — empty string still throws inside the deserializer, branch unchanged.
- `Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog` — literal `"null"` JSON still deserializes to `null` payload, hits the `if (payload is null) return null` branch (no log expected). Note: the explicit check on `Times.Never` for warnings is preserved by this path.

- [ ] **Step 4: Format**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs
git commit -m "refactor(gridlayouts): deserialize slim payload on read"
```

---

## Task 7: Add the empty-object read test (covers FR-3 amended boundary)

The arch review's Amendment 3 calls out that the legacy story doesn't only cover "extra keys" — it must also cover "missing keys" (e.g., `{}`). The defensive `?? new List<GridColumnStateDto>()` guard added in Task 6 needs a test that exercises it.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` (add a new test)

- [ ] **Step 1: Add the test**

Append the following `[Fact]` method to `GetGridLayoutHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_WhenLayoutJsonIsEmptyObject_ReturnsLayoutWithEmptyColumns()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        var lastModified = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync(new GridLayout
        {
            UserId = "user-1",
            GridKey = "test-grid",
            LayoutJson = "{}",
            LastModified = lastModified
        });

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.NotNull(response.Layout);
        Assert.Equal("test-grid", response.Layout!.GridKey);
        Assert.Equal(lastModified, response.Layout.LastModified);
        Assert.NotNull(response.Layout.Columns);
        Assert.Empty(response.Layout.Columns);

        // Empty-object payload is well-formed JSON; no warning should be logged.
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

- [ ] **Step 2: Run the test**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetGridLayoutHandlerTests.Handle_WhenLayoutJsonIsEmptyObject_ReturnsLayoutWithEmptyColumns"
```

Expected: PASS. If it fails with `NullReferenceException` or asserts on `Columns == null`, the `?? new List<...>()` guard in `GetGridLayoutHandler` was not added correctly — revisit Task 6 Step 1.

- [ ] **Step 3: Format**

```bash
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs
git commit -m "test(gridlayouts): empty-object JSON returns layout with empty columns"
```

---

## Task 8: Full verification before declaring done

- [ ] **Step 1: Full backend build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: success, zero warnings about the GridLayouts feature.

- [ ] **Step 2: Run the full GridLayouts test suite**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayouts"
```

Expected: all green. This covers:
- `SaveGridLayoutHandlerTests` (3 tests — 2 original + 1 new)
- `GetGridLayoutHandlerTests` (7 tests — 5 original + 2 new)
- `ResetGridLayoutHandlerTests` (unchanged)
- `GridLayoutRepositoryTranslationTests` (unchanged)
- `PostgresExceptionTranslatorTests` (unchanged)

- [ ] **Step 3: Run `dotnet format` over the solution**

Run:
```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: no changes (everything was already formatted by per-task `dotnet format` steps).

- [ ] **Step 4: Confirm no behavior leaked through the API**

The `GridLayoutDto` API contract is unchanged. As a sanity check, grep for any other reference to `GridLayoutDto` in the codebase to make sure nothing else depends on the persistence shape:

```bash
```

Run:
```bash
rg -l "GridLayoutDto" backend/src backend/test
```

Expected output: only files under `Features/GridLayouts/Contracts/`, `Features/GridLayouts/UseCases/`, and the test files. No matches under `Persistence/` (the repository deals in strings, not DTOs). No matches under `API/` (the controller projects DTO through the MediatR response, which is fine).

If any unexpected file references `GridLayoutDto`, stop and review — the persistence-only shape should not leak.

- [ ] **Step 5: Confirm there are no stragglers serializing or deserializing the full DTO into LayoutJson**

```bash
rg -n "JsonSerializer\.(Serialize|Deserialize).*GridLayoutDto" backend/src backend/test
```

Expected: zero hits. After this refactor, no handler should be moving a `GridLayoutDto` through `JsonSerializer` — only `GridLayoutPersistencePayload` does that.

If anything shows up, fix it before declaring done.

---

## Self-review checklist (run after writing, fix in place)

- **Spec coverage:**
  - FR-1 (persist only Columns) — Tasks 2 + 3.
  - FR-2 (read assembles DTO from slim payload + entity columns) — Task 6.
  - FR-3 (backward compatibility for legacy rows) — Task 5 (legacy keys ignored) and Task 7 (empty object → empty columns; covers arch-review Amendment 3).
  - FR-4 (shared persistence shape) — Task 1 (single internal type referenced by both handlers).
  - NFR-1/2/3 — naturally satisfied; no extra tasks needed.
  - NFR-4 — Tasks 2, 4, 5, 7 add or tighten unit tests.

- **Arch-review amendments:**
  - Amendment 1 (also strip `LastModified`) — incorporated into Task 1 (slim record has no `LastModified`), Task 2 (test asserts absence of `lastModified` in JSON), and Task 3 (the new payload no longer carries the field).
  - Amendment 2 (tighten FR-1 acceptance to assert absence) — Task 2.
  - Amendment 3 (empty `{}` JSON yields empty columns) — Task 7.

- **Placeholder scan:** All steps contain exact code, exact file paths, exact commands. No "TBD", no "similar to above", no "add appropriate handling".

- **Type consistency:** `GridLayoutPersistencePayload(List<GridColumnStateDto> Columns)` is used identically in Task 1 (definition), Task 3 (`new GridLayoutPersistencePayload(request.Columns)`), and Task 6 (`JsonSerializer.Deserialize<GridLayoutPersistencePayload>(...)` then `payload.Columns ?? new List<GridColumnStateDto>()`). The `[JsonPropertyName("columns")]` attribute is the contract pinned by Task 4's round-trip test via the locally declared `RoundTripShape` (which mirrors the same `"columns"` property name).
