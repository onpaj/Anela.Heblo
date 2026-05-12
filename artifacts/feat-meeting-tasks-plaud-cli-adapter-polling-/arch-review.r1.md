Now I have enough context. Let me produce the architecture review.

# Architecture Review: Plaud CLI Adapter, Polling Job & Ingest Handler

## Skip Design: true

## Architectural Fit Assessment

The feature fits the existing Clean Architecture monorepo well: adapters live under `backend/src/Adapters/`, vertical slices live under `backend/src/Anela.Heblo.Application/Features/<Feature>`, and Hangfire jobs are auto-discovered through `IRecurringJob`. The Anthropic adapter is a usable template for project layout, options binding, and DI extension method.

However, two structural conflicts in the spec must be resolved before implementation:

1. **Dependency inversion is missing.** The spec places `IPlaudClient` in `Anela.Heblo.Adapters.Plaud` and has `IngestPlaudRecordingHandler` (in `Application`) consume it. But `Anela.Heblo.Application.csproj` references no adapters — adapters reference Application (verified against `Anela.Heblo.Adapters.Anthropic.csproj` and `Anela.Heblo.Adapters.ShoptetApi.csproj`). The spec's directive that "Application references Anela.Heblo.Adapters.Plaud" inverts the established layering. The port (`IPlaudClient`) must live in Application; the adapter project supplies the implementation.

2. **`RecurringJobMetadata` API mismatch.** Spec code uses positional construction `new(JobName: ..., DisplayName: ..., Cron: "*/5 * * * *", DefaultIsEnabled: false)`. The actual type at `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs:6` is a plain class with `required` `init` properties: `JobName`, `DisplayName`, `Description` (required), **`CronExpression`** (not `Cron`), `DefaultIsEnabled`, and optional `TimeZoneId`. The reference job `DailyConsumptionJob` at `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJob.cs:14` uses object-initializer syntax with all four required props. The spec code will not compile.

A few other smaller deviations from existing conventions are flagged in the Specification Amendments section.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.API                                                     │
│  Program.cs  ──► AddPlaudAdapter()                                  │
│              ──► AddRecurringJobs()  (reflection discovers job)     │
│  RecurringJobDiscoveryService (HostedService) → Hangfire schedule   │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────┴──────────────────────────────────────┐
│ Anela.Heblo.Application                                             │
│  Features/MeetingTasks/                                             │
│   ├─ MeetingTasksModule                                             │
│   ├─ Ports/IPlaudClient + PlaudRecordingSummary  ◄── PORT (new home)│
│   ├─ Services/IMeetingTaskExtractor + ClaudeMeetingTaskExtractor    │
│   │       └── uses IChatClient (Anthropic adapter)                  │
│   ├─ UseCases/IngestPlaudRecording/Request+Handler                  │
│   │       └── IPlaudClient, IMeetingTaskExtractor,                  │
│   │           IMeetingTranscriptRepository (from Subtask 1)         │
│   └─ Infrastructure/Jobs/PlaudPollingJob (IRecurringJob)            │
│       └── IMediator → IngestPlaudRecordingRequest                   │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ (implements port)
┌──────────────────────────────┴──────────────────────────────────────┐
│ Anela.Heblo.Adapters.Plaud (ADAPTER)                                │
│  ├─ PlaudOptions (bound to "Plaud" section)                         │
│  ├─ PlaudCliClient : IPlaudClient  (uses System.Diagnostics.Process)│
│  ├─ PlaudTokenBootstrapper : IHostedService                         │
│  └─ PlaudAdapterServiceCollectionExtensions.AddPlaudAdapter()       │
│        References: Anela.Heblo.Application (for IPlaudClient port)  │
└─────────────────────────────────────────────────────────────────────┘
                               │ runs
                               ▼
                         plaud CLI binary (installed in Docker image)
