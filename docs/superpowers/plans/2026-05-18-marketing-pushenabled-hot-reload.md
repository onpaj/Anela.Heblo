# Marketing `PushEnabled` Runtime Hot-Reload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the three Marketing write handlers (`CreateMarketingActionHandler`, `UpdateMarketingActionHandler`, `DeleteMarketingActionHandler`) to read `MarketingCalendarOptions.PushEnabled` via `IOptionsMonitor<T>.CurrentValue` so the Outlook calendar-sync kill-switch responds to runtime configuration changes without an application restart.

**Architecture:** Pure DI-mechanism refactor inside the Application layer. Each handler swaps `IOptions<MarketingCalendarOptions>` for `IOptionsMonitor<MarketingCalendarOptions>` and reads the boolean directly at the call site (no caching, no `OnChange` subscription, no defensive `try/catch` — the read is a cached field access and `.ValidateOnStart()` already enforces binding at boot). The `TestOptionsMonitor<T>` test double currently nested inside `MarketingCategoryMapperTests` is promoted to a shared test helper so all three handler test suites can drive runtime toggles.

**Tech Stack:** .NET 8, C#, MediatR, `Microsoft.Extensions.Options`, xUnit, FluentAssertions, Moq, `Microsoft.Extensions.Logging.Abstractions`.

---

## File Structure

**Modified production files:**
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs`

Each handler changes in exactly three places: field type, constructor parameter type, and the `PushEnabled` access expression. The field name `_options` is retained.

**New test helper (shared):**
- `backend/test/Anela.Heblo.Tests/Helpers/TestOptionsMonitor.cs` — lifted verbatim from the private nested class in `MarketingCategoryMapperTests.cs:20-68`, made `public` so all test files in the assembly can use it. Placed under the existing `Helpers/` directory next to `FakeHttpMessageHandler.cs`.

**Modified test files:**
- `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCategoryMapperTests.cs` — delete the inline nested `TestOptionsMonitor<T>` and use the shared helper.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` — replace `Options.Create(...)` factory with `TestOptionsMonitor<MarketingCalendarOptions>`; add hot-reload tests required by FR-5.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` — same.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` — same.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs` — older duplicate; constructor signature must compile against `IOptionsMonitor<T>`.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs` — older duplicate covering all three handlers; three `BuildXxxHandler` methods must each take `IOptionsMonitor<T>`.

**Not modified:**
- `MarketingCalendarOptions.cs` — no schema change.
- `MarketingModule.cs` — DI registration unchanged; `IOptionsMonitor<T>` is auto-resolvable from `AddOptions<T>().Bind(...)`.
- `MarketingCategoryMapper.cs` — already correct.
- `OutlookCalendarSyncService.cs` — out of scope per spec; scoped lifetime, `GroupId` is not the kill-switch.

---

## Task 1: Promote `TestOptionsMonitor<T>` to a shared test helper

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Helpers/TestOptionsMonitor.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCategoryMapperTests.cs:20-68` (delete the nested class) and `:81-93` (use the shared helper)

- [ ] **Step 1: Create the shared test helper file**

Write `backend/test/Anela.Heblo.Tests/Helpers/TestOptionsMonitor.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Helpers;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> implementation for tests.
/// Lifted from MarketingCategoryMapperTests so all handler tests can drive
/// runtime option changes (PushEnabled toggling, category mapping reloads).
/// </summary>
public sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private T _current;
    private readonly List<Action<T, string?>> _listeners = new();

    public TestOptionsMonitor(T initial)
    {
        _current = initial;
    }

    public T CurrentValue => _current;

    public T Get(string? name) => _current;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new Subscription(() => _listeners.Remove(listener));
    }

    public void Set(T next)
    {
        _current = next;
        foreach (var l in _listeners.ToArray())
        {
            l(next, null);
        }
    }

    public void SetNull()
    {
        foreach (var l in _listeners.ToArray())
        {
            l(default(T)!, null);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;

        public Subscription(Action d)
        {
            _dispose = d;
        }

        public void Dispose() => _dispose();
    }
}
```

- [ ] **Step 2: Remove the inline nested class from `MarketingCategoryMapperTests.cs`**

In `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCategoryMapperTests.cs`, delete the entire nested helper block (lines 16-68 inclusive — the comment header and the `private sealed class TestOptionsMonitor<T>` definition through its closing brace). Add a `using` directive at the top of the file:

```csharp
using Anela.Heblo.Tests.Helpers;
```

The remaining file content (factories at line ~74 onward and the tests) is unchanged — `TestOptionsMonitor<MarketingCalendarOptions>` now resolves to the shared type because the local nested class is gone.

- [ ] **Step 3: Run the mapper tests to confirm parity**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingCategoryMapperTests"
```

