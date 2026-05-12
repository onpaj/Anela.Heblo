# Specification: Plaud CLI Adapter, Polling Job & Ingest Handler

## Summary
Replace the originally planned n8n email-webhook ingestion path with a Heblo-internal pipeline that periodically pulls completed meeting recordings from the Plaud cloud via the `plaud` CLI, extracts action items from each transcript using Claude (`IChatClient`), and persists both transcript and proposed tasks for human review. This subtask delivers the adapter, the Claude extractor, the MediatR ingest handler, and the Hangfire recurring job.

## Background
This is **Subtask 2** of the *Meeting Task Validation Checkpoint* epic. Subtask 1 (assumed completed in parallel/earlier) establishes the `MeetingTranscript` / `ProposedTask` domain model, repository, and persistence. Subsequent subtasks deliver review UI and task export.

The original design used n8n + email webhooks to ferry Plaud recordings into Heblo. That coupling is fragile and requires an external service. Plaud now ships a CLI capable of listing recent recordings and fetching transcripts and summaries; running it from Heblo directly via `System.Diagnostics.Process` removes the n8n dependency, keeps tokens inside the Heblo deployment, and makes the ingest flow testable end-to-end inside the .NET host.

Claude (already wired via the `Anela.Heblo.Adapters.Anthropic` adapter exposing `IChatClient`) provides structured action-item extraction. All ingested transcripts land in `PendingReview` so no task is auto-published to downstream task systems.

## Functional Requirements

### FR-1: Plaud CLI Adapter (`Anela.Heblo.Adapters.Plaud`)
A new adapter project encapsulates all interaction with the `plaud` CLI behind `IPlaudClient`, mirroring the structure of `Anela.Heblo.Adapters.Anthropic`.

**Acceptance criteria:**
- New project at `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/` containing `PlaudOptions`, `IPlaudClient`, `PlaudRecordingSummary`, `PlaudCliClient`, `PlaudTokenBootstrapper`, and `PlaudAdapterServiceCollectionExtensions`.
- `PlaudOptions` bound to configuration section `"Plaud"` with: `CliExecutablePath` (default `"plaud"`), `TokensJson` (default empty), `ProcessTimeoutSeconds` (default `60`), `MaxRecordingAgeDays` (default `7`).
- `IPlaudClient` exposes `ListRecentAsync(int days, CancellationToken)`, `GetTranscriptAsync(string recordingId, CancellationToken)`, `GetSummaryAsync(string recordingId, CancellationToken)`.
- `PlaudCliClient` invokes the CLI through `Process` with `RedirectStandardOutput`, `RedirectStandardError`, `UseShellExecute=false`, `CreateNoWindow=true`.
- Process execution honors `ProcessTimeoutSeconds` via a linked `CancellationTokenSource` and the caller's `CancellationToken`.
- Non-zero exit codes log stderr and throw `InvalidOperationException` with the exit code and stderr in the message.
- `AddPlaudAdapter(IConfiguration)` extension registers `PlaudOptions`, `IPlaudClient` (singleton → `PlaudCliClient`), and `PlaudTokenBootstrapper` as `IHostedService`.

### FR-2: CLI Output Parser
`PlaudCliClient.ParseFilesOutput(string stdout)` is a pure static method that converts `plaud` tabular stdout into `List<PlaudRecordingSummary>`. It must be unit-testable offline.

**Acceptance criteria:**
- Returns an empty list for an empty string.
- Returns an empty list when the input is header-only (no data rows).
- Parses each data row into `{ Id, Name, CreatedAt, HasTranscript, HasSummary }`.
- `Name` correctly captures multi-word names by positional indexing from the end of each row (last two columns are `TRANSCRIPT`/`SUMMARY`, prior two are date/time, everything between the ID and the date is the name).
- `HasTranscript` / `HasSummary` are `true` only when the column text equals `yes` (case-insensitive).
- `CreatedAt` parses via `DateTime.TryParse` from concatenated date + time columns; unparseable values yield `default(DateTime)` without throwing.
- Rows with fewer than 6 whitespace-separated columns are silently skipped.
- Parser fixture strings in unit tests must be replaced with the actual `plaud files` output captured locally after `plaud login`; the canonical fixture is committed alongside the test.

