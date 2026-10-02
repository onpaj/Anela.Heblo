# FlexiBankStatementImportService coverage-gap tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit tests covering the three untested outcome paths of `FlexiBankStatementImportService.ImportStatementAsync` (success, explicit failure with/without a message, and the exception catch block) with zero change to its behavior.

**Architecture:** Mock the SUT's direct collaborator, `FlexiBankAccountClient`, by adding the `virtual` modifier to its `ImportStatementAsync` method (a one-line, behavior-preserving production change — required because it is currently a concrete non-virtual method Moq cannot intercept, and because the alternative of mocking only the underlying FlexiBee SDK interface was tried and empirically falsified: `FlexiBankAccountClient` swallows every exception internally and never rethrows, so that seam can never reach `FlexiBankStatementImportService`'s own catch block). Full rationale in `artifacts/feat-4341/arch-review.r1.md` Decision 1.

**Tech Stack:** .NET 8, xUnit, Moq 4.20.70, FluentAssertions 6.12.0 (all already referenced by `Anela.Heblo.Adapters.Flexi.Tests.csproj`).

---

## Known pre-existing blocker (read before starting — not part of this task)

`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51` currently fails to compile on `main` (confirmed: this feature branch already contains every commit on `origin/main`, and the error reproduces from a clean checkout with none of this task's changes applied):

```
error CS1503: Argument 1: cannot convert from
'System.Collections.Generic.List<Anela.Heblo.Domain.Features.BackgroundJobs.RecurringJobConfiguration>'
to 'Anela.Heblo.Domain.Features.BackgroundJobs.RecurringJobConfiguration'
```

Root cause: line 51 calls `HasSeededFieldsChanged(existing, config)`, passing the whole `existing` collection where the method (line 73) expects a single `RecurringJobConfiguration` — it should be `HasSeededFieldsChanged(existingConfig, config)` (the loop variable declared two lines above, at line 50). This was introduced by PR #4324 and is completely unrelated to issue #4341.

`Anela.Heblo.Adapters.Flexi.Tests.csproj` references `Anela.Heblo.Application`, so **this pre-existing error blocks `dotnet build`/`dotnet test` for this task's own tests**, through no fault of this task's changes.

**Before Step 1:** run `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` once.
- **If it now succeeds** (someone fixed it upstream since this plan was written): proceed with Step 1 normally, ignore the rest of this section.
- **If it still fails with the CS1503 above:** this task cannot be validated (built/tested) without that one-line fix landing first. Apply it as its **own separate, clearly-labeled commit** before Step 1 — do not fold it into any commit for this task's own changes, and call it out explicitly as a pre-existing, unrelated fix in the commit message:

```bash
# In backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs
# line 51, change:
#     else if (HasSeededFieldsChanged(existing, config))
# to:
#     else if (HasSeededFieldsChanged(existingConfig, config))
```

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs
git commit -m "fix: RecurringJobSeeder passes wrong argument to HasSeededFieldsChanged

Pre-existing build break on main (introduced by #4324), unrelated to
#4341 — fixed here only because it blocks building/testing this
feature branch at all."
```

---

### task: bank-statement-import-service-tests

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankAccountClient.cs:21` (add `virtual`)
- Create: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Bank/FlexiBankStatementImportServiceTests.cs`

- [ ] **Step 1: Make `FlexiBankAccountClient.ImportStatementAsync` mockable**

Change line 21 of `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankAccountClient.cs` from:

```csharp
    public async Task<Result<bool>> ImportStatementAsync(int accountId, string aboData)
```

to:

```csharp
    public virtual async Task<Result<bool>> ImportStatementAsync(int accountId, string aboData)
```

No other line in this file changes.

- [ ] **Step 2: Write the test file with all four scenarios**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Bank/FlexiBankStatementImportServiceTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Bank;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.BankAccounts;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Bank;

public class FlexiBankStatementImportServiceTests
{
    private readonly Mock<FlexiBankAccountClient> _mockFlexiBankAccountClient;
    private readonly FlexiBankStatementImportService _sut;

    public FlexiBankStatementImportServiceTests()
    {
        _mockFlexiBankAccountClient = new Mock<FlexiBankAccountClient>(
            Mock.Of<IBankAccountClient>(),
            Mock.Of<ILogger<FlexiBankAccountClient>>());

        _sut = new FlexiBankStatementImportService(
            _mockFlexiBankAccountClient.Object,
            Mock.Of<ILogger<FlexiBankStatementImportService>>());
    }

    [Fact]
    public async Task ImportStatementAsync_WhenClientReturnsSuccess_ReturnsSuccessResult()
    {
        _mockFlexiBankAccountClient
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success(true));

        var result = await _sut.ImportStatementAsync(1, "statement-data");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ImportStatementAsync_WhenClientReturnsFailureWithMessage_ReturnsSameFailureMessage()
    {
        _mockFlexiBankAccountClient
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<bool>("some FlexiBee error"));

        var result = await _sut.ImportStatementAsync(1, "statement-data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("some FlexiBee error");
    }

    [Fact]
    public async Task ImportStatementAsync_WhenClientReturnsFailureWithNullMessage_FallsBackToUnknownImportError()
    {
        _mockFlexiBankAccountClient
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<bool>(null!));

        var result = await _sut.ImportStatementAsync(1, "statement-data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Unknown import error");
    }

    [Fact]
    public async Task ImportStatementAsync_WhenClientThrows_ReturnsFailureWithExceptionMessageAndDoesNotThrow()
    {
        _mockFlexiBankAccountClient
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _sut.ImportStatementAsync(1, "statement-data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Exception during import: boom");
    }
}
```

Note on `Result.Failure<bool>(null!)` in the third test: `Anela.Heblo.Domain.Shared.Result<T>.ErrorMessage` is `string?` and its private constructor stores whatever is passed with no runtime validation — the `null!` null-forgiving operator only suppresses the harmless nullable-reference compiler warning on the `Failure(string errorMessage)` parameter (`backend/src/Anela.Heblo.Domain/Shared/Result.cs`); it does not change runtime behavior.

- [ ] **Step 3: Run the new tests and verify all four pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~FlexiBankStatementImportServiceTests"`

Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`

If any test fails, do not change the assertions to match — re-read `FlexiBankStatementImportService.cs` and `FlexiBankAccountClient.cs` and find the actual discrepancy; all four expected values above (`"Unknown import error"`, `"Exception during import: boom"`, etc.) were derived directly from the current source and are correct as written.

- [ ] **Step 4: Run `dotnet format` and the full adapter test project**

Run:
```bash
cd backend
dotnet format --no-restore
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
```

Expected: `dotnet format` makes no changes to the two files touched by this task (or only whitespace normalization); the full adapter test project passes with no new failures introduced.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankAccountClient.cs backend/test/Anela.Heblo.Adapters.Flexi.Tests/Bank/FlexiBankStatementImportServiceTests.cs
git commit -m "test: cover FlexiBankStatementImportService failure and exception paths

Adds unit tests for the three previously-untested outcome paths of
ImportStatementAsync: explicit FlexiBee failure (with and without an
ErrorMessage), and the catch-all exception path. FlexiBankAccountClient's
ImportStatementAsync is marked virtual (no behavior change) so Moq can
intercept it directly, per arch-review.r1.md Decision 1.

Closes #4341"
```

## Self-Review

**1. Spec coverage:** FR-1 (success) → test 1. FR-2 (explicit failure with message) → test 2. FR-3 (null message fallback) → test 3. FR-4 (exception path) → test 4. NFR-1 (no real network calls) → satisfied, `FlexiBankAccountClient` is entirely mocked, `IBankAccountClient` never touched. NFR-2 (no production behavior change beyond an architecture-sanctioned minimal mechanical change) → satisfied by Step 1, justified in `arch-review.r1.md` Decision 1. No spec requirement is without a task.

**2. Placeholder scan:** No TBD/TODO, no "add appropriate error handling"-style steps — every step has complete, runnable code and exact expected output.

**3. Type consistency:** `FlexiBankStatementImportService`, `FlexiBankAccountClient`, `Result<bool>` / `Result.Success` / `Result.Failure<bool>`, and `IBankAccountClient` are used identically to their actual current signatures in the codebase (`ImportStatementAsync(int accountId, string aboData)` returns `Task<Result<bool>>`; verified by reading `FlexiBankStatementImportService.cs`, `FlexiBankAccountClient.cs`, `Result.cs` directly).

**4. Empirical verification during planning:** This plan's Step 1 change and Step 2 test file were applied temporarily against the real codebase to confirm they compile and pass — they initially could not even build, due entirely to the pre-existing `RecurringJobSeeder.cs` blocker documented above (confirmed unrelated: reproduces from a clean checkout with none of this task's changes applied, and the branch already contains every commit on `origin/main`). With that one-line pre-existing bug also patched locally (temporarily, not committed), `dotnet test ... --filter "FullyQualifiedName~FlexiBankStatementImportServiceTests"` reported `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`. All temporary changes (Step 1, the test file, and the local `RecurringJobSeeder.cs` patch) were reverted before this plan was finalized, so `git status` is clean and the implementing stage performs the actual work from scratch, guided by the "Known pre-existing blocker" section above.