Expected: all 11 mapper tests pass. If any fail, the helper extraction is wrong — diff against the original nested class and fix before moving on.

- [ ] **Step 4: Run a full backend build to confirm no other consumers broke**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Helpers/TestOptionsMonitor.cs \
        backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCategoryMapperTests.cs
git commit -m "refactor: extract TestOptionsMonitor to shared test helper"
```

---

## Task 2: Switch `CreateMarketingActionHandler` to `IOptionsMonitor`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs:20,25-27,33,72`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs:11,43-49,143-151` (replace `Options.Create` factory; add hot-reload tests)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs:13,34-43` (legacy duplicate — compile against new constructor)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs:16,51-63` (`BuildCreateHandler` only)

- [ ] **Step 1: Write the failing hot-reload test for Create**

In `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs`, add the following `using` directive near the existing imports:

```csharp
using Anela.Heblo.Tests.Helpers;
```

Then append the following two tests inside the class (after the existing `Handle_ReturnsUnauthorized_WhenUserNotAuthenticated` test, before the closing `}`):

```csharp
[Fact]
public async Task Handle_HonorsRuntimePushEnabledFlip_TrueToFalse()
{
    // Arrange
    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
        new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });
    var handler = new CreateMarketingActionHandler(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        monitor);

    // Act — first invocation with PushEnabled = true
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Flip the kill-switch at runtime
    monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });

    // Act — second invocation on the same handler instance
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Assert — Outlook called exactly once (for the first invocation only)
    _outlookSync.Verify(
        x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
        Times.Once);
}