### FR-3: Plaud Token Bootstrapper
On host start, materialize `~/.plaud/tokens.json` from configuration so the CLI is logged in without an interactive login step.

**Acceptance criteria:**
- `PlaudTokenBootstrapper` implements `IHostedService`.
- `StartAsync` returns immediately and logs a warning when `Plaud:TokensJson` is empty or whitespace; no file is written and the polling job remains effectively disabled.
- When `TokensJson` is present, the bootstrapper creates `~/.plaud/` (no-op if it exists) and writes the raw JSON to `~/.plaud/tokens.json`, overwriting any prior content.
- An information-level log records the written path on success.
- `StopAsync` is a no-op.
- Path resolution uses `Environment.GetFolderPath(SpecialFolder.UserProfile)` so it works in both local dev and the Linux container (where `$HOME` is set for the app user).

### FR-4: Claude Meeting Task Extractor
Convert a recording's summary + transcript into structured action items using `IChatClient`.

**Acceptance criteria:**
- `ExtractedTask` is a record with `Title`, `Description`, `Assignee`, `DueDate (DateTime?)`.
- `IMeetingTaskExtractor.ExtractAsync(string summary, string transcript, CancellationToken)` returns `List<ExtractedTask>`.
- `ClaudeMeetingTaskExtractor` builds a Czech-language prompt instructing Claude to output a JSON array with exactly the fields above and to return JSON only (no prose).
- Uses `IChatClient.GetResponseAsync<List<ExtractedTask>>(...)` for structured-output parsing.
- Returns `response.Result ?? new List<ExtractedTask>()` on success.
- Catches any exception from the chat client, logs a warning with the exception, and returns an empty list — extraction failures never propagate to the caller and never block transcript persistence.
- Registered as scoped `IMeetingTaskExtractor → ClaudeMeetingTaskExtractor` in `MeetingTasksModule`.

### FR-5: Ingest Handler (`IngestPlaudRecording`)
A MediatR request/handler pair that, given a Plaud recording reference, fetches transcript + summary, extracts tasks, and persists a `MeetingTranscript` in `PendingReview`.

**Acceptance criteria:**
- Request shape: `{ PlaudRecordingId: string, Name: string, PlaudCreatedAt: DateTime }`.
- Response shape: `{ Success: bool=true, Skipped: bool=false, TranscriptId: Guid? }`.
- If `IMeetingTranscriptRepository.ExistsByPlaudIdAsync(request.PlaudRecordingId, ct)` returns true, the handler returns `{ Skipped = true }` immediately without calling Plaud, the extractor, or the repository writers.
- Otherwise the handler calls `GetTranscriptAsync`, then `GetSummaryAsync`, then `ExtractAsync(summary, transcript)`, then constructs a new `MeetingTranscript` with: fresh `Guid` Id, `PlaudRecordingId`, `PlaudCreatedAt`, `Subject = request.Name`, `Summary`, `RawTranscript`, `Status = PendingReview`, `ReceivedAt = DateTime.UtcNow`, and a `Tasks` collection of `ProposedTask` rows (`Status = Pending`, `IsManuallyAdded = false`).
- Calls `AddAsync` then `SaveChangesAsync` exactly once each; returns the new transcript id.
- Logs an info entry on success including recording id, name, and proposed task count.
- An empty `ExtractedTask` list produces a saved `MeetingTranscript` with zero `Tasks` and `Success = true` — extraction failure does not skip persistence.

### FR-6: Hangfire Polling Job (`PlaudPollingJob`)
A recurring job that lists recent Plaud recordings and dispatches an `IngestPlaudRecordingRequest` for each one that is fully processed on Plaud's side.

**Acceptance criteria:**
- Implements `IRecurringJob` with metadata: `JobName = "MeetingTasks.PlaudPolling"`, `DisplayName = "Plaud — pull meeting transcripts"`, `Cron = "*/5 * * * *"`, `DefaultIsEnabled = false`.
- `ExecuteAsync` returns early (no-op) when `IRecurringJobStatusChecker.IsJobEnabledAsync(JobName, ct)` is false.
- Calls `IPlaudClient.ListRecentAsync(_options.MaxRecordingAgeDays, ct)`.
- Filters recordings to those with both `HasTranscript && HasSummary` ("ready") before dispatching.
- For each ready recording, sends `IngestPlaudRecordingRequest` via `IMediator`, tallies `Ingested` / `Skipped` based on response, and logs both before and after totals.
- Registered as transient in `MeetingTasksModule`; auto-discovered by `RecurringJobDiscoveryService`.
- Disabled by default — enabled via Hangfire UI / status checker after `Plaud__TokensJson` is configured.

