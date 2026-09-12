### task: remove-dead-failed-write

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs:96-105`
- Test (no changes expected, run as verification): `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

- [ ] **Step 1: Confirm the existing test does not depend on the dead write**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests" -v minimal
```
Expected: all tests in `MarketingInvoiceImportServiceTests` PASS, including `ImportAsync_FinalSaveChangesThrows_Rethrows` (this test only asserts the exception type and that `SaveChangesAsync` was called once — it never reads `result.Failed`, confirming it is safe to delete the dead write without touching the test).

- [ ] **Step 2: Delete the dead assignment and its orphaned comment**

Current code in `MarketingInvoiceImportService.cs` (post-loop flush catch block):

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

Replace with:

```csharp
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist {Count} marketing transactions for {Platform}",
                    stagedCount, source.Platform);
                throw;
            }
```

Only these two lines (`result.Failed += stagedCount;` and the comment line immediately after it) are removed. The `_logger.LogError` call and `throw;` are untouched, character-for-character.

- [ ] **Step 3: Run the full MarketingInvoices test suite to verify no regression**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoices" -v minimal
```
Expected: PASS — all tests in both `MarketingInvoiceImportServiceTests` and `ImportMarketingInvoicesHandlerTests` succeed, with the same pass count as Step 1's baseline plus the handler tests. No test needs to change.

- [ ] **Step 4: Build and format-check the whole backend**

Run:
```bash
cd backend
dotnet build
dotnet format --verify-no-changes
```
Expected: build succeeds with no new warnings/errors; `dotnet format --verify-no-changes` reports no formatting differences (the edit only removes two lines already at the correct indentation level, so no reformatting should be triggered).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs
git commit -m "fix(marketing-invoices): remove dead result.Failed write in unconditional-throw catch block"
```
