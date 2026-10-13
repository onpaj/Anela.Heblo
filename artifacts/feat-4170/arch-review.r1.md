# Architecture Review: Unit test coverage for DailyInvoiceImportJobBase

## Skip Design: true

This is a backend-only, test-only change with no new or changed UI, API surface, data model, or user-facing behavior. There is nothing for a design document to specify beyond what the spec already covers.

## Architectural Fit Assessment
This work adds no new components — it fills a coverage gap in an existing, already-shipped Vertical Slice (`Features/Invoices/Infrastructure/Jobs`). The codebase already has an established, directly analogous pattern for testing this exact shape of class: `Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`, an abstract `IRecurringJob` base class with the same disabled-guard / try-catch-rethrow structure, is tested by `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs` via a private nested test-double subclass. There is no architectural decision to make here beyond "follow that existing pattern" — introducing a different testing strategy (e.g., testing only through the concrete `DailyInvoiceImportEurJob`/`DailyInvoiceImportCzkJob` subclasses, or adding a new abstraction) would create inconsistency with zero benefit.

The main integration point is the test project's existing structure: `backend/test/Anela.Heblo.Tests/Features/Invoices/` exists but has no `Infrastructure/Jobs/` subfolder yet (unlike `Features/Bank/Infrastructure/Jobs/`). Creating that subfolder mirrors the Bank feature's layout and keeps the module's test tree parallel to its source tree, consistent with `docs/architecture/filesystem.md` conventions (source and test trees mirror each other per-feature).

## Proposed Architecture

### Component Overview

```
backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Jobs/   <- NEW folder
  └── DailyInvoiceImportJobBaseTests.cs                                  <- NEW file (only new artifact)

exercises (unchanged, production code):
  Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBase.cs
    ├── depends on IInvoiceImportService        (mocked)
    ├── depends on IRecurringJobStatusChecker    (mocked)
    └── depends on ILoggerFactory → ILogger      (NullLoggerFactory, or Mock<ILogger> for the warning-assertion test)
```

No production code is created, modified, or deleted. The only new artifact is one test file (plus its private nested test-double class, scoped inside that file — no new public type).

### Key Design Decisions

#### Decision 1: Test the abstract base class via a private nested test-double subclass
**Options considered:**
1. Test through the real `DailyInvoiceImportEurJob`/`DailyInvoiceImportCzkJob` classes directly.
2. Define a minimal private nested subclass (`TestDailyInvoiceImportJob : DailyInvoiceImportJobBase`) inside the new test file, supplying fixed `Metadata`/`Currency`.
3. Introduce a new shared test-utility base class for "recurring job with disabled-guard" tests, reusable across features.

**Chosen approach:** Option 2 — one private nested test-double class, scoped to `DailyInvoiceImportJobBaseTests.cs`.

**Rationale:** This is exactly the pattern already in production use for the sibling `BankImportJobBaseTests.cs` (see `TestBankImportJob`), so it requires no new convention, no team discussion, and no precedent-setting. Option 1 would work but forces every test to be duplicated per currency (or arbitrarily picked from one), testing currency-specific wiring that isn't what's under test — the three behaviors in scope (disabled guard, partial-failure warning, exception rethrow) live entirely in the base class and are currency-agnostic. Option 3 is premature abstraction for two data points (Bank, Invoices) — YAGNI; if a third recurring-job-base-class test shows up wanting the same scaffolding, extract it then, not now.

#### Decision 2: Asserting the warning log (FR-2)
**Options considered:**
1. Inject `NullLoggerFactory.Instance` everywhere (as `BankImportJobBaseTests` does) and skip asserting the log content — only assert "does not throw" / "result reflects failures."
2. Inject a `Mock<ILoggerFactory>` returning a `Mock<ILogger>`, and verify `ILogger.Log(LogLevel.Warning, ...)` was called via Moq's standard extension-method-unwrapping verification.

**Chosen approach:** Option 2, but scoped to only the one test that needs it (partial-failure warning). All other tests keep using `NullLoggerFactory.Instance` for simplicity, matching `BankImportJobBaseTests`'s default.

**Rationale:** The issue explicitly calls out "the partial-failure warning branch when `result.Failed.Count > 0`" as an unexercised *branch* — line coverage on that branch is satisfied by simply reaching the `if (result.Failed.Count > 0)` block with a non-empty `Failed` list and asserting the method completes without throwing; asserting the warning log itself is a stronger, more valuable test but not strictly required to cover the line. Recommend doing it anyway since it is cheap (one `Mock<ILogger>` + one `Verify` call) and directly validates the behavior the issue describes ("a warning is logged and execution continues") rather than merely executing the line. Do not introduce a shared logging-assertion helper for this — it's one `Mock<ILogger>` setup, inline in the test, not worth abstracting.

#### Decision 3: One test class, one new folder — no changes to existing files
**Options considered:**
1. Add the new tests as a standalone file, changing nothing else.
2. Also add a lightweight integration test that resolves `DailyInvoiceImportEurJob`/`DailyInvoiceImportCzkJob` from DI to confirm they're wired correctly (constructor injection resolves).

