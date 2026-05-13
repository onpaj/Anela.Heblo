# Architecture Review: Plaud CLI Adapter, Polling Job & Ingest Handler

## Skip Design: true

Backend-only feature — new adapter project, MediatR handler, Hangfire job, and AI extractor service. No UI, no new screens, no visual components.

## Architectural Fit Assessment

The proposal aligns with three well-established patterns in this codebase:

1. **Adapter project layout** — `Anela.Heblo.Adapters.Plaud` mirrors `Anela.Heblo.Adapters.Anthropic`: options class, options-bound service collection extension, single client class. ✓
2. **Recurring job pattern** — `IRecurringJob` is the de-facto interface; `RecurringJobDiscoveryService` already scans `Anela.Heblo.Application` and seeds DB-backed configuration, with `IRecurringJobStatusChecker` as the runtime on/off gate. ✓
3. **MediatR + Vertical Slice** — `Features/<Domain>/UseCases/<UseCase>/<Request>+<Handler>` plus a `<Domain>Module.cs` static class added to `ApplicationModule.AddApplicationServices()`. ✓

However, the spec/brief contain **several concrete contract mismatches** with the existing infrastructure that will fail to compile or behave wrong if implemented literally:

- `RecurringJobMetadata` uses `CronExpression` (required init), `Description` is **required**, and `JobName` convention is **kebab-case** — not `Cron`, not optional, and not dotted PascalCase as in the brief code.
- `services.AddTransient<PlaudPollingJob>()` in `MeetingTasksModule` would **double-register** the job (auto-discovery in `AddRecurringJobs()` already adds it as `Scoped`), and the lifetime mismatch (Scoped dependencies inside a Transient job) is wrong.
- The current branch is **not based on the epic branch**, so `MeetingTranscript`, `ProposedTask`, and `IMeetingTranscriptRepository` are missing locally — code won't compile until the upstream is merged in.

Integration points: Anthropic adapter (`IChatClient`), Hangfire infra (`AddRecurringJobs`, `IRecurringJobStatusChecker`, `RecurringJobDiscoveryService`), `IMeetingTranscriptRepository` (from epic), MediatR pipeline, configuration secrets.

## Proposed Architecture

### Component Overview