```

### Key Design Decisions

#### Decision 1: Port location (`IPlaudClient`)
**Options considered:**
- A. Keep interface inside the adapter project (spec's wording).
- B. Move interface to `Anela.Heblo.Application/Features/MeetingTasks/Ports/`.
- C. Move interface to `Anela.Heblo.Domain/Features/MeetingTasks/`.

**Chosen approach:** B — port lives in Application under `Features/MeetingTasks/Ports/IPlaudClient.cs` (and `PlaudRecordingSummary.cs` next to it).

**Rationale:** Application cannot reference Adapters (verified — no existing adapter is referenced from Application). The Anthropic adapter follows this same direction (it references Application for `PostAnswerEnrichmentMiddleware`). Putting the port in Domain is overkill — `PlaudRecordingSummary` is an integration DTO, not a domain concept. Application-level Ports is consistent with how the codebase places integration abstractions (e.g., adapter implementations of repositories defined in Application/Domain).

#### Decision 2: Job registration & lifetime
**Options considered:**
- A. Register `PlaudPollingJob` manually in `MeetingTasksModule` as transient (spec).
- B. Rely on `AddRecurringJobs()` reflection in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:346`, which scans the Application assembly and registers all `IRecurringJob` types as **scoped**.

**Chosen approach:** B — do not add an explicit registration in `MeetingTasksModule`. The reflection-based registrar handles it. Match the existing scoped lifetime used for `DailyConsumptionJob` (referenced in `PackingMaterialsModule.cs:22` plus auto-discovery).

**Rationale:** Spec's transient lifetime conflicts with the established pattern. The auto-discovery in `AddRecurringJobs()` already covers registration; an explicit DI registration here is redundant and inconsistent with `DailyConsumptionJob`.

#### Decision 3: `IPlaudClient` DI lifetime
**Options considered:**
- A. Singleton (spec).
- B. Scoped (consistent with the rest of the slice and with `IChatClient`).

**Chosen approach:** A is acceptable — the `PlaudCliClient` is effectively stateless (reads options, spawns a process per call, owns no scoped/EF dependencies). Singleton avoids per-handler resolution overhead.

**Rationale:** No mutable shared state. `ILogger<PlaudCliClient>` and `IOptions<PlaudOptions>` are safe singletons. Keep the spec's choice.

#### Decision 4: Process lifecycle / timeout handling
**Options considered:**
- A. Use linked CTS with `CancelAfter` and rely on `WaitForExitAsync` to observe the token (spec).
- B. As A, but on cancellation/timeout call `process.Kill(entireProcessTree: true)` before throwing.

**Chosen approach:** B.

**Rationale:** When `cts.Token` fires, `WaitForExitAsync` throws `OperationCanceledException` but the spawned `plaud` keeps running, leaking processes and tokens. The implementation must guarantee process termination via a `try/finally` that kills the process tree if it has not exited. This is a correctness fix to the spec snippet.

#### Decision 5: Hangfire concurrency on overlapping ticks
**Options considered:**
- A. Rely on `ExistsByPlaudIdAsync` + unique index in DB to dedupe.
- B. Add `[DisableConcurrentExecution(timeoutInSeconds: 270)]` on `PlaudPollingJob.ExecuteAsync`.

**Chosen approach:** B.

**Rationale:** With per-CLI timeout 60s and N recordings sequential, a tick could in theory exceed 5 min and overlap the next tick. Dedup will still hold via the unique index, but `DisableConcurrentExecution` keeps logs / dashboard state clean and avoids parallel CLI invocations sharing the same `~/.plaud/tokens.json` file.

#### Decision 6: Structured-output reliability against `AnthropicChatClient`
**Observation:** `AnthropicChatClient.GetResponseAsync` at `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs:58` ignores `options.ResponseFormat` and constructs its own request body. `IChatClient.GetResponseAsync<T>()` from `Microsoft.Extensions.AI` works by appending a JSON-schema instruction to the prompt and then parsing the model's text — so it should function, but reliability depends on Claude actually returning valid JSON.

**Chosen approach:** Use `GetResponseAsync<List<ExtractedTask>>(...)` as the spec proposes, **but** the extractor's catch block must also treat null `response.Result` and JSON-parse failures as "no tasks extracted" (already covered by the warn-and-return-empty pattern). No change to `AnthropicChatClient` is required for this subtask.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Adapters/Anela.Heblo.Adapters.Plaud/
  Anela.Heblo.Adapters.Plaud.csproj           # refs: Anela.Heblo.Application
  PlaudOptions.cs
  PlaudCliClient.cs                           # implements Application's IPlaudClient
  PlaudTokenBootstrapper.cs                   # IHostedService
  PlaudAdapterServiceCollectionExtensions.cs

