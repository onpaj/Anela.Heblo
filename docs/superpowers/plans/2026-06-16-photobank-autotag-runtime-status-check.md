# Unify PhotobankAutoTagJob enable/disable with IRecurringJobStatusChecker — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `PhotobankAutoTagJob`'s static `AutoTagOptions.Enabled` flag with a runtime check against `IRecurringJobStatusChecker`, matching the gating mechanism already used by `PhotobankIndexJob`.

**Architecture:** Thread `RecurringJobMetadata.DefaultIsEnabled` through a new optional `defaultIfMissing` parameter on `IRecurringJobStatusChecker.IsJobEnabledAsync` so the auto-tag job stays disabled even if its `RecurringJobConfigurations` row has not yet been seeded. Inject the checker into `PhotobankAutoTagJob`, gate `ExecuteAsync` only (keep `ExecuteForPhotosAsync` ungated for ad-hoc re-tags), and delete the now-redundant `Enabled` field and its `appsettings.json` entry.

**Tech Stack:** .NET 8, xUnit + FluentAssertions + Moq, MediatR + Hangfire, EF Core (PostgreSQL).

---

## Self-Review Notes (decisions baked in)

- **Parameter ordering deviation from arch review.** Arch review proposed `IsJobEnabledAsync(string jobName, bool defaultIfMissing = true, CancellationToken cancellationToken = default)`. That ordering would break every existing positional caller (e.g. `PhotobankIndexJob` calls `IsJobEnabledAsync(Metadata.JobName, cancellationToken)`). To keep all existing callers source-compatible, this plan places `defaultIfMissing` *after* `cancellationToken`. Non-conventional, but the minimal-impact correct choice.
- **Moq backward compatibility.** Existing `.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))` setups remain green because the C# compiler injects the literal `true` default for the new third argument at both setup site and production call site for current consumers; matching is value-based.

---

## File Map

**New files**
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs` — unit tests for the new `defaultIfMissing` behavior of `RecurringJobStatusChecker`.

**Modified files**
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobStatusChecker.cs` — add optional `defaultIfMissing` parameter.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs` — use `defaultIfMissing` for missing-config fallback; keep the on-exception fail-open path returning `true`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs` — inject `IRecurringJobStatusChecker`, replace `_options.Enabled` gate with `IsJobEnabledAsync(... , Metadata.DefaultIsEnabled)`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/AutoTagOptions.cs` — delete `Enabled` property.
- `backend/src/Anela.Heblo.API/appsettings.json` — delete `Photobank:AutoTag:Enabled` key.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs` — add `Mock<IRecurringJobStatusChecker>` with `true` default, update `CreateJob`, rewrite the disabled-path test to use the checker, drop `Enabled = ...` literals, add `ExecuteForPhotosAsync_RunsEvenWhenStatusCheckerReturnsFalse`.

**Unchanged files (intentional)**
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — its existing two-arg call stays valid after the additive interface change.
- All other `IsJobEnabledAsync` callers — their `(jobName, ct)` or `(jobName)` positional calls remain valid and semantically identical (fail-open on missing config).
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` — registration `services.AddScoped<PhotobankAutoTagJob>()` is sufficient; `IRecurringJobStatusChecker` is already registered globally in `BackgroundJobsModule`.

---

## Task 1: Extend `IRecurringJobStatusChecker` with `defaultIfMissing`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobStatusChecker.cs`

- [ ] **Step 1: Add the optional parameter (additive change)**

Replace the entire file with:

```csharp
namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobStatusChecker
{
    /// <summary>
    /// Returns whether the recurring job named <paramref name="jobName"/> is enabled.
    /// </summary>
    /// <param name="jobName">Unique job identifier matching <see cref="RecurringJobMetadata.JobName"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="defaultIfMissing">
    /// Value to return when no configuration row exists for the job.
    /// Defaults to <c>true</c> to preserve historical fail-open behavior for existing callers.
    /// Pass <see cref="RecurringJobMetadata.DefaultIsEnabled"/> for jobs that must default disabled
    /// (e.g. LLM-cost-producing jobs) so an unseeded row does not silently enable them.
    /// </param>
    Task<bool> IsJobEnabledAsync(
        string jobName,
        CancellationToken cancellationToken = default,
        bool defaultIfMissing = true);
}
```

- [ ] **Step 2: Verify the solution still compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED. The additive parameter has a default, so all existing positional callers (`IsJobEnabledAsync(name)`, `IsJobEnabledAsync(name, ct)`) and all Moq setups remain valid.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobStatusChecker.cs
git commit -m "refactor: add defaultIfMissing parameter to IRecurringJobStatusChecker"
```

---

## Task 2: Update `RecurringJobStatusChecker` implementation (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs`

