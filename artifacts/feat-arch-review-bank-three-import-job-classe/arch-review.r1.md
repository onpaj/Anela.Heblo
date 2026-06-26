```markdown
# Architecture Review: Consolidate Bank Import Job Classes

## Skip Design: true

## Architectural Fit Assessment

The proposal aligns precisely with existing patterns:

- **Discovery plumbing already supports the design.** `ServiceCollectionExtensions.AddRecurringJobs` (line 378) filters with `t.IsClass && !t.IsAbstract && typeof(IRecurringJob).IsAssignableFrom(t)`. An `abstract BankImportJobBase : IRecurringJob` is automatically excluded — no registration changes needed.
- **Hangfire keys jobs by concrete CLR type + `JobName`.** Both are preserved per FR-4. The pattern is identical to what `RecurringJobDiscoveryService` and `HangfireJobRegistrationHelper` already consume.
- **`Features/{Feature}/Infrastructure/Jobs/`** is the established home for recurring jobs across the codebase (`ExpeditionList`, `Photobank`, `Catalog`, `KnowledgeBase`, …). New files belong there.
- **`IRecurringJobStatusChecker.IsJobEnabledAsync(string, CancellationToken = default)`** already exists. The fact that two of the three jobs drop the token is an unambiguous bug the refactor naturally fixes.
- **Internal value type for parameters** is a familiar pattern; the codebase does not impose `class`-over-`record` for non-DTO internal types (the CLAUDE.md rule targets OpenAPI-exposed DTOs only).

No friction with the existing architecture. The refactor is structurally inert from the Hangfire / DI / discovery perspective.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain.Features.BackgroundJobs
        IRecurringJob (interface)
                 ▲
                 │ implements
                 │
Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs
        BankImportJobBase (abstract)
                 │
                 │   • IMediator, ILogger, IRecurringJobStatusChecker
                 │   • Owns ExecuteAsync template
                 │   • Calls protected abstract GetParameters() once
                 ▲
                 │ extends
   ┌─────────────┼─────────────┐
   │             │             │
ComgateCzk    ComgateEur    ShoptetPay
ImportJob     ImportJob     ImportJob

  Each subclass:
    • static Metadata
    • ctor → base(...)
    • GetParameters() → new(BankAccountNames.X, dateFrom, dateTo)

Anela.Heblo.Application.Features.Bank
        BankAccountNames (static)
                 │
                 │ const string ComgateCzk, ComgateEur, ShoptetPayCzk
                 │
                 ▼ consumed by
        BankImportJobParameters.AccountName
                 │
                 ▼
        ImportBankStatementRequest.AccountName
                 │
                 ▼
        ImportBankStatementHandler → BankAccountSettings.Accounts[].Name
```

### Key Design Decisions

#### Decision 1: Inheritance (Template Method) over Composition

**Options considered:**
- A) `BankImportJobBase` abstract class with a template `ExecuteAsync` (spec proposal).
- B) A single concrete `ScheduledBankImportJob` parameterised via constructor + three DI registrations with distinct `RecurringJobMetadata`.
- C) Composition: a `BankImportJobRunner` service called by three thin job shells.

**Chosen approach:** A — abstract template method base class.

**Rationale:** The existing discovery code is hard-coded around concrete types (`typeof(IRecurringJob)`, `MakeGenericMethod(jobType)`, type-keyed Hangfire registration). B requires modifying `AddRecurringJobs` and `HangfireJobRegistrationHelper` — explicitly out of scope per the spec and a much larger blast radius. C would still need three concrete `IRecurringJob` types to satisfy discovery and adds an indirection without saving lines. A is the smallest change consistent with the existing plumbing.

#### Decision 2: `internal sealed record` for `BankImportJobParameters`

**Options considered:**
- A) `internal sealed class` with `required init` properties (spec hedge).
- B) `internal sealed record` with positional or `init` properties.

**Chosen approach:** B — `internal sealed record BankImportJobParameters(string AccountName, DateTime DateFrom, DateTime DateTo)`.

