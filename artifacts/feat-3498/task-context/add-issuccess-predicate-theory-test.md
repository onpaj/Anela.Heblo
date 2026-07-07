# Task Plan: Unit test coverage for `StockUpOperationResult`

**Goal:** Add a focused xUnit + FluentAssertions test suite for `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpOperationResult.cs`, covering all seven static factory methods and the `IsSuccess` computed property. This is a coverage-only change — no production code is modified.

**Tech stack:** xUnit (`[Fact]`), FluentAssertions (`.Should()`). No Moq needed (no dependencies to mock). `Xunit` is globally usable in the test project (`<Using Include="Xunit" />` in `Anela.Heblo.Tests.csproj`), so no `using Xunit;` line is required, but sibling files include it explicitly for clarity — this plan follows that same convention.

**New file:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/StockUpOperationResultTests.cs`
**Namespace:** `Anela.Heblo.Tests.Features.Catalog.Services`
**Test command (run from repo root of the worktree):**
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockUpOperationResultTests"
```

**Key facts pinned from source inspection (do not re-derive, use verbatim):**
- `StockUpOperationResult` (`backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpOperationResult.cs`): properties `Status` (`StockUpResultStatus`), `Message` (`string`), `Operation` (`StockUpOperation?`), `Exception` (`Exception?`), computed `IsSuccess` (true only for `Success`, `AlreadyCompleted`, `AlreadyInShoptet`).
- `StockUpOperation` (`backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs`): public constructor `StockUpOperation(string documentNumber, string productCode, int amount, StockUpSourceType sourceType, int sourceId)`. Throws `ValidationException` if `documentNumber`/`productCode` empty or `amount == 0`. Default post-construction `State` is `StockUpOperationState.Pending`. `ErrorMessage` has a private setter, populated only via `MarkAsFailed(DateTime timestamp, string errorMessage)`.
- `StockUpSourceType` (`backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpSourceType.cs`): enum values `TransportBox = 0`, `GiftPackageManufacture = 1`.
- `StockUpOperationState` (`backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperationState.cs`): `Pending`, `Submitted`, `Completed`, `Failed`.
- Exact factory message strings (verified from source):
  - `Success` → `"Stock up operation completed successfully"`
  - `AlreadyCompleted` → `"Operation already completed"`
  - `PreviouslyFailed` → `$"Operation previously failed: {operation.ErrorMessage}"`
  - `InProgress` → `$"Operation already in progress (state: {operation?.State})"`
  - `AlreadyInShoptet` → `"Document already exists in Shoptet history"`
  - `SubmitFailed` → `$"Submit failed: {ex.Message}"`
  - `VerificationFailed` → `"Verification failed: Record not found in Shoptet history after submission"`
  - `VerificationError` → `$"Verification error: {ex.Message}"`

---

---

### task: add-issuccess-predicate-theory-test

Add a dedicated test (FR-1) that pins, in one self-contained place, which `StockUpResultStatus` values make `IsSuccess` true vs. false — built from the same factory methods used in the previous task, kept as a distinct test so the "current set of success statuses" reads clearly to a future maintainer (per the architecture review's Decision 1 and the design doc's "Test case shape" section).

**Step 1 — Add the test method**

Edit `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/StockUpOperationResultTests.cs` and add the following method as the last member of the `StockUpOperationResultTests` class (insert immediately before the closing `}` of the class, after `VerificationError_WithOperationAndException_ReturnsFailedResult`):

```csharp

    [Fact]
    public void IsSuccess_ReturnsExpectedValue_ForEachStatus()
    {
        // Arrange: one factory call per StockUpResultStatus value, paired with the
        // expected IsSuccess outcome. This is the single place that pins "the current
        // set of success statuses" (Success, AlreadyCompleted, AlreadyInShoptet).
        var operation = CreateOperation();
        var cases = new (StockUpOperationResult Result, bool ExpectedIsSuccess)[]
        {
            (StockUpOperationResult.Success(operation), true),
            (StockUpOperationResult.AlreadyCompleted(operation), true),
            (StockUpOperationResult.AlreadyInShoptet(operation), true),
            (StockUpOperationResult.InProgress(operation), false),
            (StockUpOperationResult.PreviouslyFailed(operation), false),
            (StockUpOperationResult.SubmitFailed(operation, new InvalidOperationException("boom")), false),
        };

        // Act & Assert
        foreach (var (result, expectedIsSuccess) in cases)
        {
            result.IsSuccess.Should().Be(expectedIsSuccess,
                $"because Status={result.Status} should yield IsSuccess={expectedIsSuccess}");
        }
    }
```

Note: `cases` covers all six `StockUpResultStatus` enum values (`Success`, `AlreadyCompleted`, `AlreadyInShoptet`, `InProgress`, `PreviouslyFailed`, and `Failed` via `SubmitFailed`) exactly as required by FR-1 — `SubmitFailed` is used as the representative `Failed`-status case since `VerificationFailed`/`VerificationError` already cover the same `Status` value in the per-factory tests from the previous task.

**Step 2 — Run the full test file and confirm all tests pass**

Run:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockUpOperationResultTests"
```
Expected: build succeeds, 10 tests discovered and passed (the 9 from the previous task plus `IsSuccess_ReturnsExpectedValue_ForEachStatus`).

**Step 3 — Run the full test project as a final sanity check**

Run:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: no regressions — all previously-passing tests in the project still pass; the new file only adds tests, it does not touch any shared fixtures or production code.

**Step 4 — Commit**

```
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Services/StockUpOperationResultTests.cs
git commit -m "test: pin IsSuccess predicate coverage across all StockUpResultStatus values"
```