### FR-7: API Composition Root Wiring
The Plaud adapter must be wired into the API host alongside the existing adapters.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.API/Program.cs` calls `builder.Services.AddPlaudAdapter(builder.Configuration)` immediately after `AddAnthropicAdapter(...)`.
- `MeetingTasksModule.cs` registers `IMeetingTaskExtractor → ClaudeMeetingTaskExtractor` (scoped) and `PlaudPollingJob` (transient).
- Project references: `Anela.Heblo.Application` references `Anela.Heblo.Adapters.Plaud`; `Anela.Heblo.API` references `Anela.Heblo.Adapters.Plaud`.

### FR-8: Container Image Includes Plaud CLI
The Docker image must ship with a working `plaud` binary on `PATH`.

**Acceptance criteria:**
- `Dockerfile` installs the `plaud` CLI (binary or via the documented install method) so that `plaud --version` succeeds inside the running container.
- The CLI is reachable using the default `CliExecutablePath = "plaud"`; no absolute path required in config for the standard image.
- Image build remains a single image (per project conventions).

## Non-Functional Requirements

### NFR-1: Performance & Schedule
- Polling cadence: every 5 minutes (`*/5 * * * *`).
- Per-CLI-invocation timeout: 60 seconds (configurable via `ProcessTimeoutSeconds`).
- Recording age window: 7 days back from "now" (configurable via `MaxRecordingAgeDays`).
- Single job run processes ready recordings sequentially; no concurrency requirement beyond what Hangfire already provides per job.

### NFR-2: Security & Secrets
- `Plaud:TokensJson` is a secret (full content of `~/.plaud/tokens.json`) and must be supplied via `secrets.json` locally and via Azure App Service application settings in production (`Plaud__TokensJson`). It must not be committed to source.
- Anthropic API key is already managed by the existing `Anela.Heblo.Adapters.Anthropic` adapter; no new secret is introduced for Claude.
- Token file is written to the app user's home directory with default file permissions; the container runs as a non-root, low-privilege user (project convention).
- Logs must never include token contents, transcript text, or summary text; only ids, names, counts, and exit codes/stderr are loggable.
- All transcripts land in `PendingReview` — no automatic export to external task systems before human approval (enforced by the parent epic's review flow).

### NFR-3: Reliability & Resilience
- Extractor failures (Claude API errors, malformed JSON) do **not** block transcript persistence — the transcript is saved with zero proposed tasks and a warning is logged.
- Dedup is enforced by `ExistsByPlaudIdAsync` keyed on `PlaudRecordingId` so repeated polls are idempotent.
- CLI non-zero exits surface as a logged error and a thrown `InvalidOperationException` from `IPlaudClient`; the handler does not catch it, so the failure is recorded by Hangfire and surfaced in the dashboard for the operator. The next 5-minute tick retries.
- Missing/empty `Plaud:TokensJson` produces a startup warning, the bootstrapper no-ops, and the polling job is effectively disabled — the host still starts.

### NFR-4: Testability
- Parser logic is a pure static method covered by offline unit tests with committed fixture strings.
- Extractor is mockable via `IChatClient` and covered by tests for the happy path and the failure path.
- Ingest handler is covered by tests for: new recording happy path, already-ingested skip, and extractor-returns-empty success-with-zero-tasks.
- All tests use Moq and xUnit, matching existing project conventions.

### NFR-5: Observability
- Polling job logs: counts found, counts ready, counts ingested vs. skipped per tick.
- Ingest handler logs: recording id, name, proposed task count per success; skip reason at Debug level.
- CLI failures log: arguments, exit code, stderr (capped/whole, depending on size — no transcript bodies).
- Hangfire dashboard exposes job state and last error trace via existing infrastructure.

## Data Model

This subtask **consumes** the domain model established in Subtask 1 of the parent epic:

- **MeetingTranscript** (root)
  - `Id: Guid`
  - `PlaudRecordingId: string` (dedup key — assumed indexed unique)
  - `PlaudCreatedAt: DateTime`
  - `Subject: string`
  - `Summary: string`
  - `RawTranscript: string`
  - `Status: MeetingTranscriptStatus` — values include at minimum `PendingReview`
  - `ReceivedAt: DateTime`
  - `Tasks: List<ProposedTask>`
- **ProposedTask** (child of MeetingTranscript)
  - `Id: Guid`
  - `Title: string`
  - `Description: string`
  - `Assignee: string`
  - `DueDate: DateTime?`
  - `Status: ProposedTaskStatus` — `Pending` for newly extracted tasks
  - `IsManuallyAdded: bool` — `false` for Claude-extracted tasks

- **IMeetingTranscriptRepository** must expose at least: `ExistsByPlaudIdAsync`, `AddAsync`, `SaveChangesAsync` (used by this subtask). Other read APIs are owned by the review-UI subtask.

No schema changes in this subtask — all persistence shapes are inherited from Subtask 1.

## API / Interface Design

No HTTP endpoints. The feature surfaces are:

- **Hangfire recurring job:** `MeetingTasks.PlaudPolling` (`*/5 * * * *`, disabled by default). Trigger / enable from `/hangfire`.
- **MediatR contract:** `IngestPlaudRecordingRequest → IngestPlaudRecordingResponse` (internal; called by the polling job, not exposed externally).
- **Adapter contract:** `IPlaudClient` (`ListRecentAsync`, `GetTranscriptAsync`, `GetSummaryAsync`).
- **Configuration surface:**
  - `appsettings.json` (non-secret defaults):
    ```json
    "Plaud": {
      "CliExecutablePath": "plaud",
      "ProcessTimeoutSeconds": 60,
      "MaxRecordingAgeDays": 7
    }
    ```
  - Secret (Azure App Setting / local `secrets.json`):
    - `Plaud__TokensJson` = raw content of `~/.plaud/tokens.json` produced by `plaud login` locally.

## Dependencies

- **Subtask 1 of parent epic** — must have delivered: `MeetingTranscript`, `ProposedTask`, `MeetingTranscriptStatus`, `ProposedTaskStatus`, `IMeetingTranscriptRepository` (with `ExistsByPlaudIdAsync`, `AddAsync`, `SaveChangesAsync`), and EF Core configuration with a unique index on `PlaudRecordingId`.
- **`Anela.Heblo.Adapters.Anthropic`** — provides `IChatClient` registered for Claude with structured-output support (`GetResponseAsync<T>`).
- **Hangfire infrastructure** — existing `IRecurringJob`, `RecurringJobDiscoveryService`, `IRecurringJobStatusChecker`, and Hangfire dashboard at `/hangfire`.
- **MediatR** — already in use throughout the application.
- **`plaud` CLI** — third-party binary, supports commands `login`, `recent --days <n>` (used to list recordings), `transcript <id>`, `summary <id>`. Distributed by the Plaud vendor; installed into the Docker image.
- **Microsoft.Extensions.AI** — for `IChatClient`, `ChatMessage`, `ChatRole`, `ChatResponse<T>` (already referenced through the Anthropic adapter).

## Out of Scope

- Review UI for proposed tasks (separate subtask).
- Approving / rejecting / editing tasks (separate subtask).
- Exporting approved tasks to any external task system (separate subtask).
- Refreshing or rotating Plaud tokens automatically — manual re-`plaud login` + secret update is acceptable for this iteration.
- Streaming/chunked transcript ingestion for very long recordings — full text is fetched and stored as-is.
- Removing the legacy n8n email-webhook ingestion path if any is wired (none expected; this epic replaces it).
- Multi-tenant / per-user Plaud accounts — one shared Plaud account per Heblo deployment.
- Retries with backoff inside the job — relying on the next 5-minute tick and Hangfire's built-in retry semantics is sufficient.
- Localization of the extraction prompt beyond Czech.

## Open Questions

None.

## Status: COMPLETE