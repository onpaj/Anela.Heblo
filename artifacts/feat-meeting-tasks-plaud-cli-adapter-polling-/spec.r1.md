# Specification: Plaud CLI Adapter, Polling Job & Ingest Handler

## Summary
This feature introduces an autonomous backend pipeline that pulls completed meeting recordings from the Plaud cloud via its CLI, extracts structured action items via Claude, and stores them as proposed tasks awaiting human review. It replaces an earlier n8n email-webhook ingestion approach with a self-contained Hangfire polling job inside the Heblo monorepo, keeping the system entirely server-driven with no external triggers.

## Background
Meetings recorded by Plaud devices generate transcripts and summaries that today must be reviewed manually to extract action items. The parent epic ("Meeting Task Validation Checkpoint") introduces a human-in-the-loop workflow where AI-proposed tasks are reviewed before becoming work items. This subtask delivers the **ingestion half** of that workflow: detect new Plaud recordings, fetch their content, run Claude extraction, and persist proposed tasks in `PendingReview` state. The downstream review UI/handlers are out of scope here.

The original design depended on n8n forwarding Plaud emails to a webhook. That path was rejected because it adds an external dependency, complicates error handling, and is hard to test deterministically. The CLI-based polling approach keeps all logic inside Heblo, follows the existing `IRecurringJob` pattern (`DailyConsumptionJob`), and is straightforward to gate via `IRecurringJobStatusChecker`.

## Functional Requirements

### FR-1: Plaud CLI Adapter Project
A new adapter project `Anela.Heblo.Adapters.Plaud` shall encapsulate all interaction with the `plaud` CLI binary, mirroring the structure of `Anela.Heblo.Adapters.Anthropic`.

**Acceptance criteria:**
- Project contains `PlaudOptions`, `IPlaudClient`, `PlaudRecordingSummary`, `PlaudCliClient`, `PlaudTokenBootstrapper`, and `PlaudAdapterServiceCollectionExtensions`.
- `PlaudOptions` exposes `CliExecutablePath` (default `"plaud"`), `TokensJson` (default empty), `ProcessTimeoutSeconds` (default 60), `MaxRecordingAgeDays` (default 7), bound to configuration section `"Plaud"`.
- `IPlaudClient` exposes `ListRecentAsync(int days, CancellationToken)`, `GetTranscriptAsync(string recordingId, CancellationToken)`, `GetSummaryAsync(string recordingId, CancellationToken)`.
- `PlaudCliClient` invokes the CLI via `System.Diagnostics.Process` with `RedirectStandardOutput`, `RedirectStandardError`, `UseShellExecute=false`, `CreateNoWindow=true`.
- Process execution is bounded by `ProcessTimeoutSeconds` using a linked `CancellationTokenSource`.
- Non-zero exit code logs stderr at Error and throws `InvalidOperationException`; non-empty stderr with zero exit logs at Warning.
- `AddPlaudAdapter(IConfiguration)` registers options, `IPlaudClient → PlaudCliClient` as singleton, and `PlaudTokenBootstrapper` as `IHostedService`.

### FR-2: Plaud CLI Output Parser
`PlaudCliClient.ParseFilesOutput(string stdout)` shall convert tabular CLI output into a list of `PlaudRecordingSummary` items.

**Acceptance criteria:**
- Header row (first line) is skipped.
- Multi-word `NAME` columns are reconstructed by trimming from the right (last four whitespace-separated tokens are date, time, transcript flag, summary flag).
- `HasTranscript` / `HasSummary` are parsed case-insensitively from `"yes"` / non-`"yes"`.
- `CreatedAt` is parsed from `"{date} {time}"` via `DateTime.TryParse`; failure yields `default(DateTime)` without throwing.
- Empty input returns an empty list.
- Header-only input returns an empty list.
- Rows with fewer than 6 whitespace-separated tokens are skipped.
- Parser is exposed as `public static` to enable unit testing without a running CLI.

### FR-3: Plaud Token Bootstrapper
`PlaudTokenBootstrapper` shall materialize the OAuth tokens needed by the CLI from configuration at host startup.

**Acceptance criteria:**
- Implements `IHostedService`.
- On `StartAsync`: if `PlaudOptions.TokensJson` is empty/whitespace, logs a Warning and returns without writing files.
- When `TokensJson` is provided: ensures `~/.plaud/` exists (`Environment.SpecialFolder.UserProfile`), writes `tokens.json` with the raw configured content, logs the destination path at Information.
- `StopAsync` is a no-op.
- Runs before the polling job is allowed to execute (relies on hosted service ordering and the polling-job gate).

