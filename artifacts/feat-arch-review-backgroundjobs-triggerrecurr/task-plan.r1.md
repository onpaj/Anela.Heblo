# Distinct Error Codes for `TriggerRecurringJob` Failure Modes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single `RecurringJobNotFound` code that `TriggerRecurringJobHandler` returns for three distinct failure conditions with three distinct codes, and route the controller's failure path through the existing attribute-based HTTP-status mapper so `404` / `409` / `500` are returned correctly.

**Architecture:** Two new `ErrorCodes` enum members (`RecurringJobDisabled = 1904`, `RecurringJobEnqueueFailed = 1905`), each annotated with the appropriate `[HttpStatusCode]` attribute. The handler picks the correct code per failure branch. The controller stops short-circuiting failures to `NotFound(...)` and instead delegates to the existing `BaseApiController.HandleResponse(...)` (which reflects the enum attribute) — matching the convention used by every sibling endpoint in the controller. The `Accepted(...)` (202) success path is preserved.

**Tech Stack:** .NET 8, C#, ASP.NET Core MVC, MediatR, xUnit, FluentAssertions, Moq.

---

## File Structure

| File | Responsibility | Change |
|------|----------------|--------|
| `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Single source of truth for error code + HTTP status pairing | Add two enum values with `[HttpStatusCode]` attributes |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs` | Maps handler failure conditions to error codes | Change two return branches; augment logs with `ErrorCode` structured property |
| `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` | Translates handler response to HTTP | Replace bespoke `NotFound(...)` short-circuit with `HandleResponse(response)` for the failure branch; update `[ProducesResponseType]` decorations |
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs` (new) | Pure unit tests for handler failure branches (mocked deps) | New file with three tests covering the three failure codes |
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs` | Tests controller's HTTP mapping for trigger endpoint | Rename the inaccurate existing failure test, add two new tests for `409` and `500` mappings |

No new files in `src/`. No new NuGet packages. No DB migrations. No frontend changes.

---

## Task 1: Add `RecurringJobDisabled` (1904) and `RecurringJobEnqueueFailed` (1905) to `ErrorCodes`

**Why first:** Subsequent tasks reference these enum members; the project must compile.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:201-207`

This is an additive, enabling change — no behavior changes yet, so no dedicated test is required. The new values are exercised transitively by the handler tests (Task 3) and controller tests (Task 5). The integer-value uniqueness and attribute presence are protected by the C# compiler (duplicate enum values are a compile error) and by the test assertions in Tasks 3/5.

- [ ] **Step 1: Verify the current state of the BackgroundJobs section**

Open `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` and locate the BackgroundJobs (19XX) section (lines 201–207). It currently ends with:

```csharp
// BackgroundJobs module errors (19XX)
[HttpStatusCode(HttpStatusCode.NotFound)]
RecurringJobNotFound = 1901,
[HttpStatusCode(HttpStatusCode.InternalServerError)]
RecurringJobUpdateFailed = 1902,
[HttpStatusCode(HttpStatusCode.BadRequest)]
InvalidCronExpression = 1903,
```

Confirm nothing currently uses `1904` or `1905` (a quick scan of the enum body around lines 201–230 is enough; `1904` would belong to KnowledgeBase 20XX or to BackgroundJobs — it is unused).

- [ ] **Step 2: Add the two new enum values**

Insert the following two entries immediately after `InvalidCronExpression = 1903,` and before the blank line that precedes the `// KnowledgeBase module errors (20XX)` comment:

```csharp
[HttpStatusCode(HttpStatusCode.Conflict)]
RecurringJobDisabled = 1904,
[HttpStatusCode(HttpStatusCode.InternalServerError)]
RecurringJobEnqueueFailed = 1905,
```

The complete BackgroundJobs section after the edit must read:

```csharp
// BackgroundJobs module errors (19XX)
[HttpStatusCode(HttpStatusCode.NotFound)]
RecurringJobNotFound = 1901,
[HttpStatusCode(HttpStatusCode.InternalServerError)]
RecurringJobUpdateFailed = 1902,
[HttpStatusCode(HttpStatusCode.BadRequest)]
InvalidCronExpression = 1903,
[HttpStatusCode(HttpStatusCode.Conflict)]
RecurringJobDisabled = 1904,
[HttpStatusCode(HttpStatusCode.InternalServerError)]
RecurringJobEnqueueFailed = 1905,
```