- [ ] **Step 1: Write failing tests for `defaultIfMissing`**

Create `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs` with:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobStatusCheckerTests
{
    private readonly Mock<IRecurringJobConfigurationRepository> _repo = new();

    private RecurringJobStatusChecker CreateSut() =>
        new(_repo.Object, NullLogger<RecurringJobStatusChecker>.Instance);

    [Fact]
    public async Task IsJobEnabledAsync_ReturnsConfiguredValue_WhenRowExists()
    {
        // Arrange
        _repo
            .Setup(r => r.GetByJobNameAsync("job-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecurringJobConfiguration { JobName = "job-a", IsEnabled = false });
        var sut = CreateSut();

        // Act
        var result = await sut.IsJobEnabledAsync("job-a", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsJobEnabledAsync_ReturnsTrue_WhenRowMissing_AndDefaultIfMissingIsTrue()
    {
        // Arrange
        _repo
            .Setup(r => r.GetByJobNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.IsJobEnabledAsync("missing-job", CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsJobEnabledAsync_ReturnsFalse_WhenRowMissing_AndDefaultIfMissingIsFalse()
    {
        // Arrange
        _repo
            .Setup(r => r.GetByJobNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.IsJobEnabledAsync(
            "missing-job",
            CancellationToken.None,
            defaultIfMissing: false);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsJobEnabledAsync_ReturnsTrue_WhenRepositoryThrows()
    {
        // Arrange
        _repo
            .Setup(r => r.GetByJobNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();

        // Act
        var result = await sut.IsJobEnabledAsync("any-job", CancellationToken.None);

        // Assert — error path stays fail-open to avoid breaking critical jobs
        result.Should().BeTrue();
    }
}
```

> **Note:** If the test project does not yet have a `using` for `Anela.Heblo.Domain.Features.BackgroundJobs` for `RecurringJobConfiguration` / `IRecurringJobConfigurationRepository`, verify the exact namespaces with `grep -rn "class RecurringJobConfiguration" backend/src` and `grep -rn "interface IRecurringJobConfigurationRepository" backend/src` and adjust the imports. Both types are expected under `Anela.Heblo.Domain.Features.BackgroundJobs`.

- [ ] **Step 2: Run tests to confirm the third one fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobStatusCheckerTests"`
Expected: 3 pass, 1 fail. The failure is `IsJobEnabledAsync_ReturnsFalse_WhenRowMissing_AndDefaultIfMissingIsFalse` — current implementation returns hardcoded `true` when the row is missing.

- [ ] **Step 3: Update `RecurringJobStatusChecker` to honor `defaultIfMissing`**

Replace `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs` with:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public class RecurringJobStatusChecker : IRecurringJobStatusChecker
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly ILogger<RecurringJobStatusChecker> _logger;

    public RecurringJobStatusChecker(
        IRecurringJobConfigurationRepository repository,
        ILogger<RecurringJobStatusChecker> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<bool> IsJobEnabledAsync(
        string jobName,
        CancellationToken cancellationToken = default,
        bool defaultIfMissing = true)
    {
        try
        {
            var configuration = await _repository.GetByJobNameAsync(jobName, cancellationToken);

            if (configuration == null)
            {
                _logger.LogWarning(
                    "Job configuration not found for job: {JobName}. Falling back to defaultIfMissing={DefaultIfMissing}.",
                    jobName,
                    defaultIfMissing);
                return defaultIfMissing;
            }

            if (!configuration.IsEnabled)
            {
                _logger.LogInformation("Job {JobName} is disabled. Job will be skipped.", jobName);
            }

            return configuration.IsEnabled;
        }
        catch (Exception ex)
        {
            // Safety fallback - on error, allow job to run to avoid blocking critical jobs.
            // This branch is intentionally NOT gated by defaultIfMissing: a DB outage should
            // not silently disable jobs that would otherwise run.
            _logger.LogError(
                ex,
                "Error checking job enabled status for job: {JobName}. Allowing job to run by default.",
                jobName);
            return true;
        }
    }
}
```

- [ ] **Step 4: Re-run tests, all green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobStatusCheckerTests"`
Expected: 4 passed.

- [ ] **Step 5: Run the broader background-jobs test set to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs"`
Expected: all tests pass (existing `TriggerRecurringJobHandlerTests`, `RecurringJobDiscoveryServiceTests`, etc. stay green because their setups still use the two-arg form that resolves to `defaultIfMissing: true`).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs
git commit -m "feat: honor defaultIfMissing in RecurringJobStatusChecker"
```

---

## Task 3: Inject `IRecurringJobStatusChecker` into `PhotobankAutoTagJob` and gate `ExecuteAsync`

This task replaces the `_options.Enabled` check with the runtime status check. Updates the production code, the test fixture, and the existing disabled-path test in tandem to keep the suite green at every step.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs`

- [ ] **Step 1: Update the failing test for the disabled path (status-checker driven)**

In `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs`:

a) Add the using directive at the top of the file (after the existing `using Anela.Heblo.Domain.Features.Photobank;`):

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
```

b) Add a `Mock<IRecurringJobStatusChecker>` field next to the existing mocks. Replace:

```csharp
    private readonly Mock<IPhotobankRepository> _repo = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();
```

with:

```csharp
    private readonly Mock<IPhotobankRepository> _repo = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    public PhotobankAutoTagJobTests()
    {
        // Default: job is enabled. Individual tests override as needed.
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(true);
    }
```

c) Replace the existing `CreateJob` helper. Replace:

```csharp
    private PhotobankAutoTagJob CreateJob(AutoTagOptions? options = null)
    {
        var opts = options ?? new AutoTagOptions { Enabled = true, BatchSize = 50, MaxPhotosPerRun = 5_000 };
        return new PhotobankAutoTagJob(
            _repo.Object,
            _chat.Object,
            Options.Create(opts),
            NullLogger<PhotobankAutoTagJob>.Instance,
            _cache.Object);
    }
```

with:

```csharp
    private PhotobankAutoTagJob CreateJob(AutoTagOptions? options = null)
    {
        var opts = options ?? new AutoTagOptions { BatchSize = 50, MaxPhotosPerRun = 5_000 };
        return new PhotobankAutoTagJob(
            _repo.Object,
            _chat.Object,
            Options.Create(opts),
            NullLogger<PhotobankAutoTagJob>.Instance,
            _cache.Object,
            _statusChecker.Object);
    }
```

d) Rewrite the `ExecuteAsync_WhenDisabled_DoesNotCallLlmOrRepository` test to drive the gate via the status checker. Replace:

```csharp
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_DoesNotCallLlmOrRepository()
    {
        // Arrange
        var job = CreateJob(new AutoTagOptions { Enabled = false });

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        _chat.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _repo.Verify(
            r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

with:

```csharp
    [Fact]
    public async Task ExecuteAsync_WhenStatusCheckerReturnsFalse_DoesNotCallLlmOrRepository()
    {
        // Arrange
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(false);
        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        _chat.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _repo.Verify(
            r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

e) Update the `MaxTagsPerPhoto` test's inline constructor call so it passes the new dependency and drops the obsolete field. Replace:

```csharp
        // Use job with MaxTagsPerPhoto = 2
        var jobWithCap = new PhotobankAutoTagJob(
            _repo.Object,
            _chat.Object,
            Options.Create(new AutoTagOptions { Enabled = true, BatchSize = 50, MaxPhotosPerRun = 100, Model = "test-model", MaxTagsPerPhoto = 2 }),
            NullLogger<PhotobankAutoTagJob>.Instance,
            _cache.Object);
```

with:

```csharp
        // Use job with MaxTagsPerPhoto = 2
        var jobWithCap = new PhotobankAutoTagJob(
            _repo.Object,
            _chat.Object,
            Options.Create(new AutoTagOptions { BatchSize = 50, MaxPhotosPerRun = 100, Model = "test-model", MaxTagsPerPhoto = 2 }),
            NullLogger<PhotobankAutoTagJob>.Instance,
            _cache.Object,
            _statusChecker.Object);
```

- [ ] **Step 2: Run the photobank-auto-tag tests; confirm they fail to compile**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: BUILD FAILED. Errors point to `PhotobankAutoTagJob` constructor: "no overload takes 6 arguments" (the production class still has the old 5-arg ctor).

- [ ] **Step 3: Update `PhotobankAutoTagJob` to inject the status checker and use it**

Replace `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs` lines 13–50 (class declaration through end of disabled-guard block) with the new shape. Concretely:

Replace:

```csharp
public class PhotobankAutoTagJob : IRecurringJob
{
    private readonly IPhotobankRepository _repo;
    private readonly IChatClient _chat;
    private readonly AutoTagOptions _options;
    private readonly ILogger<PhotobankAutoTagJob> _logger;
    private readonly IPhotobankTagsCache _cache;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "photobank-auto-tag",
        DisplayName = "Photobank Auto-Tag",
        Description = "Sends untagged photos to the LLM and stamps validated tags back",
        CronExpression = "0 4 * * *",
        DefaultIsEnabled = false,
    };

    public PhotobankAutoTagJob(
        IPhotobankRepository repo,
        IChatClient chat,
        IOptions<AutoTagOptions> options,
        ILogger<PhotobankAutoTagJob> logger,
        IPhotobankTagsCache cache)
    {
        _repo = repo;
        _chat = chat;
        _options = options.Value;
        _logger = logger;
        _cache = cache;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }
```

with:

```csharp
public class PhotobankAutoTagJob : IRecurringJob
{
    private readonly IPhotobankRepository _repo;
    private readonly IChatClient _chat;
    private readonly AutoTagOptions _options;
    private readonly ILogger<PhotobankAutoTagJob> _logger;
    private readonly IPhotobankTagsCache _cache;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "photobank-auto-tag",
        DisplayName = "Photobank Auto-Tag",
        Description = "Sends untagged photos to the LLM and stamps validated tags back",
        CronExpression = "0 4 * * *",
        DefaultIsEnabled = false,
    };

    public PhotobankAutoTagJob(
        IPhotobankRepository repo,
        IChatClient chat,
        IOptions<AutoTagOptions> options,
        ILogger<PhotobankAutoTagJob> logger,
        IPhotobankTagsCache cache,
        IRecurringJobStatusChecker statusChecker)
    {
        _repo = repo;
        _chat = chat;
        _options = options.Value;
        _logger = logger;
        _cache = cache;
        _statusChecker = statusChecker;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(
                Metadata.JobName,
                cancellationToken,
                defaultIfMissing: Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }
```

> The `using Anela.Heblo.Domain.Features.BackgroundJobs;` directive is already present at line 5 of the file — no import edit required.

- [ ] **Step 4: Run the full photobank test set; expect green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"`
Expected: all PhotobankAutoTagJobTests and PhotobankIndexJobTests pass (5 in the auto-tag file at this point — the four legacy tests plus the renamed disabled-path test).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs
git commit -m "refactor: gate PhotobankAutoTagJob via IRecurringJobStatusChecker"
```

---

## Task 4: Add explicit test confirming `ExecuteForPhotosAsync` is ungated

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs`

- [ ] **Step 1: Add the failing test**

Append the following test at the end of the `PhotobankAutoTagJobTests` class, immediately before its closing brace:

```csharp
    [Fact]
    public async Task ExecuteForPhotosAsync_RunsEvenWhenStatusCheckerReturnsFalse()
    {
        // Arrange — recurring-schedule toggle is OFF, but ad-hoc retag must still run.
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(false);

        var candidates = new List<PhotoAutoTagCandidate>
        {
            new(Id: 7, FolderPath: "/photos", FileName: "ad-hoc.jpg"),
        };

        _repo
            .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagCount> { new(10, "kosmetika", 1) });

        SetupChatResponse("""{"results":[{"id":7,"tags":["kosmetika"]}]}""");

        _repo
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo
            .Setup(r => r.StampAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var job = CreateJob();

        // Act
        await job.ExecuteForPhotosAsync(candidates, CancellationToken.None);

        // Assert — LLM was invoked and the candidate was stamped, despite the recurring toggle being off.
        _chat.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repo.Verify(
            r => r.StampAutoTaggedAtAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 7),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _statusChecker.Verify(
            s => s.IsJobEnabledAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the test; expect green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteForPhotosAsync_RunsEvenWhenStatusCheckerReturnsFalse"`
Expected: PASS. The production code in `ExecuteForPhotosAsync` (lines 77–87 of `PhotobankAutoTagJob.cs`) was unchanged and does not invoke the checker — this test locks that contract.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs
git commit -m "test: lock ExecuteForPhotosAsync as ungated by IRecurringJobStatusChecker"
```

---

## Task 5: Remove `Enabled` from `AutoTagOptions`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/AutoTagOptions.cs`

- [ ] **Step 1: Delete the `Enabled` property**

Replace the entire file with:

```csharp
namespace Anela.Heblo.Application.Features.Photobank;

public sealed class AutoTagOptions
{
    public const string SectionName = "Photobank:AutoTag";

    public int BatchSize { get; init; } = 50;
    public int MaxPhotosPerRun { get; init; } = 5_000;
    public string Model { get; init; } = "claude-haiku-4-5-20251001";
    public int MaxTagsPerPhoto { get; init; } = 5;
}
```

- [ ] **Step 2: Verify no production or test code still references `AutoTagOptions.Enabled`**

Run: `grep -rn "AutoTagOptions.*Enabled\|Enabled.*=.*\(true\|false\).*BatchSize" backend/src backend/test --include='*.cs' || echo "no matches"`
Expected: `no matches`. (If anything appears, it is a leftover from Tasks 3–4; fix and re-run.)

- [ ] **Step 3: Build the solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED. The compiler would catch any remaining `Enabled = ...` initializer or `_options.Enabled` access.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/AutoTagOptions.cs
git commit -m "refactor: remove obsolete Enabled flag from AutoTagOptions"
```

---

## Task 6: Drop the obsolete config key from `appsettings.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (lines 175–186)

- [ ] **Step 1: Remove the `Enabled` key from the `Photobank:AutoTag` block**

Find the block at lines 175–186:

```json
  "Photobank": {
    "AutoTag": {
      "Enabled": false,
      "BatchSize": 50,
      "MaxPhotosPerRun": 5000,
      "Model": "claude-haiku-4-5-20251001",
      "MaxTagsPerPhoto": 5
    },
    "TagsCache": {
      "TtlSeconds": 60
    }
  },
```

Replace with:

```json
  "Photobank": {
    "AutoTag": {
      "BatchSize": 50,
      "MaxPhotosPerRun": 5000,
      "Model": "claude-haiku-4-5-20251001",
      "MaxTagsPerPhoto": 5
    },
    "TagsCache": {
      "TtlSeconds": 60
    }
  },
```

- [ ] **Step 2: Confirm no other appsettings file carries the key**

Run: `grep -rn "AutoTag" backend/src/Anela.Heblo.API --include='appsettings*.json'`
Expected: only the `appsettings.json` `AutoTag` block remains (now without `Enabled`). No matches in `appsettings.Conductor.json`, `appsettings.Development.json`, `appsettings.Production.json`, `appsettings.Staging.json`, or `appsettings.Test.json` — this was already verified during plan authoring; the check guards against drift between then and now.

- [ ] **Step 3: Sanity-build the API project**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: BUILD SUCCEEDED. (The JSON change has no compile effect; this confirms nothing in the API project picked up an accidental edit.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "chore: drop Photobank:AutoTag:Enabled from appsettings.json"
```

---

## Task 7: Full validation sweep

- [ ] **Step 1: Run the entire backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: all tests pass. Pay particular attention to:
- `PhotobankAutoTagJobTests` — 5 tests (4 existing + 1 new ungated test).
- `RecurringJobStatusCheckerTests` — 4 tests (all new).
- `PhotobankIndexJobTests`, `TriggerRecurringJobHandlerTests`, `TriggerRecurringJobHandlerIntegrationTests`, and every other job's `*Tests` consuming `IRecurringJobStatusChecker` — must remain green without modification (their setups omit the new param; the compiler-injected `true` default matches production calls that also omit it).

- [ ] **Step 2: Apply formatting**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no diffs (or only trivial whitespace touch-ups). If diffs appear in files we modified, stage and amend the previous commit for that file; if diffs appear elsewhere, leave them alone and report.

- [ ] **Step 3: Final solution-wide compile**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED with 0 errors and 0 warnings introduced by this change.

- [ ] **Step 4: Spot-check the runtime contract**

Run: `grep -n "_options.Enabled\|AutoTagOptions.*Enabled" backend/src backend/test -r --include='*.cs' || echo "clean"`
Expected: `clean`. No residual references to the dropped flag.

Run: `grep -n "\"Enabled\"" backend/src/Anela.Heblo.API/appsettings.json | grep -i "AutoTag" || echo "clean"`
Expected: `clean`. The config key is gone.

- [ ] **Step 5: Commit any formatting fixes (if Step 2 produced edits)**

```bash
git status            # confirm what changed
git add -p            # stage formatting hunks only
git commit -m "chore: dotnet format"
```

If nothing changed, skip this step.

---

## Post-Implementation Notes (release notes hint)

Operators previously toggling auto-tag through `Photobank:AutoTag:Enabled` must now toggle the `photobank-auto-tag` recurring job through the same UI/API used for `photobank-index` (the `UpdateRecurringJobStatusHandler` MediatR endpoint). On first start after deploy, `SeedDefaultConfigurationsAsync` will insert a `RecurringJobConfigurations` row with `IsEnabled = false`, preserving the default-disabled posture. Any stale `Photobank:AutoTag:Enabled` value in Azure App Service settings or Key Vault is silently ignored by the options binder — safe to delete at convenience but not required.