[Fact]
public async Task Handle_HonorsRuntimePushEnabledFlip_FalseToTrue()
{
    // Arrange
    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
        new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });
    var handler = new CreateMarketingActionHandler(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        monitor);

    // Act — first invocation with PushEnabled = false
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Flip the kill-switch at runtime
    monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });

    // Act — second invocation on the same handler instance
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Assert — Outlook called exactly once (for the second invocation only)
    _outlookSync.Verify(
        x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Application.Marketing.CreateMarketingActionHandlerTests"
```

Expected: build FAILS with `CS1503` or similar — the `CreateMarketingActionHandler` constructor's fifth parameter is `IOptions<MarketingCalendarOptions>`, not `IOptionsMonitor<MarketingCalendarOptions>`. This is the red state.

- [ ] **Step 3: Update the handler to take `IOptionsMonitor<T>`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`:

Change line 20:

```csharp
// Before
private readonly IOptions<MarketingCalendarOptions> _options;

// After
private readonly IOptionsMonitor<MarketingCalendarOptions> _options;
```

Change line 27 (constructor signature, last parameter):

```csharp
// Before
IOptions<MarketingCalendarOptions> options)

// After
IOptionsMonitor<MarketingCalendarOptions> options)
```

Change line 72 (the `PushEnabled` check):

```csharp
// Before
if (_options.Value.PushEnabled)

// After
if (_options.CurrentValue.PushEnabled)
```

The existing `using Microsoft.Extensions.Options;` on line 10 already imports `IOptionsMonitor<T>`; no using change is required.

- [ ] **Step 4: Update the existing `BuildHandler` factory in the canonical Create test class**

In `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs`, replace lines 43-49 (the `BuildHandler` method) with:

```csharp
private CreateMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
    new(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));
```

Remove the now-unused `using Microsoft.Extensions.Options;` directive at line 11 (the test file no longer references `Options.Create` or `IOptions<T>` directly).

- [ ] **Step 5: Update the legacy duplicate Create test file**

In `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs`, add the helper using directive at the top:

```csharp
using Anela.Heblo.Tests.Helpers;
```

Replace the constructor body's Options setup (lines 34-43, the block that builds `mockOptions` via `Mock<IOptions<MarketingCalendarOptions>>` and constructs `_handler`):

```csharp
// Before
var options = new MarketingCalendarOptions { PushEnabled = false, GroupId = "test@example.com" };
var mockOptions = new Mock<IOptions<MarketingCalendarOptions>>();
mockOptions.Setup(o => o.Value).Returns(options);

_handler = new CreateMarketingActionHandler(
    _repositoryMock.Object,
    _currentUserServiceMock.Object,
    _loggerMock.Object,
    _outlookSyncMock.Object,
    mockOptions.Object);

// After
var options = new MarketingCalendarOptions { PushEnabled = false, GroupId = "test@example.com" };
var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(options);

_handler = new CreateMarketingActionHandler(
    _repositoryMock.Object,
    _currentUserServiceMock.Object,
    _loggerMock.Object,
    _outlookSyncMock.Object,
    monitor);
```

Then remove the unused `using Microsoft.Extensions.Options;` directive at line 13.

- [ ] **Step 6: Update `BuildCreateHandler` in the legacy combined-sync test file**

In `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs`, add the helper using directive at the top:

```csharp
using Anela.Heblo.Tests.Helpers;
```

Replace lines 51-63 (`BuildCreateHandler` method) with:

```csharp
private CreateMarketingActionHandler BuildCreateHandler(bool pushEnabled)
{
    var options = new MarketingCalendarOptions { PushEnabled = pushEnabled, GroupId = "cal@example.com" };
    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(options);

    return new CreateMarketingActionHandler(
        _repositoryMock.Object,
        _currentUserServiceMock.Object,
        NullLogger<CreateMarketingActionHandler>.Instance,
        _outlookSyncMock.Object,
        monitor);
}
```

Do **not** touch `BuildUpdateHandler` or `BuildDeleteHandler` in this step — they are wired in Tasks 3 and 4 respectively. Leave the `using Microsoft.Extensions.Options;` directive in place for now (it is still needed by the other two builders).

- [ ] **Step 7: Run the Create handler tests and verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateMarketingActionHandlerTests"
```

Expected: all Create handler tests pass — the original 7 in `Application/Marketing/`, the 2 new hot-reload tests, and the 2 in `Features/Marketing/`. Confirm the two new `Handle_HonorsRuntimePushEnabledFlip_*` tests are present in the output and green.

- [ ] **Step 8: Run the full backend build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds. `MarketingActionHandlerSyncTests.cs`'s `BuildUpdateHandler` and `BuildDeleteHandler` still compile because the Update and Delete handlers haven't been changed yet.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs
git commit -m "feat(marketing): hot-reload PushEnabled in CreateMarketingActionHandler"
```

---

## Task 3: Switch `UpdateMarketingActionHandler` to `IOptionsMonitor`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs:25,32,70`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs:11,63-69` (replace `Options.Create` factory; add hot-reload tests)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs:65-77` (`BuildUpdateHandler` only)

- [ ] **Step 1: Write the failing hot-reload test for Update**

In `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs`, add the helper using directive near the existing imports:

```csharp
using Anela.Heblo.Tests.Helpers;
```

Then append the following two tests inside the class (after the existing `Handle_UpdatesProductsAndFolderLinks_WhenProvided` test, before the closing `}`):

```csharp
[Fact]
public async Task Handle_HonorsRuntimePushEnabledFlip_TrueToFalse()
{
    // Arrange — fresh repository setup so two invocations both find the action
    _repository
        .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => BuildExistingAction());

    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
        new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });
    var handler = new UpdateMarketingActionHandler(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        monitor);

    // Act — first invocation with PushEnabled = true (existing event → UpdateEventAsync)
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Flip the kill-switch at runtime
    monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });

    // Act — second invocation on the same handler instance
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Assert — Outlook called exactly once (first invocation only)
    _outlookSync.Verify(
        x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
        Times.Once);
    _outlookSync.Verify(
        x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
        Times.Never);
}

[Fact]
public async Task Handle_HonorsRuntimePushEnabledFlip_FalseToTrue()
{
    // Arrange
    _repository
        .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => BuildExistingAction());

    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
        new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });
    var handler = new UpdateMarketingActionHandler(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        monitor);

    // Act — first invocation with PushEnabled = false
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Flip the kill-switch at runtime
    monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });

    // Act — second invocation on the same handler instance
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Assert — Outlook UpdateEventAsync called exactly once (second invocation only,
    // because BuildExistingAction has an OutlookEventId so Update path is taken).
    _outlookSync.Verify(
        x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Application.Marketing.UpdateMarketingActionHandlerTests"
```

Expected: build FAILS with `CS1503` — the `UpdateMarketingActionHandler` constructor still takes `IOptions<MarketingCalendarOptions>`.

- [ ] **Step 3: Update the handler to take `IOptionsMonitor<T>`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`:

Change line 25:

```csharp
// Before
private readonly IOptions<MarketingCalendarOptions> _options;

// After
private readonly IOptionsMonitor<MarketingCalendarOptions> _options;
```

Change line 32 (constructor signature, last parameter):

```csharp
// Before
IOptions<MarketingCalendarOptions> options)

