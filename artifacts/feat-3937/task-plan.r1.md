# RefreshOrphanContactsHandler Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise `RefreshOrphanContactsHandler` line coverage from 24.5% to at least the 60% threshold by adding unit tests for its two skip branches, its two failure-isolation branches (including the `ChangeTracker.Clear()` corruption guard), and a happy-path baseline.

**Architecture:** One new xUnit test class, `RefreshOrphanContactsHandlerTests`, mirroring the existing `CloseConversationHandlerTests` convention: `Mock<T>` (Moq) for `ISmartsuppRepository`, `ISmartsuppApiClient`, `ISmartsuppContactEnricher`, `ILogger<T>`, plus a real `ApplicationDbContext` backed by the EF Core in-memory provider (per `ListWebhookAuditHandlerTests.CreateContext()`) for the handler's direct `_db.SmartsuppConversations` read. No production code changes.

**Tech Stack:** .NET 8, xUnit, Moq 4.20.72, FluentAssertions, EF Core InMemory provider.

---

## File Structure

Single new file, no existing files modified:

- **Create:** `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`
  - Responsibility: unit tests for `RefreshOrphanContactsHandler.Handle` covering skip paths, failure isolation, and the success path.

Each task below appends new `[Fact]` methods (and, in Task 1, the class scaffold) to this single file — the file is small enough (≤ ~7 test methods) that splitting further would fight the "files that change together live together" guidance rather than serve it.

---

### task: refresh-orphan-contacts-skip-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`
- Test: same file (this task creates it)