- [ ] **Step 3: Confirm the project builds**

Run from the repo root:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Errors`.

If the build fails with a duplicate-value error, search the file for the conflicting integer — the spec/arch-review have already verified `1904`/`1905` are free in the 19XX section, but the compiler will catch any collision elsewhere.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(backgroundjobs): add RecurringJobDisabled and RecurringJobEnqueueFailed error codes

Introduces ErrorCodes.RecurringJobDisabled (1904, Conflict) and
ErrorCodes.RecurringJobEnqueueFailed (1905, InternalServerError) so the
TriggerRecurringJob handler can distinguish disabled and enqueue-failure
conditions from a true \"not found\" outcome."
```

---

## Task 2: Add `TriggerRecurringJobHandler` unit tests for the three failure branches (RED)

**Why now:** TDD — write the failing tests before changing the handler. These three tests will fail because the handler currently returns `RecurringJobNotFound` for all three branches.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs`

This file uses Moq for `IRecurringJobStatusChecker` and `IHangfireJobEnqueuer` — fully isolated unit tests, no Hangfire infrastructure. The existing `TriggerRecurringJobHandlerIntegrationTests.cs` covers happy paths against real Hangfire and is not touched here.

- [ ] **Step 1: Create the new test file**

Create `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs` with the following content:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Pure unit tests for TriggerRecurringJobHandler failure branches.
/// Happy-path coverage lives in TriggerRecurringJobHandlerIntegrationTests.
/// </summary>
public class TriggerRecurringJobHandlerTests
{
    private static TriggerRecurringJobHandler CreateHandler(
        IEnumerable<IRecurringJob>? jobs = null,
        Mock<IRecurringJobStatusChecker>? statusChecker = null,
        Mock<IHangfireJobEnqueuer>? enqueuer = null)
    {
        return new TriggerRecurringJobHandler(
            jobs ?? Array.Empty<IRecurringJob>(),
            (statusChecker ?? new Mock<IRecurringJobStatusChecker>()).Object,
            (enqueuer ?? new Mock<IHangfireJobEnqueuer>()).Object,
            new Mock<ILogger<TriggerRecurringJobHandler>>().Object);
    }

    private static IRecurringJob CreateJob(string jobName)
    {
        var job = new Mock<IRecurringJob>();
        job.SetupGet(j => j.Metadata).Returns(new RecurringJobMetadata
        {
            JobName = jobName,
            DisplayName = jobName,
            Description = "test",
            CronExpression = "0 0 * * *"
        });
        return job.Object;
    }

    [Fact]
    public async Task Handle_WhenJobIsNotRegistered_ReturnsRecurringJobNotFound()
    {
        // Arrange
        var handler = CreateHandler(jobs: Array.Empty<IRecurringJob>());
        var request = new TriggerRecurringJobRequest { JobName = "missing-job", ForceDisabled = false };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RecurringJobNotFound);
        response.Params.Should().ContainKey("jobName").WhoseValue.Should().Be("missing-job");
    }

    [Fact]
    public async Task Handle_WhenJobIsDisabledAndForceDisabledIsFalse_ReturnsRecurringJobDisabled()
    {
        // Arrange
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(x => x.IsJobEnabledAsync("disabled-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = CreateHandler(
            jobs: new[] { CreateJob("disabled-job") },
            statusChecker: statusChecker);

        var request = new TriggerRecurringJobRequest { JobName = "disabled-job", ForceDisabled = false };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RecurringJobDisabled);
        response.Params.Should().ContainKey("jobName").WhoseValue.Should().Be("disabled-job");
    }

    [Fact]
    public async Task Handle_WhenEnqueuerReturnsNull_ReturnsRecurringJobEnqueueFailed()
    {
        // Arrange
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(x => x.IsJobEnabledAsync("enabled-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var enqueuer = new Mock<IHangfireJobEnqueuer>();
        enqueuer
            .Setup(x => x.EnqueueJob(It.IsAny<IRecurringJob>(), It.IsAny<CancellationToken>()))
            .Returns((string?)null);

        var handler = CreateHandler(
            jobs: new[] { CreateJob("enabled-job") },
            statusChecker: statusChecker,
            enqueuer: enqueuer);

        var request = new TriggerRecurringJobRequest { JobName = "enabled-job", ForceDisabled = false };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RecurringJobEnqueueFailed);
        response.Params.Should().ContainKey("jobName").WhoseValue.Should().Be("enabled-job");
    }
}
```