### FR-4: Claude Task Extractor
A service `ClaudeMeetingTaskExtractor` shall call Claude via `Microsoft.Extensions.AI.IChatClient` to produce a typed list of action items from a meeting summary + transcript.

**Acceptance criteria:**
- Public type `ExtractedTask(string Title, string Description, string Assignee, DateTime? DueDate)` defined as a `record`.
- Interface `IMeetingTaskExtractor` exposes `Task<List<ExtractedTask>> ExtractAsync(string summary, string transcript, CancellationToken ct = default)`.
- Implementation builds a prompt in Czech (matching the example body) instructing Claude to return ONLY a JSON array with `title`, `description`, `assignee`, `dueDate` (ISO date or null).
- Uses `IChatClient.GetResponseAsync<List<ExtractedTask>>(messages, cancellationToken: ct)`.
- Returns `Result ?? new List<ExtractedTask>()` on success.
- Any thrown exception is caught, logged at Warning ("Failed to extract tasks via Claude — transcript will be imported without tasks"), and an empty list is returned. Extraction failures must never fail ingestion.
- Registered in `MeetingTasksModule` as `services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>()`.

### FR-5: IngestPlaudRecording MediatR Handler
A handler shall idempotently persist a single Plaud recording (transcript + summary + AI-proposed tasks) as a `MeetingTranscript` in `PendingReview` state.

**Acceptance criteria:**
- Request DTO `IngestPlaudRecordingRequest` is a **class** (per project DTO rule), with `PlaudRecordingId`, `Name`, `PlaudCreatedAt` properties.
- Response DTO `IngestPlaudRecordingResponse` is a class with `Success` (default true), `Skipped` (default false), `TranscriptId` (nullable Guid).
- Handler signature: `IRequestHandler<IngestPlaudRecordingRequest, IngestPlaudRecordingResponse>`.
- Calls `IMeetingTranscriptRepository.ExistsByPlaudIdAsync(recordingId, ct)`; if true, logs Debug and returns `{ Skipped = true }` without further work.
- Otherwise calls `IPlaudClient.GetTranscriptAsync` and `IPlaudClient.GetSummaryAsync`.
- Calls `IMeetingTaskExtractor.ExtractAsync(summary, transcript, ct)`.
- Creates a `MeetingTranscript` with: new `Guid`, `PlaudRecordingId`, `PlaudCreatedAt`, `Subject = request.Name`, `Summary`, `RawTranscript`, `Status = MeetingTranscriptStatus.PendingReview`, `ReceivedAt = DateTime.UtcNow`, and a `Tasks` collection of `ProposedTask` (each with new `Guid`, copied fields, `Status = ProposedTaskStatus.Pending`, `IsManuallyAdded = false`).
- Calls `repository.AddAsync(entity, ct)` then `repository.SaveChangesAsync(ct)`.
- Logs Information on completion: ingested ID, name, task count.

### FR-6: Hangfire Polling Job
`PlaudPollingJob` shall poll the CLI every 5 minutes and dispatch an `IngestPlaudRecordingRequest` per ready recording.

**Acceptance criteria:**
- Implements `IRecurringJob` (existing project interface used by `DailyConsumptionJob`).
- `Metadata`: `JobName = "MeetingTasks.PlaudPolling"`, `DisplayName = "Plaud — pull meeting transcripts"`, `Cron = "*/5 * * * *"`, `DefaultIsEnabled = false`.
- `ExecuteAsync` returns immediately if `IRecurringJobStatusChecker.IsJobEnabledAsync(jobName, ct)` is false.
- Calls `IPlaudClient.ListRecentAsync(MaxRecordingAgeDays, ct)`.
- Filters to recordings where `HasTranscript && HasSummary`.
- Iterates ready list, dispatching `IngestPlaudRecordingRequest` via `IMediator.Send`; counts ingested vs skipped via the response.
- Logs Information at start ("{Total} found, {Ready} ready") and end ("{Ingested} new, {Skipped} already known").
- Registered in `MeetingTasksModule` as `services.AddTransient<PlaudPollingJob>()`. Auto-discovery via `RecurringJobDiscoveryService` registers the cron entry.
- Job is disabled by default; operator enables it via Hangfire UI or `IRecurringJobStatusChecker` once tokens are configured.

### FR-7: Idempotency & Deduplication
The pipeline shall never create duplicate `MeetingTranscript` rows for the same Plaud recording ID.

**Acceptance criteria:**
- `IMeetingTranscriptRepository.ExistsByPlaudIdAsync` is the single source of truth for "already ingested".
- Polling job re-processing of an already-ingested recording results in a no-op (response `Skipped = true`, no DB writes beyond the existence check).
- One failed extraction does not block other recordings in the same poll cycle (each `IngestPlaudRecording` dispatch is independent — see FR-8 for error scope).

