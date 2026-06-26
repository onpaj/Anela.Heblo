# Smartsupp Webhook Duplicate-Key Race Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent `HTTP 500` on `POST /api/webhooks/smartsupp` caused by a check-then-insert race that produces `23505 duplicate key` on `PK_SmartsuppConversations`.

**Architecture:** Add `DiscardChanges()` to `ISmartsuppRepository` so the handler can clear tracked-but-unsaved entities, then wrap `reaction.HandleAsync + SaveChangesAsync` in a single max-1-retry loop: on a unique-violation the first attempt clears the tracker, re-runs the (pure, side-effect-free) reaction, and saves again. The second attempt propagates normally on any failure.

**Tech Stack:** .NET 8 / C#, EF Core (`ChangeTracker.Clear()`), Npgsql (`PostgresException.SqlState == "23505"`), xUnit + Moq + FluentAssertions.

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventHandler.cs` |
| Modify (tests) | `backend/test/Anela.Heblo.Tests/Features/Smartsupp/ProcessWebhookEventHandlerTests.cs` |

---

### Task 1: Write failing tests for retry behaviour

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/ProcessWebhookEventHandlerTests.cs`

- [ ] **Step 1: Add two private helpers for constructing `DbUpdateException` with Postgres inner exceptions**

  Open `ProcessWebhookEventHandlerTests.cs` and add these two helpers inside the `ProcessWebhookEventHandlerTests` class, below the existing `CreateHandler` method:

  ```csharp
  private static Microsoft.EntityFrameworkCore.DbUpdateException MakeUniqueViolation() =>
      new("duplicate key", new Npgsql.PostgresException(
          "duplicate key value violates unique constraint",
          "ERROR",
          "ERROR",
          Npgsql.PostgresErrorCodes.UniqueViolation));

  private static Microsoft.EntityFrameworkCore.DbUpdateException MakeFkViolation() =>
      new("fk violation", new Npgsql.PostgresException(
          "foreign key violation",
          "ERROR",
          "ERROR",
          "23503"));
  ```

- [ ] **Step 2: Add the three new test methods**

  Add after the last existing `[Fact]` in the class:

  ```csharp
  [Fact]
  public async Task Handle_UniqueViolationOnFirstSave_RetriesOnce_AndReturnsHandled()
  {
      // Arrange
      var reaction = new Mock<ISmartsuppWebhookReaction>();
      reaction.Setup(r => r.EventName).Returns("conversation.opened");

      _repo.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .ThrowsAsync(MakeUniqueViolation())
          .Returns(Task.CompletedTask);

      var handler = CreateHandler(new[] { reaction.Object });

      // Act
      var response = await handler.Handle(MakeRequest("conversation.opened"), CancellationToken.None);

      // Assert
      response.Handled.Should().BeTrue();
      reaction.Verify(r => r.HandleAsync(It.IsAny<WebhookEventContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
      _repo.Verify(r => r.DiscardChanges(), Times.Once);
      _metrics.Verify(m => m.RecordReceived("conversation.opened", "handled", It.IsAny<double>()), Times.Once);
      _metrics.Verify(m => m.RecordReceived("conversation.opened", "error", It.IsAny<double>()), Times.Never);
  }

  [Fact]
  public async Task Handle_PersistentUniqueViolation_Rethrows_AfterOneRetry()
  {
      // Arrange
      var reaction = new Mock<ISmartsuppWebhookReaction>();
      reaction.Setup(r => r.EventName).Returns("conversation.opened");

      _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .ThrowsAsync(MakeUniqueViolation());

      var handler = CreateHandler(new[] { reaction.Object });

      // Act
      var act = async () => await handler.Handle(MakeRequest("conversation.opened"), CancellationToken.None);

      // Assert
      await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
      _repo.Verify(r => r.DiscardChanges(), Times.Once);
      _metrics.Verify(m => m.RecordReceived("conversation.opened", "error", It.IsAny<double>()), Times.Once);
      _metrics.Verify(m => m.RecordReceived("conversation.opened", "handled", It.IsAny<double>()), Times.Never);
  }

  [Fact]
  public async Task Handle_NonUniqueDbUpdateException_RethrowsImmediately_WithoutRetry()
  {
      // Arrange
      var reaction = new Mock<ISmartsuppWebhookReaction>();
      reaction.Setup(r => r.EventName).Returns("conversation.opened");

      _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .ThrowsAsync(MakeFkViolation());

      var handler = CreateHandler(new[] { reaction.Object });

      // Act
      var act = async () => await handler.Handle(MakeRequest("conversation.opened"), CancellationToken.None);

      // Assert
      await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
      _repo.Verify(r => r.DiscardChanges(), Times.Never);
      _metrics.Verify(m => m.RecordReceived("conversation.opened", "error", It.IsAny<double>()), Times.Once);
  }
  ```