- [ ] **Step 2: Run the new tests and verify they fail with the expected reasons**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs.TriggerRecurringJobHandlerTests" \
  --no-restore
```

Expected output:
- `Handle_WhenJobIsNotRegistered_ReturnsRecurringJobNotFound` → **PASS** (handler already returns `RecurringJobNotFound` for this branch — it is the only correct branch today).
- `Handle_WhenJobIsDisabledAndForceDisabledIsFalse_ReturnsRecurringJobDisabled` → **FAIL** (handler returns `RecurringJobNotFound` instead of `RecurringJobDisabled`).
- `Handle_WhenEnqueuerReturnsNull_ReturnsRecurringJobEnqueueFailed` → **FAIL** (handler returns `RecurringJobNotFound` instead of `RecurringJobEnqueueFailed`).

Two failing tests is the RED state we want. Do not commit yet — the next task fixes them.

---

## Task 3: Update `TriggerRecurringJobHandler` failure branches and logging (GREEN)

**Why now:** Make the failing tests pass and augment logs per FR-5.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs`

- [ ] **Step 1: Update the "disabled" branch (current lines 53–64) to return `RecurringJobDisabled` and include `ErrorCode` in the log**

Locate lines 53–64 in `TriggerRecurringJobHandler.cs`:

```csharp
if (!isEnabled)
{
    _logger.LogWarning("Job {JobName} is disabled. Use forceDisabled=true to trigger anyway.", request.JobName);
    return new TriggerRecurringJobResponse(
        ErrorCodes.RecurringJobNotFound,
        new Dictionary<string, string>
        {
            { "jobName", request.JobName },
            { "forceDisabled", request.ForceDisabled.ToString() }
        }
    );
}
```

Replace with:

```csharp
if (!isEnabled)
{
    _logger.LogWarning(
        "Job {JobName} is disabled. Use forceDisabled=true to trigger anyway. ErrorCode={ErrorCode}",
        request.JobName,
        (int)ErrorCodes.RecurringJobDisabled);
    return new TriggerRecurringJobResponse(
        ErrorCodes.RecurringJobDisabled,
        new Dictionary<string, string>
        {
            { "jobName", request.JobName },
            { "forceDisabled", request.ForceDisabled.ToString() }
        }
    );
}
```

- [ ] **Step 2: Update the "enqueue returned null" branch (current lines 70–81) to return `RecurringJobEnqueueFailed` and include `ErrorCode` in the log**

Locate lines 70–81:

```csharp
if (jobId == null)
{
    _logger.LogError("Failed to enqueue job {JobName}", request.JobName);
    return new TriggerRecurringJobResponse(
        ErrorCodes.RecurringJobNotFound,
        new Dictionary<string, string>
        {
            { "jobName", request.JobName },
            { "forceDisabled", request.ForceDisabled.ToString() }
        }
    );
}
```

Replace with:

```csharp
if (jobId == null)
{
    _logger.LogError(
        "Failed to enqueue job {JobName}. ErrorCode={ErrorCode}",
        request.JobName,
        (int)ErrorCodes.RecurringJobEnqueueFailed);
    return new TriggerRecurringJobResponse(
        ErrorCodes.RecurringJobEnqueueFailed,
        new Dictionary<string, string>
        {
            { "jobName", request.JobName },
            { "forceDisabled", request.ForceDisabled.ToString() }
        }
    );
}
```