### FR-8: Error Isolation
A failure ingesting one recording shall not abort the polling cycle for others. *(Assumption — see Open Questions.)*

**Acceptance criteria:**
- Recordings with `HasTranscript || HasSummary == false` are silently filtered out.
- CLI process failures during `GetTranscript`/`GetSummary` for a single recording surface as exceptions from `IPlaudClient`; the polling job catches per-recording exceptions, logs Error with the recording ID, and continues with the next item.
- Claude extraction failures are already absorbed inside `ClaudeMeetingTaskExtractor` (returns empty list).
- Repository failures (DB write) propagate to Hangfire so the job is retried per Hangfire's default retry policy.

### FR-9: Configuration & Registration
The adapter shall be wired into the API host so the feature works end-to-end after configuration is set.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.API/Program.cs` calls `builder.Services.AddPlaudAdapter(builder.Configuration)` after the existing `AddAnthropicAdapter` registration.
- `appsettings.json` includes the `"Plaud"` section with non-secret defaults: `CliExecutablePath`, `ProcessTimeoutSeconds`, `MaxRecordingAgeDays`.
- `Plaud__TokensJson` is supplied through user-secrets (local) and Azure App Service configuration (production) — never committed.
- `Dockerfile` installs the `plaud` CLI binary so it is available at runtime in the container image.

### FR-10: Unit Test Coverage
The feature shall include unit tests for each layer, all runnable offline (no CLI, no live Claude, no DB).

**Acceptance criteria:**
- `Anela.Heblo.Adapters.Plaud.Tests` contains `PlaudCliClientParserTests` covering: full parse of multi-row fixture, ready-recording detection, empty input, header-only input.
- `Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests` covers: success path returns parsed tasks; thrown `HttpRequestException` results in empty list (graceful degradation).
- `Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests` covers: new recording creates transcript + tasks in `PendingReview`; already-ingested skips without saving; empty extractor result still saves transcript with zero tasks.
- All tests follow AAA pattern, use Moq for collaborators, do not start `Process`, and pass without network access.
- Project-wide coverage stays at or above the 80% project minimum on the changed code.

## Non-Functional Requirements

### NFR-1: Performance
- Polling cadence: 5 minutes. Expected typical load: 0–3 ready recordings per cycle.
- Per-recording wall-clock budget: ≤ 60 s CLI timeout + Claude latency (typically ≤ 15 s) + DB write — comfortably within a Hangfire job slot.
- Parser must run in O(n) over CLI output lines; n is small (< 100 typically).
- The poller must not run more than one instance concurrently (Hangfire `DisableConcurrentExecution` semantics inherited from the existing recurring-job infrastructure).

### NFR-2: Security
- Plaud OAuth tokens are sensitive: never logged, never committed, only supplied via user-secrets or Azure App Service configuration. `Plaud:TokensJson` is the only secret value.
- Token bootstrapper writes `~/.plaud/tokens.json` with default OS permissions — acceptable inside a container (single tenant) but must be considered when running on a multi-user dev box.
- CLI is invoked with `UseShellExecute=false` and explicit argument strings. **No untrusted input is interpolated into the command line** (recording IDs come from the CLI's own output; they should not be shell-quoted, but the CLI must reject malformed IDs — see Open Questions).
- Claude API key is shared with the existing Anthropic adapter; no new secret surface.
- Transcript content may contain PII (names, internal discussions). Storage relies on existing DB protections; no additional encryption at rest is added here.

### NFR-3: Reliability
- Polling job disabled by default — must not break a Heblo deployment that lacks Plaud configuration.
- Token bootstrapper warns but does not throw when tokens are missing.
- Extractor failures degrade gracefully (empty task list) — the transcript is still preserved for human review.
- Per-recording errors do not abort the poll cycle.
- Hangfire retry policy handles transient DB/IO failures at the job level.

### NFR-4: Observability
- Information-level logs at job start/end with counts.
- Warning logs for: missing tokens, non-empty stderr from CLI, extractor failure.
- Error logs for: non-zero CLI exit, per-recording ingestion exception.
- Debug log on dedup skip.
- All logs include `PlaudRecordingId` where applicable.

### NFR-5: Maintainability
- File layout follows the existing adapter pattern (`Adapters.Anthropic` as reference).
- DTOs (`IngestPlaudRecordingRequest` / `Response`) are classes, not records — per project rule (OpenAPI codegen compatibility), even though this handler is currently internal-only.
- `ExtractedTask` may remain a record (internal domain type, not exposed via OpenAPI).
- The CLI parser is `public static` only because tests need it; reconsider if a richer API emerges.

## Data Model

This feature consumes (not defines) the `MeetingTranscript` aggregate and `IMeetingTranscriptRepository` produced by Subtask 1 of the parent epic.

**MeetingTranscript** (assumed shape, owned by Domain layer):
- `Id: Guid`
- `PlaudRecordingId: string` — unique dedup key
- `PlaudCreatedAt: DateTime`
- `Subject: string`
- `Summary: string`
- `RawTranscript: string`
- `Status: MeetingTranscriptStatus` (this handler sets `PendingReview`)
- `ReceivedAt: DateTime` (UTC, set at ingest)
- `Tasks: List<ProposedTask>`

**ProposedTask** (child entity):
- `Id: Guid`
- `Title: string`
- `Description: string`
- `Assignee: string`
- `DueDate: DateTime?`
- `Status: ProposedTaskStatus` (this handler sets `Pending`)
- `IsManuallyAdded: bool` (this handler sets `false`)

**PlaudRecordingSummary** (adapter DTO, not persisted):
- `Id: string`, `Name: string`, `CreatedAt: DateTime`, `HasTranscript: bool`, `HasSummary: bool`.

**ExtractedTask** (application service DTO, not persisted):
- `record (Title, Description, Assignee, DueDate)`.

**Required repository contract** (added or already present per Subtask 1):
- `Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct)`
- `Task AddAsync(MeetingTranscript entity, CancellationToken ct)`
- `Task SaveChangesAsync(CancellationToken ct)`

## API / Interface Design

### Internal MediatR contract
- Request: `IngestPlaudRecordingRequest : IRequest<IngestPlaudRecordingResponse>`
- Response: `{ bool Success, bool Skipped, Guid? TranscriptId }`
- No HTTP endpoint exposed in this subtask — request is only dispatched by the polling job.

### Adapter contract
```csharp
public interface IPlaudClient
{
    Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default);
    Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default);
    Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default);
}
```

### CLI invocations (subprocess)
- `plaud recent --days {N}` → tabular stdout parsed by `ParseFilesOutput`.
- `plaud transcript {recordingId}` → plain-text transcript on stdout.
- `plaud summary {recordingId}` → plain-text summary on stdout.

### Hangfire recurring job
- Job name: `MeetingTasks.PlaudPolling`
- Cron: `*/5 * * * *` (every 5 minutes, server timezone).
- Default state: **disabled**. Operator enables via Hangfire dashboard `/hangfire` once tokens are configured.

### Configuration shape
`appsettings.json`:
```json
"Plaud": {
  "CliExecutablePath": "plaud",
  "ProcessTimeoutSeconds": 60,
  "MaxRecordingAgeDays": 7
}
```
Secret (user-secrets / App Service):
```
Plaud__TokensJson = <raw JSON content of ~/.plaud/tokens.json>
```

## Dependencies

**Upstream (must exist before this feature compiles):**
- Subtask 1 of the epic: `Anela.Heblo.Domain.Features.MeetingTasks` (`MeetingTranscript`, `ProposedTask`, status enums, `IMeetingTranscriptRepository`), and a `MeetingTasksModule` DI registration entry-point.
- Existing `IRecurringJob` / `IRecurringJobStatusChecker` / `RecurringJobDiscoveryService` infrastructure (used by `DailyConsumptionJob`).
- Existing `Anela.Heblo.Adapters.Anthropic` registering `IChatClient`.

**External:**
- `plaud` CLI binary installed in the container image (Dockerfile change).
- Anthropic API (via existing adapter) for task extraction.
- Hangfire server already configured by the host.

**NuGet:**
- `Microsoft.Extensions.AI` (already in solution via Anthropic adapter).
- `MediatR` (already in solution).
- `Moq`, `xUnit` for tests (already in test infra).

**Branching:**
- Feature branch is cut from `feat/meeting-task-validation-epic` (the epic branch), not `main`.
- PR targets `feat/meeting-task-validation-epic`.

## Out of Scope

- Review/approve/reject UI and handlers for `ProposedTask` (Subtask 3+ of the epic).
- Exposing an HTTP endpoint to manually trigger ingest (job is the only caller in this subtask).
- Migration that creates `MeetingTranscripts` / `ProposedTasks` tables — owned by Subtask 1.
- Authentication/authorization on any new endpoints (none added here).
- Webhook/event-driven alternative to polling.
- Editing or reprocessing previously-ingested transcripts.
- Multi-account Plaud token rotation or per-user tokens — a single service account is assumed.
- Localization of the extractor prompt — Czech is hard-coded for this iteration.
- E2E tests covering this pipeline against a live CLI (manual verification step only).

## Open Questions

None.

## Status: COMPLETE