**Rationale:** This is a pure internal value type, not an OpenAPI DTO. The "DTOs are classes" rule in CLAUDE.md exists because NSwag mishandles `record` parameter order in generated clients — that risk does not apply here (the type is `internal` and never crosses the API boundary). `record` is idiomatic for an immutable value object, gets value equality for free (useful in tests), and aligns with the global C# coding-style rule ("Prefer `record` … for immutable value-like models"). The spec's hedge to allow `class` adds no safety — the type cannot leak through OpenAPI because it is `internal`.

#### Decision 3: Logger categorization — preserve per-subclass log category

**Options considered:**
- A) `ILogger<BankImportJobBase>` injected once.
- B) `ILoggerFactory` injected, base creates logger via `loggerFactory.CreateLogger(GetType())`.

**Chosen approach:** B.

**Rationale:** Current code uses `ILogger<ComgateCzkImportJob>` etc. The log *message format* is preserved by either option, but the log *category* (the source-context property emitted by `Microsoft.Extensions.Logging`) is what filters and observability rules key on. Option A silently collapses three categories into one (`BankImportJobBase`), which can break log queries / alerts. Option B preserves the existing concrete-type categorization. The cost is one extra DI parameter and a single `loggerFactory.CreateLogger(GetType())` call in the base constructor — negligible.

**Spec amendment:** FR-1 should require `ILoggerFactory` injection, with the per-subclass `ILogger` materialized via `CreateLogger(GetType())`. The acceptance criterion "log messages remain byte-identical" should be widened to include "log category equals `typeof(<ConcreteJob>).FullName`".

#### Decision 4: Single `GetParameters()` call per `ExecuteAsync` invocation

**Options considered:**
- A) Three abstract properties (`AccountName`, `DateFrom`, `DateTo`).
- B) One abstract method returning a value object (spec proposal).

**Chosen approach:** B.

**Rationale:** Spec is correct. Calling `DateTime.Today` independently for `DateFrom` and `DateTo` is theoretically broken across the midnight boundary (a 5 AM cron won't hit it, but the design should not embed the schedule's assumptions). One call materializes one immutable snapshot.

#### Decision 5: Placement of `BankAccountNames`

**Options considered:**
- A) `Features/Bank/BankAccountNames.cs` (feature root, alongside `BankModule.cs`, `BankMappingProfile.cs`).
- B) `Features/Bank/Infrastructure/Jobs/BankAccountNames.cs` (next to the jobs).
- C) `Features/Bank/BankConstants.cs` as a single feature-constants file with a `AccountNames` nested class.

**Chosen approach:** A.

**Rationale:** The conventional name `{Feature}Constants.cs` is reserved (per `docs/architecture/filesystem.md`) but the constants here are not generic feature constants — they are wire-contract identifiers for `BankAccountSettings.Accounts[].Name`. They are consumed by the jobs but are not *about* jobs; conceptually they belong to the Bank module's contract surface. Placing them at the feature root keeps them discoverable to any future code (handlers, validators, integration tests) that needs the same identifiers. B narrows visibility unnecessarily.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Bank/
├── BankAccountNames.cs                              [NEW]
└── Infrastructure/
    └── Jobs/
        ├── BankImportJobBase.cs                     [NEW]
        ├── BankImportJobParameters.cs               [NEW]   (internal sealed record)
        ├── ComgateCzkImportJob.cs                   [MODIFIED — slim]
        ├── ComgateEurImportJob.cs                   [MODIFIED — slim]
        └── ShoptetPayImportJob.cs                   [MODIFIED — slim]

backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/
├── BankImportJobBaseTests.cs                        [NEW]
├── ComgateCzkImportJobTests.cs                      [NEW]
├── ComgateEurImportJobTests.cs                      [NEW]
└── ShoptetPayImportJobTests.cs                      [NEW]
```

Test layout mirrors src/, matching the existing `Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs` precedent — not the flat `Features/Bank/*Tests.cs` shape used by older handler tests.

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application/Features/Bank/BankAccountNames.cs
namespace Anela.Heblo.Application.Features.Bank;

public static class BankAccountNames
{
    public const string ComgateCzk = "ComgateCZK";
    public const string ComgateEur = "ComgateEUR";
    public const string ShoptetPayCzk = "ShoptetPay-CZK";
}
```

```csharp
// Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobParameters.cs
namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

internal sealed record BankImportJobParameters(
    string AccountName,
    DateTime DateFrom,
    DateTime DateTo);
```

```csharp
// Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs
namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public abstract class BankImportJobBase : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger _logger;

    public abstract RecurringJobMetadata Metadata { get; }

    protected BankImportJobBase(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(statusChecker);

        _mediator = mediator;
        _statusChecker = statusChecker;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    protected abstract BankImportJobParameters GetParameters();

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);

            var p = GetParameters();
            var request = new ImportBankStatementRequest(p.AccountName, p.DateFrom, p.DateTo);

            var response = await _mediator.Send(request, cancellationToken);

            _logger.LogInformation("{JobName} completed. Imported {Count} statements",
                Metadata.JobName, response.Statements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
```

Subclass shape (`ComgateCzkImportJob` shown — others identical structure):

```csharp
public sealed class ComgateCzkImportJob : BankImportJobBase
{
    public override RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-comgate-czk-import",
        DisplayName = "Daily Comgate CZK Import",
        Description = "Imports Comgate CZK payment statements from previous day",
        CronExpression = "30 4 * * *",
        DefaultIsEnabled = true,
    };

    public ComgateCzkImportJob(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker)
        : base(mediator, loggerFactory, statusChecker)
    {
    }

    protected override BankImportJobParameters GetParameters()
    {
        var yesterday = DateTime.Today.AddDays(-1);
        return new BankImportJobParameters(BankAccountNames.ComgateCzk, yesterday, yesterday);
    }
}
```

Mark subclasses `sealed` — they are not designed for further extension and `sealed` is the C# style default.

### Data Flow

1. Hangfire scheduler fires the recurring job at its cron time.
2. Hangfire resolves the concrete `ComgateCzkImportJob` (etc.) from the DI scope.
3. `ExecuteAsync` (defined on base) runs.
4. Base calls `_statusChecker.IsJobEnabledAsync(jobName, ct)` — early-return on disabled.
5. Base calls `GetParameters()` exactly once on the subclass → returns `BankImportJobParameters` snapshot.
6. Base constructs `ImportBankStatementRequest` from the snapshot and sends via MediatR, passing `ct`.
7. `ImportBankStatementHandler` consumes the request, resolving `AccountName` against `BankAccountSettings.Accounts[].Name`.
8. Response statements count is logged with the concrete job's log category.

No change to the data flow shape — only structural ownership of each step.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Discovery scan accidentally registers `BankImportJobBase` (e.g., if `abstract` is removed during a future edit) — would cause Hangfire to attempt instantiation of an abstract type and crash startup. | Medium | Existing filter `!t.IsAbstract` blocks this. Add a discovery-level test (FR-5 #3) that asserts the count of registered `IRecurringJob` services equals the expected concrete count and that no base type leaks in. Compile-time safety: `BankImportJobBase` has no parameterless or public ctor — only `protected`. |
| Log category change from `ILogger<ConcreteJob>` to `ILogger<BankImportJobBase>` breaks downstream log filters / alerts. | Medium | Decision 3 — inject `ILoggerFactory` and use `CreateLogger(GetType())` to preserve per-subclass categorization. |
| Hardcoded account-name strings still drift from `appsettings.*.json` (the brief notes local dev uses `AccountCZK`/`AccountEUR`). The refactor does not solve this; it only consolidates the magic strings into constants. | Medium | Acknowledge as out of scope per spec. Recommend a follow-up: startup-time validator that asserts every `BankAccountNames` constant matches some configured `BankAccountSettings.Accounts[].Name` and logs a fail-fast error otherwise. Track separately. |
| Subclass forgets to pass dependencies to `base(...)` and the file still compiles (because of default values on optional ctor params). | Low | The base constructor takes all three required dependencies as non-optional. Forgetting to forward fails compilation. No mitigation needed. |
| `GetParameters()` capturing `DateTime.Today` across midnight rollover. | Low | Decision 4 — single call per invocation produces one snapshot. Tests assert `GetParameters()` is invoked exactly once. |
| Hidden override of `ExecuteAsync` in a subclass diverges from base. | Low | Base `ExecuteAsync` is not marked `virtual`, so subclasses cannot override it. Hiding via `new` is possible but flagged by analyzers and not present in the codebase pattern. Code review catches it. |
| Per-job date assertions flaky around midnight in tests. | Low | Use a tolerance of ±1 day in date assertions (matches spec FR-5 fallback) OR inject a `TimeProvider` for determinism. Spec defers `TimeProvider` adoption; tolerance is acceptable for this refactor. |
| `ILogger`/observability tools relying on the *exact* concrete generic type `ILogger<ComgateCzkImportJob>` (e.g., scope filters by closed generic). | Low | `CreateLogger(GetType())` yields the same category name string as `ILogger<ComgateCzkImportJob>` resolves to — they are equivalent at the category-name level. |

## Specification Amendments

1. **FR-1, dependency injection.** Replace `ILogger<BankImportJobBase>` with `ILoggerFactory`. The base creates a typed logger via `loggerFactory.CreateLogger(GetType())` so the log category matches the concrete subclass and observability tooling is unaffected. (Rationale: Decision 3.)
2. **FR-4 acceptance criteria.** Add: "Log category (i.e., `ILogger` source-context property) for each concrete job equals `typeof(<ConcreteJob>).FullName`, identical to current behavior."
3. **Data Model section.** Change `BankImportJobParameters` from `internal sealed class` to `internal sealed record BankImportJobParameters(string AccountName, DateTime DateFrom, DateTime DateTo)`. The "DTOs are classes" project rule applies only to OpenAPI-exposed contract DTOs (it exists to work around NSwag's record-parameter-order issue). This type is `internal` and never crosses the API boundary. (Rationale: Decision 2.)
4. **FR-2 / file layout.** Subclasses should be declared `sealed` (no design intent to extend further).
5. **FR-3 placement.** Place `BankAccountNames.cs` at `Features/Bank/BankAccountNames.cs` (feature root), not under `Infrastructure/`. (Rationale: Decision 5 — these are contract identifiers, not job infrastructure.)
6. **FR-5 #3 — discovery test acceptance.** The test should additionally assert that `BankImportJobBase` is not present in `GetServices<IRecurringJob>()` and that the total count of registered `IRecurringJob` services equals the count *before* the refactor (so unrelated jobs are not accidentally affected).
7. **FR-5 #2 — per-job parameter tests.** Add a guard against the existing wire-contract drift: each per-job test must assert `GetParameters().AccountName == BankAccountNames.<Constant>` *and* assert the constant's literal value (`"ComgateCZK"` / `"ComgateEUR"` / `"ShoptetPay-CZK"`). A future refactor that renames the constant value would then trip a test and force the developer to update `appsettings*.json` / Key Vault deliberately.

## Prerequisites

None. The refactor:

- Adds no new NuGet packages.
- Requires no database migration (Hangfire job storage is keyed on unchanged `JobName` + unchanged concrete CLR type).
- Requires no configuration change (`appsettings*.json`, Key Vault, `BankAccountSettings` shape unchanged).
- Requires no changes to `AddRecurringJobs`, `RecurringJobDiscoveryService`, or `HangfireJobRegistrationHelper` — the existing `!t.IsAbstract` filter already accommodates the new base class.
- Requires no changes to `ImportBankStatementRequest` or `ImportBankStatementHandler`.

Implementation can start immediately.
```