- [ ] **Step 3: Augment the "not registered" branch (current lines 36–47) log with `ErrorCode` for consistency**

Locate lines 36–47:

```csharp
if (job == null)
{
    _logger.LogWarning("Job {JobName} not found in registered jobs", request.JobName);
    return new TriggerRecurringJobResponse(
        ErrorCodes.RecurringJobNotFound,
        new Dictionary<string, string>
        {
            { "jobName", request.JobName },
            { "forceDisabled", request.ForceDisabled.ToString() }
        }
    );
}
```

Replace with:

```csharp
if (job == null)
{
    _logger.LogWarning(
        "Job {JobName} not found in registered jobs. ErrorCode={ErrorCode}",
        request.JobName,
        (int)ErrorCodes.RecurringJobNotFound);
    return new TriggerRecurringJobResponse(
        ErrorCodes.RecurringJobNotFound,
        new Dictionary<string, string>
        {
            { "jobName", request.JobName },
            { "forceDisabled", request.ForceDisabled.ToString() }
        }
    );
}
```

The return value of this branch is unchanged. The error code stays `RecurringJobNotFound` — this is the only branch that should ever produce it.

- [ ] **Step 4: Re-run the handler tests and verify all three pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs.TriggerRecurringJobHandlerTests" \
  --no-restore
```

Expected: all 3 tests **PASS**.

- [ ] **Step 5: Re-run the existing integration tests to verify the happy path is intact**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs.TriggerRecurringJobHandlerIntegrationTests" \
  --no-restore
```

Expected: all 3 integration tests **PASS** (no behavior changes on the success path; the handler still calls `_jobEnqueuer.EnqueueJob` exactly the same way).

- [ ] **Step 6: Verify `RecurringJobNotFound` is now only referenced from the not-registered branch in the handler**

```bash
grep -n "RecurringJobNotFound" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/
```

Expected: exactly one match, in `TriggerRecurringJobHandler.cs` at the "not registered" branch (currently around line 41–44 after the edits above).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs
git commit -m "feat(backgroundjobs): distinguish disabled and enqueue-failure outcomes in TriggerRecurringJob handler

- Disabled branch now returns RecurringJobDisabled (1904) instead of RecurringJobNotFound
- Enqueue-null branch now returns RecurringJobEnqueueFailed (1905) instead of RecurringJobNotFound
- Not-registered branch is the sole producer of RecurringJobNotFound (1901)
- Failure logs now include the ErrorCode structured property
- New unit tests cover all three failure branches"
```

---

## Task 4: Update `RecurringJobsController.TriggerJob` to delegate failure mapping to `HandleResponse` (controller TDD — RED first)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs:100-121`

The existing controller test file already covers `RecurringJobNotFound → 404` (which keeps working — `BaseApiController.HandleResponse` returns `NotFoundObjectResult` for the `NotFound` status). It also has a misleadingly named test `TriggerJob_WhenTriggerFails_ShouldReturnBadRequest` that actually asserts `NotFoundObjectResult` for `RecurringJobUpdateFailed` — that test was masking the bug and must be updated.

- [ ] **Step 1: Rewrite the existing misleading test and add two new failure-mapping tests**

Open `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs`.

Replace the entire body of `TriggerJob_WhenTriggerFails_ShouldReturnBadRequest` (lines 115–140) with a renamed, accurate test, and append two new tests for `RecurringJobDisabled` and `RecurringJobEnqueueFailed`. The replacement code (drop into the class, after the existing `TriggerJob_WithNonExistentJobName_ShouldReturnNotFound` test and before `TriggerJob_ShouldPassCancellationTokenToMediator`):