backend/src/Anela.Heblo.Application/Features/MeetingTasks/
  MeetingTasksModule.cs                       # AddMeetingTasksModule()
  Ports/
    IPlaudClient.cs                           # <-- PORT here, not in adapter
    PlaudRecordingSummary.cs
  Services/
    IMeetingTaskExtractor.cs
    ExtractedTask.cs                          # record (immutable)
    ClaudeMeetingTaskExtractor.cs
  UseCases/IngestPlaudRecording/
    IngestPlaudRecordingRequest.cs
    IngestPlaudRecordingResponse.cs
    IngestPlaudRecordingHandler.cs
  Infrastructure/Jobs/
    PlaudPollingJob.cs

backend/test/Anela.Heblo.Adapters.Plaud.Tests/
  Anela.Heblo.Adapters.Plaud.Tests.csproj
  PlaudCliClientParserTests.cs                # offline fixture-based

backend/test/Anela.Heblo.Tests/Features/MeetingTasks/
  ClaudeMeetingTaskExtractorTests.cs
  IngestPlaudRecordingHandlerTests.cs
  PlaudPollingJobTests.cs                     # (new — see amendments)
```

Wire `AddMeetingTasksModule()` into `backend/src/Anela.Heblo.Application/ApplicationModule.cs` next to the existing `AddPackingMaterialsModule()` call. Wire `AddPlaudAdapter(builder.Configuration)` into `backend/src/Anela.Heblo.API/Program.cs` right after `AddAnthropicAdapter(...)` at line 74.

Add `<ProjectReference Include="..\..\Adapters\Anela.Heblo.Adapters.Plaud\Anela.Heblo.Adapters.Plaud.csproj" />` to `Anela.Heblo.API.csproj` only — **do not** add it to `Anela.Heblo.Application.csproj`.

### Interfaces and Contracts

```csharp
// Application/Features/MeetingTasks/Ports/IPlaudClient.cs
public interface IPlaudClient
{
    Task<IReadOnlyList<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default);
    Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default);
    Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default);
}

// Application/Features/MeetingTasks/Ports/PlaudRecordingSummary.cs
public sealed record PlaudRecordingSummary(
    string Id,
    string Name,
    DateTime CreatedAt,
    bool HasTranscript,
    bool HasSummary);

// Application/Features/MeetingTasks/Services/ExtractedTask.cs
public sealed record ExtractedTask(string Title, string Description, string Assignee, DateTime? DueDate);

// MeetingTasksModule.cs
public static IServiceCollection AddMeetingTasksModule(this IServiceCollection services)
{
    services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();
    // No explicit PlaudPollingJob registration — AddRecurringJobs() reflection covers it.
    return services;
}
```

`PlaudCliClient` lives in the adapter project but its `: IPlaudClient` references the port in Application via the existing project reference (`Adapters.Plaud → Application`).

Use object-initializer syntax with `CronExpression` (not `Cron`) for the metadata:

```csharp
public RecurringJobMetadata Metadata { get; } = new()
{
    JobName = "MeetingTasks.PlaudPolling",
    DisplayName = "Plaud — pull meeting transcripts",
    Description = "Polls Plaud CLI every 5 minutes for completed recordings and ingests them as pending-review transcripts.",
    CronExpression = "*/5 * * * *",
    DefaultIsEnabled = false
};
```

### Data Flow

**Polling tick (every 5 min, when enabled):**

```
Hangfire ─► PlaudPollingJob.ExecuteAsync(ct)
            ├─ IRecurringJobStatusChecker.IsJobEnabledAsync → exit if false
            ├─ IPlaudClient.ListRecentAsync(7, ct)
            │     └─ Process.Start("plaud", "recent --days 7") → stdout
            │        └─ PlaudCliClient.ParseFilesOutput(stdout) → List<PlaudRecordingSummary>
            ├─ filter: HasTranscript && HasSummary
            └─ for each ready recording:
                IMediator.Send(IngestPlaudRecordingRequest { Id, Name, CreatedAt })
                  └─ IngestPlaudRecordingHandler
                       ├─ IMeetingTranscriptRepository.ExistsByPlaudIdAsync → skip if true
                       ├─ IPlaudClient.GetTranscriptAsync(id)
                       ├─ IPlaudClient.GetSummaryAsync(id)
                       ├─ IMeetingTaskExtractor.ExtractAsync(summary, transcript)
                       │     └─ IChatClient.GetResponseAsync<List<ExtractedTask>>(...)
                       │        (catch-all → empty list on failure)
                       ├─ build MeetingTranscript (Status = PendingReview)
                       ├─ AddAsync + SaveChangesAsync
                       └─ return { TranscriptId, Skipped = false }
