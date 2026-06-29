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