// After
IOptionsMonitor<MarketingCalendarOptions> options)
```

Change line 70 (the `PushEnabled` check):

```csharp
// Before
if (_options.Value.PushEnabled)

// After
if (_options.CurrentValue.PushEnabled)
```

- [ ] **Step 4: Update the existing `BuildHandler` factory in the canonical Update test class**

In `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs`, replace lines 63-69 (the `BuildHandler` method) with:

```csharp
private UpdateMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
    new(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));
```

Remove the now-unused `using Microsoft.Extensions.Options;` directive at line 11.

- [ ] **Step 5: Update `BuildUpdateHandler` in the legacy combined-sync test file**

In `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs`, replace lines 65-77 (the `BuildUpdateHandler` method) with:

```csharp
private UpdateMarketingActionHandler BuildUpdateHandler(bool pushEnabled)
{
    var options = new MarketingCalendarOptions { PushEnabled = pushEnabled, GroupId = "cal@example.com" };
    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(options);

    return new UpdateMarketingActionHandler(
        _repositoryMock.Object,
        _currentUserServiceMock.Object,
        NullLogger<UpdateMarketingActionHandler>.Instance,
        _outlookSyncMock.Object,
        monitor);
}
```

Leave `BuildDeleteHandler` and the existing `using Microsoft.Extensions.Options;` directive in place — both are still needed for Task 4.

- [ ] **Step 6: Run the Update handler tests and verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests"
```

Expected: all Update handler tests pass — the original 8 plus the 2 new hot-reload tests. Also re-run the cross-cutting sync tests to confirm the Update path inside `MarketingActionHandlerSyncTests` still passes:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingActionHandlerSyncTests"
```

Expected: all 7 sync tests pass.

- [ ] **Step 7: Run the full backend build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds. `BuildDeleteHandler` in the sync test file still compiles because the Delete handler hasn't been changed yet.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs
git commit -m "feat(marketing): hot-reload PushEnabled in UpdateMarketingActionHandler"
```

---

## Task 4: Switch `DeleteMarketingActionHandler` to `IOptionsMonitor`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs:20,27,54`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs:11,53-59` (replace `Options.Create` factory; add hot-reload tests)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs:79-91` (`BuildDeleteHandler` only)

- [ ] **Step 1: Write the failing hot-reload test for Delete**

In `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs`, add the helper using directive near the existing imports:

```csharp
using Anela.Heblo.Tests.Helpers;
```

Then append the following two tests inside the class (after the existing `Handle_ReturnsNotFound_WhenActionDoesNotExist` test, before the closing `}`):

```csharp
[Fact]
public async Task Handle_HonorsRuntimePushEnabledFlip_TrueToFalse()
{
    // Arrange — return a fresh action with an OutlookEventId on every call
    _repository
        .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => BuildExistingAction());

    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
        new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });
    var handler = new DeleteMarketingActionHandler(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        monitor);

    // Act — first invocation with PushEnabled = true
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Flip the kill-switch at runtime
    monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });

    // Act — second invocation on the same handler instance
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Assert — Outlook DeleteEventAsync called exactly once (first invocation only)
    _outlookSync.Verify(
        x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Once);
}

[Fact]
public async Task Handle_HonorsRuntimePushEnabledFlip_FalseToTrue()
{
    // Arrange
    _repository
        .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => BuildExistingAction());

    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
        new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });
    var handler = new DeleteMarketingActionHandler(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        monitor);

    // Act — first invocation with PushEnabled = false
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Flip the kill-switch at runtime
    monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });

    // Act — second invocation on the same handler instance
    await handler.Handle(BuildRequest(), CancellationToken.None);

    // Assert — Outlook DeleteEventAsync called exactly once (second invocation only)
    _outlookSync.Verify(
        x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Application.Marketing.DeleteMarketingActionHandlerTests"
```

Expected: build FAILS with `CS1503` — the `DeleteMarketingActionHandler` constructor still takes `IOptions<MarketingCalendarOptions>`.

- [ ] **Step 3: Update the handler to take `IOptionsMonitor<T>`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs`:

Change line 20:

```csharp
// Before
private readonly IOptions<MarketingCalendarOptions> _options;

// After
private readonly IOptionsMonitor<MarketingCalendarOptions> _options;
```

Change line 27 (constructor signature, last parameter):

```csharp
// Before
IOptions<MarketingCalendarOptions> options)