- [ ] **Step 1: Write the failing tests (class scaffold + FR-1 + FR-2)**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class RefreshOrphanContactsHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ISmartsuppContactEnricher> _enricher = new();
    private readonly Mock<ILogger<RefreshOrphanContactsHandler>> _logger = new();

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"orphan_{Guid.NewGuid()}").Options);

    private RefreshOrphanContactsHandler CreateHandler(ApplicationDbContext db) =>
        new(_repo.Object, _apiClient.Object, _enricher.Object, db, _logger.Object);

    private void SetupIds(params string[] ids) =>
        _repo.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.ToList());

    private static SmartsuppConversation MakeLocalConversation(string id) => new()
    {
        Id = id,
        Status = SmartsuppConversationStatus.Open,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        SyncedAt = DateTime.UtcNow,
        Messages = new(),
    };

    [Fact]
    public async Task Handle_IncrementsSkippedNoContactId_WhenRemoteContactIdIsNull()
    {
        // Arrange
        SetupIds("conv-1");
        _apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = null });
        using var db = CreateContext();

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Scanned.Should().Be(1);
        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        response.Failed.Should().Be(0);
        _enricher.Verify(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IncrementsSkippedNoContactId_WhenLocalConversationNotFound()
    {
        // Arrange
        SetupIds("conv-1");
        _apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = "contact-1" });
        using var db = CreateContext(); // no local row seeded for "conv-1"

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        response.Failed.Should().Be(0);
        _enricher.Verify(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: FAIL to compile/run — the file did not previously exist, so this establishes the new tests execute against the real (already-implemented) handler. Since `RefreshOrphanContactsHandler` already has this behavior, expect these two tests to PASS immediately once the file compiles — there is no production code to write for this task. If either test fails against the real handler, STOP: this means the handler does not actually behave as documented in the brief/spec, and that discrepancy must be reported rather than papered over (see spec NFR-1).

- [ ] **Step 3: Confirm both tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: PASS (2 passed, 0 failed)

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs
git commit -m "test(smartsupp): cover RefreshOrphanContactsHandler skip-no-contact-id branches"
```

---

### task: refresh-orphan-contacts-failure-isolation-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs` (append methods inside the existing class, after the two skip tests from the previous task)
- Test: same file

- [ ] **Step 1: Write the failing tests (FR-3, FR-4, and continue-after-failure)**

Add these three `[Fact]` methods inside the `RefreshOrphanContactsHandlerTests` class (after `Handle_IncrementsSkippedNoContactId_WhenLocalConversationNotFound`):

```csharp
    [Fact]
    public async Task Handle_ClearsChangeTracker_WhenEnrichContactAsyncThrows()
    {
        // Arrange
        SetupIds("conv-fail");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); // reset tracking noise from seeding, isolate the handler's own effect

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(0);
        // Without ChangeTracker.Clear(), the entity mutated by `local.ContactId = remote.ContactId`
        // just before the throw would still be tracked as Modified. An empty tracker here proves
        // the handler's catch block actually called _db.ChangeTracker.Clear().
        db.ChangeTracker.Entries().Should().BeEmpty();
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IsolatesFailure_WhenUpsertConversationAsyncThrows()
    {
        // Arrange
        SetupIds("conv-fail");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));
        _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upsert boom"));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(0);
        db.ChangeTracker.Entries().Should().BeEmpty();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ContinuesToNextItem_AfterAFailure()
    {
        // Arrange
        SetupIds("conv-fail", "conv-ok");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-fail"));
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-ok"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-fail", ContactId = "contact-1" });
        _apiClient.Setup(a => a.GetConversationAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-ok", ContactId = "contact-2" });

        _enricher.Setup(e => e.EnrichContactAsync(
                It.Is<SmartsuppConversation>(c => c.Id == "conv-fail"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));
        _enricher.Setup(e => e.EnrichContactAsync(
                It.Is<SmartsuppConversation>(c => c.Id == "conv-ok"), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Scanned.Should().Be(2);
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(1); // conv-ok was still processed despite conv-fail's exception
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "conv-ok"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: all 5 tests present; the 3 new ones should PASS against the existing handler implementation (the brief documents this behavior as already implemented — these tests lock it in). If `Handle_ClearsChangeTracker_WhenEnrichContactAsyncThrows` or `Handle_IsolatesFailure_WhenUpsertConversationAsyncThrows` fails on the `ChangeTracker.Entries().Should().BeEmpty()` assertion, STOP and report: it means `_db.ChangeTracker.Clear()` is missing or not effective, which is the exact regression the brief warns about — do not weaken the assertion to make it pass.

- [ ] **Step 3: Confirm all 5 tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: PASS (5 passed, 0 failed)

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs
git commit -m "test(smartsupp): cover RefreshOrphanContactsHandler per-item failure isolation"
```

---

### task: refresh-orphan-contacts-happy-path-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs` (append one method)
- Test: same file

No dedicated test exercises the successful-update path today (confirmed: no existing test file references `RefreshOrphanContactsHandler` or `RefreshOrphanContactsRequest` anywhere in `backend/test`). The four branch tests above only exercise skip/failure paths, so `response.Updated++` and the full success flow (`EnrichContactAsync` → `UpsertConversationAsync` → `SaveChangesAsync`) remain uncovered without this task. Per the architecture review's coverage-baseline risk, this task is required to reliably clear the 60% threshold.

- [ ] **Step 1: Write the failing test**

Add this `[Fact]` method inside the `RefreshOrphanContactsHandlerTests` class (after `Handle_ContinuesToNextItem_AfterAFailure`):

```csharp
    [Fact]
    public async Task Handle_IncrementsUpdated_WhenItemProcessedSuccessfully()
    {
        // Arrange
        SetupIds("conv-ok");
        using var db = CreateContext();
        db.SmartsuppConversations.Add(MakeLocalConversation("conv-ok"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _apiClient.Setup(a => a.GetConversationAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-ok", ContactId = "contact-1" });
        _enricher.Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .Returns<SmartsuppConversation, CancellationToken>((c, _) => Task.FromResult(c));

        // Act
        var response = await CreateHandler(db).Handle(new RefreshOrphanContactsRequest(), CancellationToken.None);

        // Assert
        response.Scanned.Should().Be(1);
        response.Updated.Should().Be(1);
        response.SkippedNoContactId.Should().Be(0);
        response.Failed.Should().Be(0);
        response.FailedIds.Should().BeEmpty();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "conv-ok" && c.ContactId == "contact-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: 6 tests present; the new one should PASS against the existing handler (it's an assertion of already-implemented behavior). If it fails, STOP and report the discrepancy rather than adjusting the test to match unexpected behavior (per spec NFR-1).

- [ ] **Step 3: Confirm all 6 tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
Expected: PASS (6 passed, 0 failed)

- [ ] **Step 4: Run full backend test suite to confirm no regressions**

Run: `dotnet build && dotnet format --verify-no-changes && dotnet test backend/test/Anela.Heblo.Tests`
Expected: build succeeds, formatting clean, full suite PASS.

- [ ] **Step 5: Verify coverage of the target file meets the 60% threshold**

Run the project's coverage tooling against `backend/test/Anela.Heblo.Tests` (per `docs/testing/testing-strategy.md` if a specific coverage command is documented there; otherwise `dotnet test backend/test/Anela.Heblo.Tests /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura` or the equivalent already configured for this repo) and confirm `RefreshOrphanContactsHandler.cs` line coverage is now ≥ 60% (up from 24.5%).
Expected: coverage report shows `RefreshOrphanContactsHandler.cs` at or above 60% line coverage.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs
git commit -m "test(smartsupp): cover RefreshOrphanContactsHandler success path, close coverage gap"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (remote contact null path) → `task: refresh-orphan-contacts-skip-tests`, `Handle_IncrementsSkippedNoContactId_WhenRemoteContactIdIsNull`. Covered.
- FR-2 (local conversation not found) → `task: refresh-orphan-contacts-skip-tests`, `Handle_IncrementsSkippedNoContactId_WhenLocalConversationNotFound`. Covered.
- FR-3 (enrichment exception isolation, incl. `ChangeTracker.Clear()`, loop continuation, `Updated` not incremented) → `task: refresh-orphan-contacts-failure-isolation-tests`, `Handle_ClearsChangeTracker_WhenEnrichContactAsyncThrows` + `Handle_ContinuesToNextItem_AfterAFailure`. Covered.
- FR-4 (repository upsert exception isolation) → `task: refresh-orphan-contacts-failure-isolation-tests`, `Handle_IsolatesFailure_WhenUpsertConversationAsyncThrows`. Covered.
- NFR-1 (no behavior change; report discrepancies rather than papering over) → explicit STOP instructions in Step 2 of each task. Covered.
- NFR-2 (mocked collaborators + in-memory `ApplicationDbContext`, deterministic) → all tasks use `Mock<T>` for the three interfaces and `UseInMemoryDatabase` with a unique name per test. Covered.
- NFR-3 (60% coverage threshold) → `task: refresh-orphan-contacts-happy-path-test`, Step 5 explicitly verifies this. Covered.
- Out-of-scope items (no handler refactor, no new integration/E2E tests) → no task modifies `RefreshOrphanContactsHandler.cs` or adds anything outside the one new unit test file. Confirmed not violated.

**2. Placeholder scan:** No "TBD"/"TODO"/"add appropriate error handling" phrasing anywhere in the tasks above; every step shows complete, concrete C# code or an exact runnable command with its expected output.

**3. Type consistency:** `RefreshOrphanContactsHandler`, `RefreshOrphanContactsRequest`, `RefreshOrphanContactsResponse` (`Scanned`, `Updated`, `SkippedNoContactId`, `Failed`, `FailedIds`), `ISmartsuppRepository` (`ListOrphanContactConversationIdsAsync`, `UpsertConversationAsync`, `SaveChangesAsync`), `ISmartsuppApiClient` (`GetConversationAsync`), `ISmartsuppContactEnricher` (`EnrichContactAsync`), `SmartsuppConversation`, `SmartsuppConversationData`, `SmartsuppConversationStatus.Open`, and `ApplicationDbContext.SmartsuppConversations` are used identically (same names, same signatures) across all three tasks — verified directly against the actual source files read during architecture review (`RefreshOrphanContactsHandler.cs`, `ISmartsuppRepository.cs`, `ISmartsuppApiClient.cs`, `ISmartsuppContactEnricher.cs`, `SmartsuppConversation.cs`).