- [ ] **Step 3: Verify the test file does not compile (expected — `DiscardChanges()` does not exist yet)**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riyadh-v1/backend
  dotnet build Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | grep -E "error|Error"
  ```

  Expected: build errors referencing `DiscardChanges` and possibly `SetupSequence`. This confirms the tests are driving the interface change.

---

### Task 2: Add `DiscardChanges()` to the interface and implement it

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

- [ ] **Step 1: Add `DiscardChanges()` to `ISmartsuppRepository`**

  In `ISmartsuppRepository.cs`, add the method after `SaveChangesAsync`:

  ```csharp
  Task SaveChangesAsync(CancellationToken cancellationToken);

  void DiscardChanges();
  ```

  The full interface method list after the edit (only the added line is new — all others already exist):
  ```csharp
  Task SaveChangesAsync(CancellationToken cancellationToken);

  void DiscardChanges();

  Task UpdateVisitorCacheAsync(
      string conversationId,
      ...
  ```

- [ ] **Step 2: Implement `DiscardChanges()` in `SmartsuppRepository`**

  In `SmartsuppRepository.cs`, add after the `SaveChangesAsync` implementation (currently the last method, line 274-275):

  ```csharp
  public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
      await _db.SaveChangesAsync(cancellationToken);

  public void DiscardChanges() => _db.ChangeTracker.Clear();
  ```

  `ChangeTracker.Clear()` detaches all tracked entities. After a failed `SaveChanges`, any entities in `Added` state remain tracked — clearing ensures the subsequent `reaction.HandleAsync` call performs a fresh query and re-adds from scratch rather than hitting EF's identity-map cache for the `Added` instance.

- [ ] **Step 3: Verify the solution compiles**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riyadh-v1/backend
  dotnet build
  ```

  Expected: build succeeds with no errors. The three new tests will still **fail** at runtime because the handler has not been changed yet.

- [ ] **Step 4: Run tests to confirm the new tests fail (red)**

  ```bash
  dotnet test --filter "FullyQualifiedName~Smartsupp" --no-build 2>&1 | tail -30
  ```

  Expected: 3 new tests fail, existing 5 tests pass.

---

### Task 3: Implement retry loop in `ProcessWebhookEventHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventHandler.cs`

- [ ] **Step 1: Replace the `try/catch` block in `Handle` with a retry loop**

  Current code (lines 50-65):
  ```csharp
  try
  {
      await reaction.HandleAsync(ctx, cancellationToken);
      await _repository.SaveChangesAsync(cancellationToken);
      _metrics.RecordReceived(ctx.EventName, "handled", sw.Elapsed.TotalMilliseconds);
      _logger.LogInformation("smartsupp webhook handled {Event} in {ElapsedMs}ms",
          ctx.EventName, (int)sw.Elapsed.TotalMilliseconds);
      return new ProcessWebhookEventResponse { Handled = true };
  }
  catch (Exception ex)
  {
      _metrics.RecordReceived(ctx.EventName, "error", sw.Elapsed.TotalMilliseconds);
      _logger.LogError(ex, "smartsupp webhook reaction failed for {Event} ({Reaction})",
          ctx.EventName, reaction.GetType().Name);
      throw;
  }
  ```

  Replace with:
  ```csharp
  for (var attempt = 0; ; attempt++)
  {
      try
      {
          await reaction.HandleAsync(ctx, cancellationToken);
          await _repository.SaveChangesAsync(cancellationToken);
          _metrics.RecordReceived(ctx.EventName, "handled", sw.Elapsed.TotalMilliseconds);
          _logger.LogInformation("smartsupp webhook handled {Event} in {ElapsedMs}ms",
              ctx.EventName, (int)sw.Elapsed.TotalMilliseconds);
          return new ProcessWebhookEventResponse { Handled = true };
      }
      catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsUniqueViolation(ex) && attempt == 0)
      {
          _logger.LogWarning(ex,
              "smartsupp webhook unique violation on {Event}, retrying once",
              ctx.EventName);
          _repository.DiscardChanges();
      }
      catch (Exception ex)
      {
          _metrics.RecordReceived(ctx.EventName, "error", sw.Elapsed.TotalMilliseconds);
          _logger.LogError(ex, "smartsupp webhook reaction failed for {Event} ({Reaction})",
              ctx.EventName, reaction.GetType().Name);
          throw;
      }
  }
  ```

- [ ] **Step 2: Add the `IsUniqueViolation` helper at the bottom of the class**

  After the existing `ClassifyUnhandled` method, add:
  ```csharp
  private static bool IsUniqueViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex) =>
      ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
  ```

  The full bottom of the class after the edit:
  ```csharp
      private static string ClassifyUnhandled(string eventName)
      {
          if (eventName.StartsWith("visitor.", StringComparison.Ordinal)) return "observed";
          if (eventName.StartsWith("app.", StringComparison.Ordinal)) return "ignored";
          return "unknown";
      }

      private static bool IsUniqueViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex) =>
          ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
  }
  ```

- [ ] **Step 3: Build and run all Smartsupp tests (green)**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riyadh-v1/backend
  dotnet build && dotnet test --filter "FullyQualifiedName~Smartsupp" --no-build
  ```

  Expected output: **8 tests pass, 0 fail** (5 existing + 3 new).

---

### Task 4: Full build, format, and commit

**Files:** all modified files from Tasks 1–3.

- [ ] **Step 1: Run `dotnet format` to normalise whitespace and satisfy the analyser**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riyadh-v1/backend
  dotnet format
  ```

  Expected: exits 0, may reformat minor whitespace.

- [ ] **Step 2: Run full build to confirm no regressions**

  ```bash
  dotnet build
  ```

  Expected: build succeeds, 0 errors.

- [ ] **Step 3: Run the complete Smartsupp test suite one final time**

  ```bash
  dotnet test --filter "FullyQualifiedName~Smartsupp" --no-build
  ```

  Expected: 8 tests pass.

- [ ] **Step 4: Commit**

  ```bash
  git add \
    backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs \
    backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs \
    backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventHandler.cs \
    backend/test/Anela.Heblo.Tests/Features/Smartsupp/ProcessWebhookEventHandlerTests.cs
  git commit -m "fix: retry on duplicate-key race in Smartsupp webhook handler

  On concurrent events for a new conversation, two requests both read
  'no existing row' and both INSERT, causing a 23505 unique violation on
  PK_SmartsuppConversations. Add DiscardChanges() to ISmartsuppRepository
  (backed by ChangeTracker.Clear()) and wrap the reaction+save path in a
  max-1-retry loop: on a unique violation the handler clears tracked state,
  re-runs the pure reaction (which now finds the committed row and takes
  the update branch), and saves again. Second failure propagates normally."
  ```