// After
IOptionsMonitor<MarketingCalendarOptions> options)
```

Change line 54 (the `PushEnabled` check):

```csharp
// Before
if (_options.Value.PushEnabled && !string.IsNullOrEmpty(action.OutlookEventId))

// After
if (_options.CurrentValue.PushEnabled && !string.IsNullOrEmpty(action.OutlookEventId))
```

- [ ] **Step 4: Update the existing `BuildHandler` factory in the canonical Delete test class**

In `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs`, replace lines 53-59 (the `BuildHandler` method) with:

```csharp
private DeleteMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
    new(
        _repository.Object,
        _currentUserService.Object,
        _logger.Object,
        _outlookSync.Object,
        new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));
```

Remove the now-unused `using Microsoft.Extensions.Options;` directive at line 11.

- [ ] **Step 5: Update `BuildDeleteHandler` in the legacy combined-sync test file**

In `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs`, replace lines 79-91 (the `BuildDeleteHandler` method) with:

```csharp
private DeleteMarketingActionHandler BuildDeleteHandler(bool pushEnabled)
{
    var options = new MarketingCalendarOptions { PushEnabled = pushEnabled, GroupId = "cal@example.com" };
    var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(options);

    return new DeleteMarketingActionHandler(
        _repositoryMock.Object,
        _currentUserServiceMock.Object,
        NullLogger<DeleteMarketingActionHandler>.Instance,
        _outlookSyncMock.Object,
        monitor);
}
```

Now that none of the three `BuildXxxHandler` methods reference `IOptions<T>` or `Mock<IOptions<...>>` anywhere in the file, remove the unused `using Microsoft.Extensions.Options;` directive at line 16.

- [ ] **Step 6: Run the Delete handler tests and verify all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DeleteMarketingActionHandlerTests"
```

Expected: all Delete handler tests pass — the original 7 plus the 2 new hot-reload tests.

- [ ] **Step 7: Run the full marketing test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Marketing"
```

Expected: all marketing-namespace tests pass — mapper tests, all three handler test classes (canonical), the two legacy duplicate handler test files, and any other marketing tests in the project.

- [ ] **Step 8: Run the full backend build and the full test suite**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds; all tests in the suite pass with no regressions outside marketing.

- [ ] **Step 9: Run `dotnet format` and commit**

```bash
dotnet format backend/Anela.Heblo.sln
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs
git commit -m "feat(marketing): hot-reload PushEnabled in DeleteMarketingActionHandler"
```

If `dotnet format` produces additional whitespace-only changes in unrelated files, stage and commit those separately with `chore: dotnet format` rather than bundling them into the feature commit.

---

## Task 5: Final verification

**Files:** none modified — this task only verifies the end state.

- [ ] **Step 1: Confirm no remaining `IOptions<MarketingCalendarOptions>` references in the three handlers**

```bash
grep -nE "IOptions<MarketingCalendarOptions>" \
  backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs
```

Expected: each file shows only `IOptionsMonitor<MarketingCalendarOptions>` matches (because `IOptionsMonitor` contains the substring `IOptions`). To check there is no exact `IOptions<...>` (without `Monitor`):

```bash
grep -nE "IOptions<MarketingCalendarOptions>" \
  backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/*/*Handler.cs \
  | grep -v IOptionsMonitor
```

Expected: empty output (no matches).

- [ ] **Step 2: Confirm `OutlookCalendarSyncService` is untouched (out of scope per spec)**

```bash
git diff main -- backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs
```

Expected: empty diff. This service was intentionally left on `IOptions<T>` because it is scoped per request and `GroupId` is not the kill-switch.

- [ ] **Step 3: Confirm `MarketingModule` DI registration is untouched**

```bash
git diff main -- backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs
```

Expected: empty diff. `IOptionsMonitor<MarketingCalendarOptions>` is auto-resolvable from the existing `AddOptions<T>().Bind(...)` call; no new registration is needed.

- [ ] **Step 4: Run the full test suite and full backend build one more time as a final gate**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: both succeed.

- [ ] **Step 5: Manually confirm the PR description should call out the duplicate test files**

When opening the PR, include a note in the description (do not modify any code for this step):

> Two duplicate Marketing handler test classes exist alongside the canonical ones in `Application/Marketing/`:
> - `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs`
> - `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs`
>
> Both were updated to compile against the new `IOptionsMonitor<T>` constructor signature but were intentionally **not** deleted as part of this change. Recommend follow-up to consolidate or remove these duplicates.

No commit produced by this step — it is a PR-description reminder, not a code change.