```

**Host startup (one-time):**

```
PlaudTokenBootstrapper.StartAsync
  ├─ if TokensJson empty → log warn, no-op
  └─ else → mkdir ~/.plaud; write tokens.json
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `plaud` CLI binary install method is unknown / not publicly documented; Dockerfile changes are not specified | **High** | **Prerequisite**: capture the actual install procedure (apt-get? curl + binary? language runtime?) and verify `plaud --version` inside the runtime image before implementation. Pin a specific CLI version. If install requires extra packages, add them to the existing `apt-get install` block at `Dockerfile:58`. |
| CLI subcommands assumed (`recent --days`, `transcript`, `summary`, `files`); the brief uses `plaud files`, spec uses `plaud recent --days` — possibly inconsistent | **High** | Run the CLI locally first, capture real `--help` output, and commit a verified fixture string + final argv into `PlaudCliClient`. Update spec wording to match real CLI surface. |
| Parser format drift — output format may differ from the assumed `ID NAME DATE TIME TRANSCRIPT SUMMARY` layout, especially with whitespace in names or alternative columns | High | Commit a canonical fixture captured from the real CLI to the parser tests. Add a test for a row where `Name` contains multiple spaces. Skip rather than throw on malformed rows. |
| Process leak on timeout — spec snippet does not kill the child process when the linked CTS fires | High | Wrap `WaitForExitAsync` in `try/finally`; in the finally, if `!process.HasExited`, call `process.Kill(entireProcessTree: true)`. Log a warning with arguments and elapsed time. |
| Overlapping ticks share the same `~/.plaud/tokens.json` and CLI process state | Medium | Add `[DisableConcurrentExecution(270)]` to `PlaudPollingJob.ExecuteAsync`. DB unique index on `PlaudRecordingId` provides a secondary safety net. |
| `Environment.SpecialFolder.UserProfile` resolves to empty string when `$HOME` is unset for the container user (uid 1001 created via `adduser`) | Medium | `adduser --disabled-password` creates `/home/appuser` and sets HOME. Verify at startup: log the resolved path. Optionally fall back to `Path.Combine("/home", Environment.UserName, ".plaud")` if `UserProfile` is empty, and fail-fast with a clear error message rather than writing to `/`. |
| Hangfire's default retry on a thrown `InvalidOperationException` will re-run the failed recording immediately on transient CLI failures, possibly amplifying load | Medium | Add `[AutomaticRetry(Attempts = 0)]` on `PlaudPollingJob.ExecuteAsync` — the next 5-minute tick is the retry. Spec implies this but does not enforce it. |
| `AnthropicChatClient` ignores `options.ResponseFormat`; structured-output relies on Claude obeying inline JSON instructions | Medium | Keep extractor's try/catch + return-empty behavior; rely on schema-in-prompt from `Microsoft.Extensions.AI`. Optionally extend `AnthropicChatClient` later to forward `response_format` — out of scope for this subtask. |
| Transcript and summary text are large; loading them into the `MeetingTranscript.RawTranscript`/`Summary` columns may hit EF tracker memory or DB column type limits | Low | Confirm Subtask 1 uses `text` (Postgres) or `nvarchar(max)` for both columns. If not, raise a prerequisite issue against Subtask 1. |
| Logs accidentally leak secret/transcript content via structured logging or unhandled exception | Medium | Code-review checklist: no `_logger.LogX(..., transcript)`, `..., summary)`, or `..., TokensJson)` anywhere. Use deterministic redaction (`{Length}` instead of body) when needed for debugging. |
| `IRecurringJob` assemblies are auto-scanned at startup — registering `PlaudPollingJob` manually as transient (per spec) would create a duplicate registration and may register the job twice with conflicting lifetimes | Low | Do not add an explicit DI registration for `PlaudPollingJob` in `MeetingTasksModule`. The reflection in `AddRecurringJobs()` is sufficient. |

## Specification Amendments

The spec is otherwise solid. The following amendments are required for implementation to compile and align with the codebase:

1. **Move `IPlaudClient` and `PlaudRecordingSummary` to `Anela.Heblo.Application/Features/MeetingTasks/Ports/`.** The adapter project still defines `PlaudCliClient : IPlaudClient`. Update FR-1's file list accordingly. Update FR-7 to **remove** the directive that "`Anela.Heblo.Application` references `Anela.Heblo.Adapters.Plaud`" — that reference must not exist. Only `Anela.Heblo.API` references the adapter.