```
                ┌─────────────────────────────────────────────────┐
                │  Hangfire Server (every 5 min via cron)          │
                └────────────────────────┬─────────────────────────┘
                                         │  ExecuteAsync
                                         ▼
┌──────────────────────────────────────────────────────────────────┐
│ Application/Features/MeetingTasks                                │
│                                                                  │
│  ┌─────────────────────┐   gate    ┌────────────────────────┐    │
│  │ PlaudPollingJob     │──────────▶│ IRecurringJobStatus    │    │
│  │ (IRecurringJob)     │           │ Checker (DB-backed)    │    │
│  └──────────┬──────────┘           └────────────────────────┘    │
│             │ list + per-recording dispatch                      │
│             ▼                                                    │
│  ┌─────────────────────┐                                         │
│  │ IngestPlaudRecordin │                                         │
│  │ gHandler (MediatR)  │                                         │
│  └─────┬─────┬─────┬───┘                                         │
│        │     │     │                                             │
│  dedup │     │     │ extract                                     │
│        │     │     ▼                                             │
│        │     │  ┌──────────────────────────┐                     │
│        │     │  │ IMeetingTaskExtractor    │                     │
│        │     │  │  → ClaudeMeetingTask...  │─┐                   │
│        │     │  └──────────────────────────┘ │                   │
│        │     │                               │                   │
└────────┼─────┼───────────────────────────────┼───────────────────┘
         │     │ transcript/summary            │ IChatClient
         │     ▼                               ▼
         │   ┌────────────────────┐    ┌────────────────────┐
         │   │ IPlaudClient       │    │ Anthropic adapter  │
         │   │  PlaudCliClient    │    │ (existing)         │
         │   │  (Adapters.Plaud)  │    └────────────────────┘
         │   └─────────┬──────────┘
         │             │ Process.Start: plaud {cmd} {args}
         │             ▼
         │       [plaud CLI binary, ~/.plaud/tokens.json]
         │             ▲
         │             │ writes tokens.json on host start
         │       ┌─────┴───────────────┐
         │       │ PlaudTokenBootstrap │  IHostedService
         │       └─────────────────────┘
         ▼
   ┌─────────────────────────────┐
   │ IMeetingTranscriptRepository│ (epic-branch subtask 1)
   │   ExistsByPlaudIdAsync      │
   │   AddAsync / SaveChangesAsync│
   └─────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Do NOT manually register `PlaudPollingJob` in `MeetingTasksModule`
**Options considered:**
- (A) `services.AddTransient<PlaudPollingJob>()` in `MeetingTasksModule` (as spec/brief say).
- (B) Rely on `AddRecurringJobs()` assembly scan (which already does `AddScoped(typeof(IRecurringJob), jobType)` + `AddScoped(jobType)` for every Application-assembly `IRecurringJob`).
**Chosen approach:** (B).
**Rationale:** Pattern matches `LeafletIngestionJob`, `DailyConsumptionJob`, and every other Application-side job. Adding a Transient registration on top of the Scoped one creates lifetime ambiguity (Hangfire resolves via `GetService<PlaudPollingJob>()`, which would pick the last registration — likely Transient — and break Scoped dependencies like `IMediator`, `IRecurringJobStatusChecker`, EF repositories). Match the existing comment in `LeafletModule.cs:28`.

#### Decision 2: Build the process command line via `ProcessStartInfo.ArgumentList`, not interpolated `Arguments`
**Options considered:**
- (A) `Arguments = $"transcript {recordingId}"` (brief).
- (B) `ArgumentList.Add("transcript"); ArgumentList.Add(recordingId);`.
**Chosen approach:** (B).
**Rationale:** `ArgumentList` performs per-platform escaping. While `UseShellExecute=false` eliminates shell metacharacter risk on POSIX, on Windows .NET still concatenates the list into a single command-line string with quoting rules. Plaud recording IDs are externally sourced (CLI output) — we control format but should not assume it. Cost of switching is zero; benefit is removing a quoting class of bug entirely.

#### Decision 3: Kill the process explicitly when the timeout fires
**Options considered:**
- (A) Linked CTS only (brief — relies on cancellation to break the wait, but the OS process keeps running).
- (B) Linked CTS + `try { await WaitForExitAsync(linked.Token); } catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException(...); }`.
**Chosen approach:** (B).
**Rationale:** Hangfire will mark the job as failed if cancellation throws, but the orphan `plaud` process keeps holding stdout/stderr pipes and consumes resources. Explicit `Kill(true)` reclaims them. Also have read tasks observe the linked token so `ReadToEndAsync` unblocks.

#### Decision 4: Use the Czech-prompt + plain-text + manual JSON parsing path, NOT `GetResponseAsync<T>`
**Options considered:**
- (A) `await _chatClient.GetResponseAsync<List<ExtractedTask>>(messages, cancellationToken: ct)` (brief/spec).
- (B) Plain-text `GetResponseAsync(messages, ...)` + `JsonSerializer.Deserialize<List<ExtractedTask>>(text)`.
**Chosen approach:** (B).
**Rationale:** The current `AnthropicChatClient` (the only registered `IChatClient`) ignores `ChatOptions.ResponseFormat` and only forwards `model`, `max_tokens`, `system`, `messages` to the API. `GetResponseAsync<T>` from `Microsoft.Extensions.AI` falls back to prompt augmentation + JSON parsing; that augmentation may inject a separate system message that fights the existing Czech prompt's "Vrať POUZE JSON pole" instruction, and the inner `AnthropicChatClient` would drop or ignore it. Plain-text + `Deserialize` is deterministic, testable (mocking `IChatClient.GetResponseAsync` with a fixed text response is trivial), and matches the spec's "must never fail ingestion" guarantee already wrapped in try/catch. Strip Markdown code-fence prefix/suffix before deserializing.

#### Decision 5: Wrap each per-recording dispatch in `PlaudPollingJob.ExecuteAsync` with try/catch
**Chosen approach:** `foreach (var rec in ready) { try { … _mediator.Send … } catch (Exception ex) { _logger.LogError(ex, "Ingest failed {Id}", rec.Id); } }`.
**Rationale:** FR-8 requires per-recording error isolation, but the brief's reference code throws on the first failing recording (no per-iteration catch), which contradicts the spec. Without this, a single bad transcript fails the entire cycle and Hangfire retries the whole batch — duplicating successful work and amplifying the failure.

#### Decision 6: Use kebab-case `JobName`, include required `Description`
**Chosen approach:** `JobName = "plaud-polling"`, `DisplayName = "Plaud — pull meeting transcripts"`, `Description = "Polls Plaud CLI every 5 minutes for completed recordings, extracts action items via Claude, and stores them as proposed tasks awaiting human review."`, `CronExpression = "*/5 * * * *"`, `DefaultIsEnabled = false`.
**Rationale:** Matches every other job (`leaflet-ingestion`, `daily-consumption-calculation`, `knowledge-base-ingestion`, …). The brief's `"MeetingTasks.PlaudPolling"` would parse but visually clash with the rest of the Hangfire dashboard.

## Implementation Guidance

### Directory / Module Structure

```
backend/
├── src/
│   ├── Adapters/
│   │   └── Anela.Heblo.Adapters.Plaud/                            ← NEW project
│   │       ├── Anela.Heblo.Adapters.Plaud.csproj
│   │       ├── PlaudOptions.cs
│   │       ├── PlaudRecordingSummary.cs                           (class, public)
│   │       ├── IPlaudClient.cs
│   │       ├── PlaudCliClient.cs                                  (sealed class)
│   │       ├── PlaudTokenBootstrapper.cs                          (IHostedService)
│   │       └── PlaudAdapterServiceCollectionExtensions.cs
│   ├── Anela.Heblo.API/
│   │   └── Program.cs                                             ← add AddPlaudAdapter
│   └── Anela.Heblo.Application/
│       └── Features/MeetingTasks/                                 ← NEW feature folder
│           ├── MeetingTasksModule.cs                              (AddScoped<IMeetingTaskExtractor,…>)
│           ├── Services/
│           │   ├── IMeetingTaskExtractor.cs
│           │   └── ClaudeMeetingTaskExtractor.cs
│           ├── UseCases/IngestPlaudRecording/
│           │   ├── IngestPlaudRecordingRequest.cs                 (class — DTO rule)
│           │   ├── IngestPlaudRecordingResponse.cs                (class — DTO rule)
│           │   └── IngestPlaudRecordingHandler.cs
│           └── Infrastructure/Jobs/
│               └── PlaudPollingJob.cs
├── test/
│   ├── Anela.Heblo.Adapters.Plaud.Tests/                          ← NEW test project
│   │   ├── Anela.Heblo.Adapters.Plaud.Tests.csproj
│   │   └── PlaudCliClientParserTests.cs
│   └── Anela.Heblo.Tests/Features/MeetingTasks/
│       ├── ClaudeMeetingTaskExtractorTests.cs
│       └── IngestPlaudRecordingHandlerTests.cs
└── Dockerfile                                                     ← install plaud binary
```

Also touch:
- `backend/Anela.Heblo.sln` — add the two new projects to the solution.
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call `services.AddMeetingTasksModule(configuration)` inside `AddApplicationServices`.
- `backend/src/Anela.Heblo.API/Program.cs` — `builder.Services.AddPlaudAdapter(builder.Configuration)` after `AddAnthropicAdapter` (line 74).
- `backend/appsettings.json` — `"Plaud"` section (non-secret defaults only).

### Interfaces and Contracts

```csharp
// Anela.Heblo.Adapters.Plaud
public class PlaudOptions
{
    public const string SectionKey = "Plaud";
    public string CliExecutablePath { get; set; } = "plaud";
    public string TokensJson { get; set; } = string.Empty;
    public int ProcessTimeoutSeconds { get; set; } = 60;
    public int MaxRecordingAgeDays { get; set; } = 7;
}