```csharp
[Fact]
public async Task TriggerJob_WhenUpdateFailedErrorReturned_ShouldReturn500()
{
    // Arrange
    // RecurringJobUpdateFailed (1902) is annotated [HttpStatusCode(InternalServerError)].
    // After delegating failure mapping to BaseApiController.HandleResponse, this must
    // produce an ObjectResult with StatusCode 500 — not a 404 (which the old test
    // asserted, masking the controller bug).
    var jobName = "test-job";
    var errorResponse = new TriggerRecurringJobResponse(ErrorCodes.RecurringJobUpdateFailed);

    _mediatorMock
        .Setup(x => x.Send(
            It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(errorResponse);

    // Act
    var result = await _controller.TriggerJob(jobName);

    // Assert
    var objectResult = result.Result.Should().BeAssignableTo<ObjectResult>().Subject;
    objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    var returnedResponse = objectResult.Value.Should().BeAssignableTo<TriggerRecurringJobResponse>().Subject;
    returnedResponse.Success.Should().BeFalse();
    returnedResponse.ErrorCode.Should().Be(ErrorCodes.RecurringJobUpdateFailed);
}

[Fact]
public async Task TriggerJob_WhenJobIsDisabled_ShouldReturn409Conflict()
{
    // Arrange
    var jobName = "disabled-job";
    var errorResponse = new TriggerRecurringJobResponse(ErrorCodes.RecurringJobDisabled);

    _mediatorMock
        .Setup(x => x.Send(
            It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(errorResponse);

    // Act
    var result = await _controller.TriggerJob(jobName);

    // Assert
    var objectResult = result.Result.Should().BeAssignableTo<ObjectResult>().Subject;
    objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    var returnedResponse = objectResult.Value.Should().BeAssignableTo<TriggerRecurringJobResponse>().Subject;
    returnedResponse.Success.Should().BeFalse();
    returnedResponse.ErrorCode.Should().Be(ErrorCodes.RecurringJobDisabled);
}

[Fact]
public async Task TriggerJob_WhenEnqueueFails_ShouldReturn500InternalServerError()
{
    // Arrange
    var jobName = "test-job";
    var errorResponse = new TriggerRecurringJobResponse(ErrorCodes.RecurringJobEnqueueFailed);

    _mediatorMock
        .Setup(x => x.Send(
            It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(errorResponse);

    // Act
    var result = await _controller.TriggerJob(jobName);

    // Assert
    var objectResult = result.Result.Should().BeAssignableTo<ObjectResult>().Subject;
    objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    var returnedResponse = objectResult.Value.Should().BeAssignableTo<TriggerRecurringJobResponse>().Subject;
    returnedResponse.Success.Should().BeFalse();
    returnedResponse.ErrorCode.Should().Be(ErrorCodes.RecurringJobEnqueueFailed);
}
```

Notes on assertions:
- `BaseApiController.HandleResponse` returns `NotFoundObjectResult` for `NotFound`, but falls through to the generic `ObjectResult` (via `StatusCode((int)statusCode, response)`) for `Conflict` and `InternalServerError`. The tests therefore assert `ObjectResult` + `StatusCode == 409 / 500`, not `ConflictObjectResult` or any specialized type.
- The existing test `TriggerJob_WithNonExistentJobName_ShouldReturnNotFound` continues to assert `NotFoundObjectResult` and remains unchanged — that is the correct return type for the `NotFound` branch in `HandleResponse`.

- [ ] **Step 2: Run the controller tests and verify the three new/updated tests fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs.RecurringJobsControllerTriggerTests" \
  --no-restore
