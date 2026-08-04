# Design: Remove hidden `DateTime.UtcNow` from `RecurringJobConfiguration`

No UI is involved — this is a backend-only domain/application-layer refactor. The UX/UI section is omitted.

## Component design

### 1. `RecurringJobConfiguration` (domain entity) — becomes pure

**File:** `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`

Responsibility unchanged: validate and mutate job configuration state. The only change is that the entity no longer owns "what time is it" — every method that sets `LastModifiedAt` receives the timestamp as a parameter instead of calling `DateTime.UtcNow`.

Public constructor — new parameter `lastModifiedAt` appended after `lastModifiedBy` (matches the plan's placement, keeps existing named/positional call sites needing only one appended argument):

```csharp
public RecurringJobConfiguration(
    string jobName,
    string displayName,
    string description,
    string cronExpression,
    string timeZoneId,
    bool isEnabled,
    string lastModifiedBy,
    DateTime lastModifiedAt)
```
- Validation order and messages unchanged.
- Body: `LastModifiedAt = lastModifiedAt;` replaces `LastModifiedAt = DateTime.UtcNow;`.
- The private parameterless EF Core constructor is untouched.

Mutation methods — each gains a trailing `DateTime modifiedAt` parameter, `modifiedBy` stays first argument:

```csharp
public void Enable(string modifiedBy, DateTime modifiedAt)
public void Disable(string modifiedBy, DateTime modifiedAt)
public void UpdateCronExpression(string cronExpression, string modifiedBy, DateTime modifiedAt)
public void UpdateConfiguration(string displayName, string description, string cronExpression, string timeZoneId, string modifiedBy, DateTime modifiedAt)
```
- Validation logic (blank-field checks, `ValidationException`) is unchanged and still runs before any assignment.
- Each method's `LastModifiedAt = DateTime.UtcNow;` line is replaced with `LastModifiedAt = modifiedAt;`.
- No other property or invariant changes.

### 2. `RecurringJobSeeder` — passes wall-clock time explicitly

**File:** `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`

Startup-only; not worth injecting `TimeProvider`. Both call sites (constructor call in the `Select` projection, and the `UpdateConfiguration` call in the existing-job branch) pass `DateTime.UtcNow` as the new trailing argument. No signature or behavior change to `IRecurringJobSeeder`.

### 3. `UpdateRecurringJobStatusHandler` — clock ownership moves here

**File:** `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobStatus/UpdateRecurringJobStatusHandler.cs`

Adds a fourth constructor dependency, mirroring the existing pattern in `GetRecurringJobHandler`:

```csharp
private readonly TimeProvider _timeProvider;

public UpdateRecurringJobStatusHandler(
    ILogger<UpdateRecurringJobStatusHandler> logger,
    IRecurringJobConfigurationRepository repository,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider)
```

In `Handle`, before calling `Enable`/`Disable`:

```csharp
var now = _timeProvider.GetUtcNow().UtcDateTime;
if (request.IsEnabled) job.Enable(modifiedBy, now);
else job.Disable(modifiedBy, now);
```

No change to `UpdateRecurringJobStatusRequest`/`Response` shapes, error paths, or logging.

### 4. `UpdateRecurringJobCronHandler` — same pattern

**File:** `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs`

Same constructor addition (`TimeProvider timeProvider` as fifth parameter, after `scheduler`), same `_timeProvider.GetUtcNow().UtcDateTime` computed once per `Handle` call, passed into:

```csharp
job.UpdateCronExpression(request.CronExpression, modifiedBy, now);
```

No change to cron validation (`IsValidCronExpression`), scheduler notification, or response shape.

### 5. DI registration — no change

`TimeProvider.System` is already registered as a singleton (per plan-01.md, confirmed pattern from `GetRecurringJobHandler`'s existing working injection). Both handlers pick it up automatically through constructor injection; no `ServiceCollectionExtensions.cs` edit needed.

### 6. Test doubles — reuse the existing `Mock<TimeProvider>` pattern, no new package

`GetRecurringJobHandlerTests.cs` already establishes the pattern for controlling `TimeProvider` in this codebase:

```csharp
private readonly Mock<TimeProvider> _timeProviderMock;
_timeProviderMock = new Mock<TimeProvider>();
_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(FixedUtcNow); // DateTimeOffset
```

This resolves the plan's open question: use `Moq`'s `Mock<TimeProvider>` (already a project dependency, already used this exact way), not `Microsoft.Extensions.Time.Testing.FakeTimeProvider` — no new NuGet package is needed. `UpdateRecurringJobStatusHandlerTests` and `UpdateRecurringJobCronHandlerTests` add the same `_timeProviderMock` field, pass `_timeProviderMock.Object` into the handler constructor, and assert `result.LastModifiedAt` against the fixed `DateTime` (via `FixedUtcNow.UtcDateTime`) with exact equality instead of `BeCloseTo(..., TimeSpan.FromSeconds(5))`.

Entity-level tests (`RecurringJobConfigurationTests.cs` and any other test that constructs the entity or calls a mutation method directly — `RecurringJobStatusCheckerTests.cs`, `RecurringJobDiscoveryServiceTests.cs`, `RecurringJobConfigurationRepositoryTests.cs`, `GetRecurringJobHandlerTests.cs`, `GetRecurringJobsListHandlerTests.cs`, `RecurringJobSeederTests.cs`) don't need a `TimeProvider` mock at all — they pass a literal `DateTime` (a fixed constant or a `DateTime.UtcNow` captured once in the test body) directly as the new constructor/method argument.

## Data schemas

No database schema change: `LastModifiedAt` remains a plain `DateTime` column on `RecurringJobConfiguration`, populated the same way from the outside — only the write path (caller-supplied vs. entity-internal `UtcNow`) changes.

No HTTP contract change: `UpdateRecurringJobStatusRequest/Response`, `UpdateRecurringJobCronRequest/Response`, and `RecurringJobDto` are byte-for-byte unchanged — `LastModifiedAt` is still a `DateTime` field on the response, just now sourced from `_timeProvider.GetUtcNow().UtcDateTime` inside the handler instead of from the entity's internal clock call.

Entity method signatures (the only "interface" changing in this design):

| Member | Before | After |
|---|---|---|
| Constructor | `(jobName, displayName, description, cronExpression, timeZoneId, isEnabled, lastModifiedBy)` | `(..., lastModifiedBy, lastModifiedAt)` |
| `Enable` | `(modifiedBy)` | `(modifiedBy, modifiedAt)` |
| `Disable` | `(modifiedBy)` | `(modifiedBy, modifiedAt)` |
| `UpdateCronExpression` | `(cronExpression, modifiedBy)` | `(cronExpression, modifiedBy, modifiedAt)` |
| `UpdateConfiguration` | `(displayName, description, cronExpression, timeZoneId, modifiedBy)` | `(..., modifiedBy, modifiedAt)` |

Handler constructor signatures:

| Handler | Before | After |
|---|---|---|
| `UpdateRecurringJobStatusHandler` | `(logger, repository, currentUserService)` | `(logger, repository, currentUserService, timeProvider)` |
| `UpdateRecurringJobCronHandler` | `(logger, repository, currentUserService, scheduler)` | `(logger, repository, currentUserService, scheduler, timeProvider)` |

No event payloads are involved — this module has no domain events; the repository `UpdateAsync` call and Hangfire cron scheduling calls are unaffected.