2. **Fix `RecurringJobMetadata` construction in FR-6.** Replace the positional-record snippet with object-initializer syntax matching `DailyConsumptionJob.cs:14`. Add the missing required `Description` field. Rename `Cron` to `CronExpression`. Default `TimeZoneId` is already `Europe/Prague`.

3. **Drop the "register `PlaudPollingJob` as transient" requirement in FR-6/FR-7.** The reflection-based `AddRecurringJobs()` in `ServiceCollectionExtensions.cs:346` discovers and registers it as scoped (matching `DailyConsumptionJob`). The `MeetingTasksModule` only needs to register `IMeetingTaskExtractor → ClaudeMeetingTaskExtractor` (scoped) and call into `ApplicationModule.AddApplicationServices()`.

4. **Add `[DisableConcurrentExecution(timeoutInSeconds: 270)]` and `[AutomaticRetry(Attempts = 0)]` on `PlaudPollingJob.ExecuteAsync`.** Update FR-6 acceptance criteria.

5. **Fix process termination in `PlaudCliClient.RunCommandAsync`.** Wrap `WaitForExitAsync` in `try/finally`; in finally, kill the process tree if it hasn't exited. Update FR-1's process-timeout acceptance criterion accordingly.

6. **Reconcile CLI argv between brief and spec.** Brief example uses `plaud files`; spec uses `plaud recent --days <n>`. Pick one based on the real CLI; record both the chosen argv and the verified `--version` output as committed artifacts before implementation.

7. **Make `ExtractedTask`, `PlaudRecordingSummary`, and `IngestPlaudRecordingResponse` immutable records** (per project coding-style rule on immutability and per existing convention — DTOs in HTTP-facing contracts must remain `class` per CLAUDE.md, but these are internal types, not OpenAPI-exposed, so records are fine). `IngestPlaudRecordingRequest` is a MediatR request — keep as a class with init-only setters to stay consistent with other handlers' request shapes (verify against existing requests under `Features/PackingMaterials/UseCases/`).

8. **Add `PlaudPollingJobTests` to NFR-4 coverage** (currently lists only parser, extractor, handler). Covering: status-checker-disabled exits early; recordings without both flags are filtered; dispatch counts correctly accumulate `Skipped`/`Ingested`.

9. **Logging redaction note in NFR-5:** explicitly forbid `_logger.LogX` calls that take `transcript`, `summary`, or `TokensJson` as a templated argument. Logged sizes / lengths are acceptable.

10. **Wire `AddMeetingTasksModule()` into `ApplicationModule.cs`** alongside the other `services.AddXxxModule()` calls. This is implied but not explicit in FR-7.

## Prerequisites

Before implementation can start:

1. **Subtask 1 delivered**, with verified shapes for `MeetingTranscript`, `ProposedTask`, `MeetingTranscriptStatus.PendingReview`, `ProposedTaskStatus.Pending`, and `IMeetingTranscriptRepository` exposing at minimum `ExistsByPlaudIdAsync`, `AddAsync`, `SaveChangesAsync`. Confirm EF Core configuration applies a **unique index** on `PlaudRecordingId` and uses unbounded text types for `Summary` and `RawTranscript`.
2. **`plaud` CLI install procedure verified locally** on Linux (the runtime image base is `mcr.microsoft.com/dotnet/aspnet:8.0`, Debian-based). Capture: install command(s), version string, full `plaud --help` output, real output of `plaud recent --days 7` (or whichever is the listing command) including header layout and column delimiter behavior with multi-word names, and real outputs of `plaud transcript <id>` / `plaud summary <id>`. Commit these as fixtures.
3. **`Plaud:TokensJson` secret available** in local user-secrets and a staging Azure App Service App Setting before enabling the job in any non-dev environment. Job remains `DefaultIsEnabled = false` until that is set.
4. **Anthropic adapter operational** (`AnthropicOptions.ApiKey` set in the same environment). No new secret introduced.
5. **Dockerfile change reviewed** for image size impact and for HOME being set for `appuser` (uid 1001). If `adduser` did not create `/home/appuser` for the existing image, add `RUN mkdir -p /home/appuser && chown appuser:appuser /home/appuser` and set `ENV HOME=/home/appuser` before the `USER appuser` directive at `Dockerfile:83`.
6. **Hangfire scheduler enabled in the target environment** (`HangfireOptions.SchedulerEnabled = true`) — otherwise `RecurringJobDiscoveryService` will skip registration and the job will never run regardless of the DB enable flag.