```

Expected:
- `TriggerJob_WithValidJobName_ShouldReturnAcceptedWithSuccessResponse` → **PASS** (success branch unchanged).
- `TriggerJob_WithNonExistentJobName_ShouldReturnNotFound` → **PASS** (controller currently returns `NotFound(response)` for all failures — happens to be correct for this code).
- `TriggerJob_WhenUpdateFailedErrorReturned_ShouldReturn500` → **FAIL** (controller returns `NotFoundObjectResult`, test expects `ObjectResult` with 500).
- `TriggerJob_WhenJobIsDisabled_ShouldReturn409Conflict` → **FAIL** (same reason; 404 vs 409).
- `TriggerJob_WhenEnqueueFails_ShouldReturn500InternalServerError` → **FAIL** (same reason; 404 vs 500).
- `TriggerJob_ShouldPassCancellationTokenToMediator` → **PASS** (unchanged).

Three failing tests is the RED state we want.

- [ ] **Step 3: Update `RecurringJobsController.TriggerJob` to delegate failure mapping**

Open `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` and locate the `TriggerJob` action (lines 100–121).

Replace lines 100–121 with:

```csharp
/// <summary>
/// Manually trigger a recurring job to run immediately
/// </summary>
/// <param name="jobName">The name of the job to trigger</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Background job ID if triggered successfully</returns>
[HttpPost("{jobName}/trigger")]
[ProducesResponseType(typeof(TriggerRecurringJobResponse), StatusCodes.Status202Accepted)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
[ProducesResponseType(StatusCodes.Status500InternalServerError)]
public async Task<ActionResult<TriggerRecurringJobResponse>> TriggerJob(
    string jobName,
    CancellationToken cancellationToken = default)
{
    var request = new TriggerRecurringJobRequest
    {
        JobName = jobName
    };

    var response = await _mediator.Send(request, cancellationToken);

    if (!response.Success)
    {
        return HandleResponse(response);
    }

    return Accepted(response);
}
```

Changes vs. the old version:
- Added `[ProducesResponseType(StatusCodes.Status409Conflict)]` and `[ProducesResponseType(StatusCodes.Status500InternalServerError)]` decorations.
- The failure branch now calls `HandleResponse(response)` (from `BaseApiController`) — reflection-based status mapping via the `[HttpStatusCode]` attribute on each `ErrorCodes` value.
- The success branch still returns `Accepted(response)` → HTTP `202` (preserving existing behavior and matching the `Status202Accepted` `[ProducesResponseType]` decoration).
- `HandleResponse<T>` returns `ActionResult<T>`; assigning it directly inside an `ActionResult<TriggerRecurringJobResponse>`-returning method works because `ActionResult<T>` has implicit conversion from itself. The C# compiler will accept `return HandleResponse(response);` — no cast needed.

- [ ] **Step 4: Re-run the controller tests and verify all six pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs.RecurringJobsControllerTriggerTests" \
  --no-restore
```

Expected: all 6 controller-trigger tests **PASS**.

- [ ] **Step 5: Run the broader BackgroundJobs test set to confirm nothing else regressed**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs" \
  --no-restore
```

Expected: all BackgroundJobs tests **PASS**. Pay particular attention to `RecurringJobsControllerTests.cs` (the other controller test file) — it covers the unchanged endpoints (`GetRecurringJobs`, `UpdateJobStatus`, `UpdateJobCron`) and must continue to pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs
git commit -m "feat(backgroundjobs): map TriggerJob failures via BaseApiController.HandleResponse

The TriggerJob endpoint previously short-circuited every handler failure to
HTTP 404 NotFound, masking 'disabled' and 'enqueue failed' outcomes. It now
delegates failure mapping to BaseApiController.HandleResponse, which reads
the [HttpStatusCode] attribute on each ErrorCodes value — matching the
convention already used by GetRecurringJobs, UpdateJobStatus, and UpdateJobCron
in the same controller.

- 404 NotFound for RecurringJobNotFound (1901)
- 409 Conflict for RecurringJobDisabled (1904)
- 500 InternalServerError for RecurringJobEnqueueFailed (1905) and any other failure

The 202 Accepted success path is preserved. ProducesResponseType decorations
updated to advertise the new statuses."
```

---

## Task 5: Full backend verification

**Files:** none modified.

This is the project-wide gate from `CLAUDE.md` ("Validation before completion"). No further code changes — only verification.

- [ ] **Step 1: Format**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: no formatting violations remain. If any files are reformatted, stage them and amend the most recent commit OR add a follow-up `chore: dotnet format` commit — do not skip formatting.

- [ ] **Step 2: Full backend build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded. 0 Errors`. Warnings present before the change should not increase.

- [ ] **Step 3: Full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all tests **PASS**. The change touches a single use case and one controller action; broader test impact is not expected, but the full run confirms no transitive coupling was missed.

- [ ] **Step 4: Final `grep` audit — exactly one production usage of `ErrorCodes.RecurringJobNotFound`**

```bash
grep -rn "ErrorCodes.RecurringJobNotFound" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/
```

Expected: exactly one match, in `TriggerRecurringJobHandler.cs` (the "not registered" branch). Any additional production-code matches indicate the refactor is incomplete.

- [ ] **Step 5: If anything was reformatted in Step 1, commit it**

```bash
git status
# If files were changed by dotnet format:
git add -A
git commit -m "chore: apply dotnet format after error-code refactor"
```

If nothing changed, skip this step.

---

## Self-Review

**Spec coverage check (against `spec.r1.md` + `arch-review.r1.md` amendments):**

| Requirement | Implemented in |
|-------------|----------------|
| FR-1 `RecurringJobDisabled = 1904` (amended from 1903) | Task 1 |
| FR-2 `RecurringJobEnqueueFailed = 1905` (amended from 1904) | Task 1 |
| FR-3 `RecurringJobNotFound` restricted to not-registered branch | Task 3 Step 6; Task 5 Step 4 (audit) |
| FR-4 HTTP status mapping (amended: via `[HttpStatusCode]` + `HandleResponse`, not switch) | Task 1 (attributes) + Task 4 (`HandleResponse` delegation) |
| FR-5 Logging continuity (severity preserved; `ErrorCode` added as structured property) | Task 3 Steps 1–3 |
| FR-6 Unit test coverage for three handler branches and three controller mappings | Task 2 (handler) + Task 4 (controller) |
| NFR-1 Performance: constant lookups, switch, log property — no measurable impact | No work item (architectural property of the chosen design) |
| NFR-2 Security: unchanged auth, no internal leaks beyond job id and code | No work item (no new error-message fields introduced) |
| NFR-3 Backward compatibility: response DTO shape unchanged; behavioral change in HTTP status called out in commit messages | Task 4 Step 6 commit body |
| NFR-4 Observability: three distinct buckets visible in dashboards | Task 1 + Task 3 |
| Arch-review amendment: success path is `202 Accepted` (not `200 OK`) | Task 4 Step 3 — `return Accepted(response);` preserved |
| Arch-review amendment: `[HttpStatusCode]` attribute approach replaces controller `switch` | Task 1 (attributes) + Task 4 Step 3 (controller delegation) |

**Placeholder scan:** All steps contain concrete code/commands and explicit expected outputs. No TBD, no "implement later", no "similar to Task N". Code blocks are full and self-contained.

**Type / signature consistency:**
- `ErrorCodes.RecurringJobDisabled` and `ErrorCodes.RecurringJobEnqueueFailed` — same names used across Tasks 1, 2, 3, 4.
- `TriggerRecurringJobResponse(ErrorCodes)` constructor — used identically in handler edits (Task 3) and controller tests (Task 4).
- `HandleResponse(response)` (lowercase `r`, single argument) — matches the actual signature on `BaseApiController` (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:28`).
- `BaseApiController.HandleResponse` does not have a dedicated `ConflictObjectResult` branch in its `switch`; for `Conflict` and `InternalServerError` it returns `StatusCode((int)statusCode, response)` which produces an `ObjectResult`. Tests assert on `ObjectResult` + `StatusCode == 409 / 500` accordingly (Task 4 Step 1) — confirmed against `BaseApiController.cs:45-54`.
- `IRecurringJob.Metadata` returns `RecurringJobMetadata` with `JobName`, `DisplayName`, `Description`, `CronExpression` — matches the test helper in Task 2 Step 1 and the existing usage in `TriggerRecurringJobHandlerIntegrationTests.cs:188-194`.

**Out-of-scope reminders (per arch-review):**
- The controller does not currently bind `ForceDisabled` from the request. The new `RecurringJobDisabled` code is therefore unreachable via HTTP today — only via in-process callers (e.g., the new unit tests) — until a follow-up adds `[FromQuery] bool forceDisabled = false` to `TriggerJob`. This is intentionally out of scope for this plan. Flag it in the PR description.
- The spec's NFR-3 / arch-review "Risks" call out the behavior change for any HTTP client that relied on `404` for any failure. This is intentional and must be highlighted in the PR description and release notes (see Task 4 Step 6 commit body).