**Chosen approach:** Option 1.

**Rationale:** DI wiring/registration for these jobs is already exercised elsewhere (`InvoicesModuleTests.cs`, `HangfireJobRegistrationHelperTests.cs`, `RecurringJobDiscoveryServiceTests.cs` per the file listing) — re-verifying it here would be scope creep against a task whose entire purpose is closing a specific, narrowly-described coverage gap in ~2h. Stay surgical.

## Implementation Guidance

### Directory / Module Structure
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBaseTests.cs`
- No other files are created, modified, or deleted.

### Interfaces and Contracts
No new interfaces or contracts. The test file consumes these existing types as-is (do not modify any of them):
- SUT: `Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs.DailyInvoiceImportJobBase`
- `Anela.Heblo.Application.Features.Invoices.Services.IInvoiceImportService` (mock)
- `Anela.Heblo.Application.Features.Invoices.Contracts.ImportResultDto` (construct directly with `Succeeded`/`Failed` lists)
- `Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery` (only needed if asserting on the query passed to `ImportInvoicesAsync`, which is optional — not required by the spec's FRs)
- `Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJobStatusChecker` (mock)
- `Anela.Heblo.Domain.Features.BackgroundJobs.RecurringJobMetadata` (construct directly for the test double's `Metadata`)

Test double shape (mirrors `BankImportJobBaseTests.TestBankImportJob`):
```csharp
private sealed class TestDailyInvoiceImportJob : DailyInvoiceImportJobBase
{
    public TestDailyInvoiceImportJob(
        IInvoiceImportService invoiceImportService,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker,
        string jobName,
        string currency)
        : base(invoiceImportService, loggerFactory, statusChecker)
    {
        Metadata = new RecurringJobMetadata
        {
            JobName = jobName,
            DisplayName = "Test Daily Invoice Import Job",
            Description = "Test job for DailyInvoiceImportJobBase",
            CronExpression = "0 0 * * *",
            DefaultIsEnabled = true,
        };
        Currency = currency;
    }

    public override RecurringJobMetadata Metadata { get; }
    protected override string Currency { get; }
}
```
Note: `Currency` is a `protected abstract string` *property*, so it can be overridden as an auto-property set from the constructor (as shown) — same technique the spec allows, and simpler than `BankImportJobBase`'s `GetTargetEndDate(DateTime today)` method-override because `Currency` has no parameters.

### Data Flow
1. Arrange: construct `Mock<IInvoiceImportService>`, `Mock<IRecurringJobStatusChecker>`, and either `NullLoggerFactory.Instance` or a `Mock<ILoggerFactory>`/`Mock<ILogger>` pair; construct `TestDailyInvoiceImportJob` with these.
2. Act: `await job.ExecuteAsync(CancellationToken.None)` (or, for FR-3, wrap the call in a lambda for FluentAssertions' `ThrowsAsync`).
3. Assert: per-FR as specified — `Times.Never` on the import call (FR-1), no-throw + optional log verify (FR-2), `ThrowsAsync<TException>` (FR-3).

No database, no HTTP, no Hangfire runtime involved — pure in-memory unit tests, consistent with `docs/architecture/testing-strategy.md`'s "70% unit tests, fast, isolated" pyramid guidance and its explicit listing of "background jobs" business logic as required unit-test material.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `Currency` being a property (not a method) makes the override syntax subtly different from `BankImportJobBase`'s pattern, risking a copy-paste compile error | Low | Implementation guidance above shows the exact override syntax; verify with `dotnet build` before considering the task done (per CLAUDE.md validation requirements) |
| Moq's `ILogger.Log` verification requires unwrapping the extension method (`LogWarning` compiles to `ILogger.Log(LogLevel, EventId, TState, Exception, Func<TState,Exception,string>)`), which trips up developers unfamiliar with the pattern | Low | If no existing `ILogger` mock-verification helper exists elsewhere in the test project, use the standard Moq pattern: `mockLogger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once)`. The developer should grep the test project first (`grep -r "ILogger.Log(" backend/test`) for an existing helper before hand-rolling this. |
| New test folder needs a namespace matching its path (`Anela.Heblo.Tests.Features.Invoices.Infrastructure.Jobs`) to match project convention | Low | Confirmed convention from `BankImportJobBaseTests.cs`'s namespace (`Anela.Heblo.Tests.Features.Bank.Infrastructure.Jobs`) — mirror it exactly for the Invoices equivalent |

## Specification Amendments
None. The spec (`spec.r1.md`) is implementable as written; this review only pins down the exact test-double shape and file location.

## Prerequisites
None. No migrations, no config, no new packages — xUnit/Moq/FluentAssertions/`Microsoft.Extensions.Logging.Abstractions` are already referenced by the test project (confirmed via `BankImportJobBaseTests.cs`'s existing usings). Ready to implement immediately.