public interface IPlaudClient
{
    Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default);
    Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default);
    Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default);
}

// Anela.Heblo.Application.Features.MeetingTasks.Services
public record ExtractedTask(string Title, string Description, string Assignee, DateTime? DueDate);
public interface IMeetingTaskExtractor
{
    Task<List<ExtractedTask>> ExtractAsync(string summary, string transcript, CancellationToken ct = default);
}

// Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording
public class IngestPlaudRecordingRequest : IRequest<IngestPlaudRecordingResponse>
{
    public string PlaudRecordingId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
}
public class IngestPlaudRecordingResponse
{
    public bool Success { get; set; } = true;
    public bool Skipped { get; set; }
    public Guid? TranscriptId { get; set; }
}
```

**RecurringJobMetadata (use the existing required-init shape):**
```csharp
public RecurringJobMetadata Metadata { get; } = new()
{
    JobName = "plaud-polling",
    DisplayName = "Plaud — pull meeting transcripts",
    Description = "Polls Plaud CLI every 5 minutes for completed recordings, extracts action items via Claude, and stores them as proposed tasks awaiting human review.",
    CronExpression = "*/5 * * * *",
    DefaultIsEnabled = false
};
```

**`MeetingTasksModule.AddMeetingTasksModule`:**
```csharp
public static IServiceCollection AddMeetingTasksModule(this IServiceCollection services, IConfiguration configuration)
{
    services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();
    // PlaudPollingJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs().
    // IMeetingTranscriptRepository is registered in PersistenceModule (subtask 1).
    // IngestPlaudRecordingHandler is auto-registered by MediatR assembly scan.
    return services;
}
```

### Data Flow

**Happy path (single new recording in a poll cycle):**
1. Hangfire fires `plaud-polling` cron → invokes `PlaudPollingJob.ExecuteAsync(ct)`.
2. Job calls `IRecurringJobStatusChecker.IsJobEnabledAsync("plaud-polling", ct)` → returns true only when operator has enabled it.
3. Job calls `IPlaudClient.ListRecentAsync(7, ct)` → `Process.Start("plaud", ["recent","--days","7"])` → stdout parsed by `PlaudCliClient.ParseFilesOutput` → list of `PlaudRecordingSummary`.
4. Filter `HasTranscript && HasSummary`. Log `{Total} found, {Ready} ready`.
5. For each ready recording (inside per-recording try/catch):
   a. `_mediator.Send(new IngestPlaudRecordingRequest { … }, ct)`.
   b. Handler: `repository.ExistsByPlaudIdAsync(id, ct)` → false.
   c. Handler: `_plaudClient.GetTranscriptAsync(id, ct)` + `_plaudClient.GetSummaryAsync(id, ct)` (sequential, two process invocations).
   d. Handler: `_extractor.ExtractAsync(summary, transcript, ct)` → list of `ExtractedTask` (empty list on Claude failure — already swallowed).
   e. Handler: construct `MeetingTranscript` (Status=`PendingReview`, ReceivedAt=`DateTime.UtcNow`, Tasks mapped to `ProposedTask` with Status=`Pending`, IsManuallyAdded=`false`).
   f. Handler: `repository.AddAsync(entity, ct)` + `repository.SaveChangesAsync(ct)`.
   g. Handler returns `{ Success=true, Skipped=false, TranscriptId=entity.Id }`.
6. Job increments `ingested`/`skipped` counters; logs `{Ingested} new, {Skipped} already known`.

**Idempotency path (recording already ingested):** Step 5b returns true → handler returns `{ Skipped=true }` immediately, no CLI calls, no extractor call, no DB writes.

**Failure isolation:** Step 5c/5e/5f exceptions are caught at the job level, logged with `PlaudRecordingId`, loop continues. Hangfire retry is not triggered for per-recording failures (only for the whole-job exception path, which is now reserved for truly catastrophic failures e.g. DB unreachable from the dedup check itself — but that's not currently scoped; document as accepted behavior).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Plaud CLI binary install mechanism is unspecified.** Spec says "install `plaud` CLI" in Dockerfile but doesn't name a distribution channel (npm? pip? curl release tarball? Go binary?). Without this, the container build will fail. | **HIGH (blocker)** | Resolve before merge: pick a channel, document in `docs/integrations/` (new file `plaud-cli.md`), pin the version, and add a smoke test stage in the Dockerfile that runs `plaud --version`. Treat as a prerequisite (see below). |
| **CLI output format is unverified.** Parser is a guess based on assumed column structure; brief explicitly says "adjust column format in tests to match actual output." A wrong parser silently returns zero or malformed records — symptom is "polling runs, no transcripts ingested, no errors." | **HIGH** | Capture real `plaud recent --days 7` (or `plaud files`) stdout from a logged-in CLI before merging implementation. Save the verbatim string as a test fixture file in `Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_recent_sample.txt`. Tests must load from that file, not inline-define a hypothetical. CI fails until fixture is committed. |
| **`GetResponseAsync<T>` may produce empty/wrong results against the current `AnthropicChatClient`** (no `ResponseFormat` support). | **MEDIUM** | Decision 4 — use plain-text response + `JsonSerializer.Deserialize<List<ExtractedTask>>`. Strip Markdown code-fence wrappers before deserializing. Spec amendment in next section. |
| **Process orphan on timeout.** Linked CTS cancels reads but does not kill the OS process. | **MEDIUM** | Decision 3 — explicit `process.Kill(entireProcessTree: true)` in catch + linked token passed to `ReadToEndAsync`. |
| **Sequential 60-s CLI timeouts × N recordings can exceed Hangfire's job slot.** Three ready recordings = up to 360 s of pure CLI wall-clock (list + transcript×3 + summary×3 = 7 invocations × 60 s worst case). | **LOW–MEDIUM** | Document the worst case in NFR-1. Optionally cap recordings per poll (e.g. process at most 5 per cycle) to stay deterministic. The next 5-min cycle picks up leftovers. |
| **Per-recording exception not isolated.** Brief job code throws on first failure; this contradicts FR-8. | **MEDIUM** | Decision 5 — wrap dispatch in per-iteration try/catch with structured log. Spec amendment below. |
| **Token file overwritten on every host start** with default file permissions (world-readable on Linux). | **LOW** | Inside a single-tenant container this is acceptable. Add a defensive `chmod 600` (`File.SetUnixFileMode` on .NET 7+) when writing on Unix. Document in NFR-2. |
| **CLI argument injection via recording IDs.** `Arguments = $"transcript {id}"` is shell-safe on POSIX (`UseShellExecute=false`) but quoted/parsed by .NET on Windows. | **LOW** | Decision 2 — switch to `ArgumentList`. |
| **MeetingTasksModule double-registration of PlaudPollingJob.** Brief instruction `AddTransient<PlaudPollingJob>()` conflicts with auto-discovery's Scoped registration. | **HIGH (correctness)** | Decision 1 — omit the manual registration. Add a code comment explaining auto-discovery (mirror `LeafletModule.cs:28`). |
| **Current branch is not based on epic.** `MeetingTranscript`, `ProposedTask`, `IMeetingTranscriptRepository` are missing locally — nothing compiles. | **HIGH (blocker)** | Prerequisite — rebase the feature branch onto `origin/feat/meeting-task-validation-epic` before any implementation work. PR target is `feat/meeting-task-validation-epic`, not `main`. |
| **Hangfire `DisableConcurrentExecution` is not applied anywhere in the existing infra.** A long-running poll could overlap with the next 5-min trigger. | **LOW** | Add `[DisableConcurrentExecution(timeoutInSeconds: 60)]` attribute on `PlaudPollingJob.ExecuteAsync` (already used implicitly via single Hangfire worker process in most deployments, but explicit is safer for the 5-min cadence). |

## Specification Amendments

1. **FR-2 / parser fixture provenance.** Amend FR-2 to require: "Parser tests load fixture from `Fixtures/plaud_recent_sample.txt`, captured verbatim from a real `plaud recent --days 7` run and committed to the repo. CI fails if the fixture file is missing or empty." Inline-string fixtures are disallowed.

2. **FR-4 / Claude call uses plain text.** Replace `GetResponseAsync<List<ExtractedTask>>` with `GetResponseAsync(messages, …)` returning text, followed by Markdown-fence stripping (````json … ````) and `JsonSerializer.Deserialize<List<ExtractedTask>>(text, options)` where `options` has `PropertyNameCaseInsensitive = true`. Tests stub `IChatClient.GetResponseAsync(...)` returning `ChatResponse` with a single assistant `ChatMessage` containing the JSON string.

3. **FR-6 / job registration.** Strike "Registered in `MeetingTasksModule` as `services.AddTransient<PlaudPollingJob>()`". Replace with: "`PlaudPollingJob` lives in `Anela.Heblo.Application` and is auto-discovered by `AddRecurringJobs()` as Scoped — do not add a manual registration."

4. **FR-6 / metadata shape.** Replace metadata snippet with the existing required-init pattern: `new RecurringJobMetadata { JobName = "plaud-polling", DisplayName = "...", Description = "...", CronExpression = "*/5 * * * *", DefaultIsEnabled = false }`. `JobName` is **kebab-case**, `Description` is **required**.

5. **FR-6 / per-recording try-catch.** Add to acceptance criteria: "Per-recording dispatch is wrapped in `try { … } catch (Exception ex) { _logger.LogError(ex, …); }`; the loop continues. Repository-write exceptions are caught here as well — they no longer propagate to Hangfire."

6. **FR-6 / concurrency guard.** Decorate `PlaudPollingJob.ExecuteAsync` with `[DisableConcurrentExecution(60)]`.

7. **FR-1 / Process invocation.** Use `ProcessStartInfo.ArgumentList.Add(...)` (one entry per token); on timeout, `process.Kill(entireProcessTree: true)` and translate `OperationCanceledException` into `TimeoutException` distinct from caller-driven cancellation.

8. **FR-9 / module wiring.** Add: "Create `MeetingTasksModule.AddMeetingTasksModule(IConfiguration)` and call it from `ApplicationModule.AddApplicationServices()` after `AddLeafletModule`. Add new projects (`Anela.Heblo.Adapters.Plaud`, `Anela.Heblo.Adapters.Plaud.Tests`) to `backend/Anela.Heblo.sln`."

9. **NFR-2 / token file permissions.** "On Unix-family runtimes, `PlaudTokenBootstrapper` sets `tokens.json` mode to `0600` via `File.SetUnixFileMode`."

10. **Open Questions** (was "None"). Now lists: Plaud CLI distribution channel and version pin (blocker for Dockerfile).

## Prerequisites

Must exist or be resolved **before** implementation starts:

1. **Branch base.** Rebase or recreate the working branch from `origin/feat/meeting-task-validation-epic`. The MeetingTasks domain types from Subtask 1 (`MeetingTranscript`, `ProposedTask`, `MeetingTranscriptStatus`, `ProposedTaskStatus`, `IMeetingTranscriptRepository`, EF Core configs, migration) live only on the epic branch and are not in the current worktree.

2. **Plaud CLI distribution channel.** Pick and document one of: official release binary (`curl … && install`), npm package, pip package, Go install. Pin a version. Required to write the Dockerfile change; otherwise the container build is undefined. Add a short note to `docs/integrations/plaud-cli.md` covering install command, version, and `plaud login` flow.

3. **Real CLI output fixture.** A verbatim `plaud recent --days 7` (or whatever the actual subcommand is) stdout captured against a logged-in CLI, committed as `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_recent_sample.txt`. The parser implementation and FR-2 tests must use this file as the source of truth.

4. **Subtask 1 PR merged (or at least its commits accessible).** The persistence migration `20260512191541_AddMeetingTasksTables` is referenced indirectly — handler writes through `IMeetingTranscriptRepository` which expects the EF Core model to be in sync. Manual `dotnet ef database update` is required on each environment (per project facts: "Database migrations are manual").

5. **Secret provisioning plan.** `Plaud__TokensJson` in dotnet user-secrets for local dev; corresponding Azure App Service configuration entry for production (`Plaud__TokensJson`). Token rotation procedure documented (out of scope to implement, but the runbook entry should exist before the job is enabled).

6. **Hangfire dashboard enable-step is documented.** Add a one-line operator note: after first deployment, open `/hangfire`, navigate to recurring jobs, locate `plaud-polling`, set to Enabled. Required because `DefaultIsEnabled = false`.