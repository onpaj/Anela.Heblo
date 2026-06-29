# Task Plan: Fix SaveChangesAsync Exception Swallowing in MarketingInvoiceImportService

## Overview

`MarketingInvoiceImportService.ImportAsync` silently swallows exceptions thrown by `SaveChangesAsync`. A batch-level DB flush failure is logged and counted as `Failed`, but the exception is consumed — so the calling Hangfire job sees success. The fix is a one-line `throw;` addition and a corresponding test update.

## File Map

| File | Action | Responsibility |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` | Modify | Production service — add `throw;` at end of SaveChangesAsync catch block (line 107) |
| `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | Modify | Rename one test that encodes the bug; add one test proving exception type is preserved |

---

## Tasks

### task: fix-service

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`

- [ ] **Step 1: Locate the SaveChangesAsync catch block**

  Open the service file. The batch-flush block runs from line 93 to line 109. The catch block looks like this (lines 100–108):

  ```csharp
          catch (Exception ex)
          {
              _logger.LogError(
                  ex,
                  "Failed to persist {Count} marketing transactions for {Platform}",
                  stagedCount, source.Platform);
              result.Failed += stagedCount;
              // result.Imported intentionally stays 0 — nothing was committed.
          }
  ```

- [ ] **Step 2: Add `throw;` after `result.Failed += stagedCount;`**

  Replace the catch block so it reads:

  ```csharp
          catch (Exception ex)
          {
              _logger.LogError(
                  ex,
                  "Failed to persist {Count} marketing transactions for {Platform}",
                  stagedCount, source.Platform);
              result.Failed += stagedCount;
              // result.Imported intentionally stays 0 — nothing was committed.
              throw;
          }
  ```

  The exact edit is: after line 106 (`result.Failed += stagedCount;`), before the closing `}` of the catch block, insert `throw;`. Use `throw;` (bare rethrow), not `throw ex;` — bare rethrow preserves the original stack trace.

- [ ] **Step 3: Verify the surrounding context is untouched**

  After the edit the full `if (stagedCount > 0)` block should look like this:

  ```csharp
      if (stagedCount > 0)
      {
          try
          {
              await _repository.SaveChangesAsync(ct);
              result.Imported = stagedCount;
          }
          catch (Exception ex)
          {
              _logger.LogError(
                  ex,
                  "Failed to persist {Count} marketing transactions for {Platform}",
                  stagedCount, source.Platform);
              result.Failed += stagedCount;
              // result.Imported intentionally stays 0 — nothing was committed.
              throw;
          }
      }
  ```

  The per-transaction catch block (lines 83–91) must remain unchanged — it intentionally swallows to keep the run going.

- [ ] **Step 4: Build**

  ```
  cd backend
  dotnet build
  ```

  Expected: build succeeds with no errors. The `throw;` statement in a catch block with a declared return type is valid because control never reaches `return result;` when the exception propagates — the compiler accepts this.

- [ ] **Step 5: Commit**

  ```
  git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs
  git commit -m "fix: rethrow SaveChangesAsync exception in MarketingInvoiceImportService"
  ```

---

### task: fix-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

- [ ] **Step 1: Rename and rewrite the bug-encoding test**

  The current test at line 130–164 is named `ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow`. It asserts `DoesNotThrow` (by simply awaiting without any throw assertion) — this is the bug encoded in the tests. It must be replaced entirely.

  **Current test (lines 130–164):**

  ```csharp
  [Fact]
  public async Task ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow()
  {
      // Arrange
      var from = new DateTime(2026, 4, 1);
      var to = new DateTime(2026, 4, 2);

      var transactions = new List<MarketingTransaction>
      {
          new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
          new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
      };

      _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
          .ReturnsAsync(transactions);

      _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(false);

      _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

      // The single post-loop flush fails — none of the staged records are persisted
      _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("flush failed"));

      // Act
      var result = await _service.ImportAsync(_mockSource.Object, from, to);

      // Assert
      Assert.Equal(0, result.Imported);
      Assert.Equal(0, result.Skipped);
      Assert.Equal(2, result.Failed);
      _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
  }
  ```

  **Replace with:**

  ```csharp
  [Fact]
  public async Task ImportAsync_FinalSaveChangesThrows_Rethrows()
  {
      // Arrange
      var from = new DateTime(2026, 4, 1);
      var to = new DateTime(2026, 4, 2);

      var transactions = new List<MarketingTransaction>
      {
          new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
          new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
      };

      _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
          .ReturnsAsync(transactions);

      _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(false);

      _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

      // The single post-loop flush fails — none of the staged records are persisted
      _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("flush failed"));

      // Act & Assert
      await Assert.ThrowsAsync<InvalidOperationException>(
          () => _service.ImportAsync(_mockSource.Object, from, to));

      _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
  }
  ```

  Note: the result assertions (`Assert.Equal(0, result.Imported)` etc.) are removed because `ImportAsync` throws — there is no return value to assert on.

- [ ] **Step 2: Add the exception-type-preservation test**

  After the renamed test, add a new `[Fact]` that proves `throw;` (not `throw ex;`) is in use. Add it as a new method at the end of the class, before the closing `}`:

  ```csharp
  [Fact]
  public async Task ImportAsync_FinalSaveChangesThrows_ExceptionTypeIsPreserved()
  {
      // Arrange
      var from = new DateTime(2026, 4, 1);
      var to = new DateTime(2026, 4, 2);

      var transactions = new List<MarketingTransaction>
      {
          new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
      };

      _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
          .ReturnsAsync(transactions);

      _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(false);

      _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

      _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("flush failed"));

      // Act
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => _service.ImportAsync(_mockSource.Object, from, to));

      // Assert — exception type propagates unchanged (proves `throw;` not `throw ex;`)
      Assert.IsType<InvalidOperationException>(ex);
      Assert.Equal("flush failed", ex.Message);
  }
  ```

  This test verifies:
  1. `InvalidOperationException` propagates (the exception is not wrapped or swallowed).
  2. The message is unchanged (the original exception object, not a re-thrown copy).

- [ ] **Step 3: Run only the marketing invoice tests**

  ```
  cd backend
  dotnet test --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"
  ```

  Expected output: all tests in the class pass. Specifically:
  - `ImportAsync_FinalSaveChangesThrows_Rethrows` — passes (throws as expected)
  - `ImportAsync_FinalSaveChangesThrows_ExceptionTypeIsPreserved` — passes
  - All other existing tests — still pass (per-transaction catch is unaffected)

- [ ] **Step 4: Run the full test suite**

  ```
  cd backend
  dotnet test
  ```

  Expected: all tests pass. No regressions.

- [ ] **Step 5: Format and commit**

  ```
  cd backend
  dotnet format
  git add backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
  git commit -m "test: update MarketingInvoiceImportServiceTests to assert rethrow behavior"
  ```

---

## Self-Review: Spec Coverage Check

| Requirement | Covered |
|---|---|
| FR-1: `throw;` added after `result.Failed += stagedCount;` | task: fix-service, Step 2 |
| FR-1: `LogError` still executes before rethrow | task: fix-service, Step 3 (block shown in full; LogError precedes throw) |
| FR-1: Per-transaction catch unaffected | task: fix-service, Step 3 (explicitly called out) |
| FR-2: Test renamed to `ImportAsync_FinalSaveChangesThrows_Rethrows` | task: fix-tests, Step 1 |
| FR-2: Uses `await Assert.ThrowsAsync<InvalidOperationException>(...)` | task: fix-tests, Step 1 |
| FR-2: Result assertions removed | task: fix-tests, Step 1 |
| FR-2: `SaveChangesAsync` mock verification `Times.Once` preserved | task: fix-tests, Step 1 |
| FR-3: Test named `ImportAsync_FinalSaveChangesThrows_ExceptionTypeIsPreserved` | task: fix-tests, Step 2 |
| FR-3: Verifies `InvalidOperationException` type propagates | task: fix-tests, Step 2 |
