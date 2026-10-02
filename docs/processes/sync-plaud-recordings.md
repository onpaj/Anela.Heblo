---
process: sync-plaud-recordings
kind: sync
module: meeting-tasks
summary: Every 5 minutes pulls finished Plaud meeting recordings (transcript + AI summary) into MeetingTranscripts and has Claude extract participants and proposed action items into ProposedTasks for human review; also covers the manual re-import.
owns:
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/**
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/**
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUserDirectory.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingUserDirectory.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUser.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IPlaudClient.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/Plaud*.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Plaud/**
  - backend/src/Anela.Heblo.API/meeting-users.json
verified_at: "5e993f9e2"
related:
  - feed-meeting-tasks-to-planner
---

# Plaud recordings → meeting notes and proposed tasks

## Purpose
Turns meetings recorded on the Plaud voice recorder into meeting notes in Heblo (**Porady**,
page `/automation/meeting-tasks`): the full transcript, Plaud's AI summary, the list of
participants and a list of **proposed action items** with an assignee and due date. Nothing
is sent anywhere automatically — the proposed tasks wait in state *Pending* until someone with
write access approves or rejects them and sends the approved ones to Microsoft Planner
(`feed-meeting-tasks-to-planner`).

The same data is readable by Claude through the MCP tools `ListMeetings`, `GetMeetingSummary`,
`GetMeetingTranscript` and `GetMeetingTasks`, and Mind maps (module MindMaps) can attach a
meeting as input.

## Trigger
- **Hangfire job `plaud-polling`**, cron `*/5 * * * *` (every 5 minutes), category
  Integrations, display name "Plaud — pull meeting transcripts". `DefaultIsEnabled = false`:
  it does nothing until enabled in Recurring Jobs (the check runs at the start of each run).
  `[AutomaticRetry(Attempts = 0)]` — a failed run is not retried; the next cron tick is the retry.
- **Manual re-import** — button on the meeting detail page →
  `POST /api/meeting-tasks/{transcriptId}/reimport` (requires `anela.meetings.write`).

## Data flow
Polling run (`PlaudPollingJob` → MediatR `IngestPlaudRecordingRequest` per recording):
1. `plaud recent --days {MeetingTasks:MaxRecordingAgeDays}` (Plaud CLI, `PlaudCliClient`) lists
   recordings of the last 7 days: id, name, date. Ids arrive as `of_<32 hex>` and are stored
   **without** the `of_` prefix; rows whose id is not 32 lowercase hex chars are ignored.
2. For each recording (`IngestPlaudRecordingHandler`):
   1. Skip if `public."MeetingTranscripts"` already has that `PlaudRecordingId`.
   2. Skip if `public."DeletedPlaudRecordings"` has it (a user deleted the meeting — never re-import).
   3. `plaud file of_<id>` — skip ("not yet generated") unless both `transcript` and `summary`
      report `available`.
   4. `plaud transcript of_<id> -o <tmp>` → plain-text transcript.
   5. `plaud summary of_<id> -o <tmp>` → JSON; `header.headline` = headline,
      `ai_content` = Markdown summary (non-JSON output is taken as the summary verbatim).
   6. Claude extraction (`ClaudeMeetingTaskExtractor`, below) → participants + tasks.
   7. Insert one `MeetingTranscripts` row (status `PendingReview`, access `Private`,
      `ReceivedAt` = now UTC) and one `ProposedTasks` row per task (status `Pending`,
      `IsManuallyAdded = false`), in one `SaveChanges`.
3. After every successful CLI call the on-disk Plaud token is mirrored to Key Vault (see
   Configuration / quirks).

Manual re-import (`ReimportMeetingTranscriptHandler`) on an existing meeting:
`file` check (refused with BusinessRuleViolation if not generated) → transcript → summary →
`plaud recent --days N` (N = days since the recording, 1–365) to re-read the recording name →
overwrite `RawTranscript`, `Summary`, `Subject` (name, else headline, else unchanged) and
`Participants` → re-run extraction → **delete all `Pending` tasks** and insert the new ones.
`Approved` and `Rejected` tasks (incl. their Planner link) are kept. Status and access are
not changed.

## Logic & formulas
- **Subject** = recording name set in Plaud; if empty, the summary headline.
- **`PlaudCreatedAt`** = the date column of `plaud recent` (date only, no time); unparseable →
  `0001-01-01`. The list is ordered by it, newest first.
- **Extraction prompt** (Czech) asks Claude to return only JSON
  `{participants: [...], tasks: [{title, description, assignee, assigneeEmail, dueDate}]}`.
  The user message is `Souhrn: <summary>\n\nTranskript: <transcript>`; `MaxOutputTokens` 8192.
  The system prompt lists every user from `meeting-users.json` as `DisplayName (přezdívky: …) → email`
  and tells Claude to pick `assigneeEmail` only from that list.
- **Parsing**: Markdown code fences are stripped; if the text is not valid JSON, the first
  balanced `{…}` object inside it is tried. A response is rejected if it is not JSON or any
  task has an empty title. Participants are trimmed and de-duplicated case-insensitively.
  Zero tasks is a valid result (logged as a warning).
- **Retries**: up to 3 Claude calls. All 3 invalid → `MeetingTaskExtractionFailedException`
  → the recording is **not stored** (polling: counted as failed, retried on the next run;
  re-import: returns error `Exception`). A transport error (HTTP/timeout) on the **3rd** call
  instead returns an empty result → the meeting is stored with no tasks and no participants.
- **Assignee e-mail**: Claude's `assigneeEmail` if given; otherwise `meeting-users.json` lookup
  of the `assignee` name — exact case-insensitive match on display name or alias, and for
  names like "Jana & Petr" / "Jana, Petr" the first part that matches. No match → `null`
  (the task cannot be sent to Planner until someone picks a user).
- **Due date**: whatever ISO date Claude returns, else `null`. Not validated.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Recurring job `plaud-polling` enabled | off (`DefaultIsEnabled = false`) | Master switch, Recurring Jobs page |
| `MeetingTasks:MaxRecordingAgeDays` | 7 | Polling look-back window in days |
| `MeetingTasks:UserDirectoryPath` | `meeting-users.json` (21 users in repo) | People list for assignee matching; relative to the app directory, read once at start |
| `Plaud:CliExecutablePath` | `plaud` | Plaud CLI (`npm install -g @plaud-ai/cli` in the Dockerfile) |
| `Plaud:ProcessTimeoutSeconds` | 60 | Per CLI call; on timeout the process tree is killed |
| `Plaud:TokensJson` | empty (secret, KV `Plaud--TokensJson`) | Seeds `~/.plaud/tokens.json` at startup (`PlaudTokenBootstrapper`) |
| `Plaud:MaxRecordingAgeDays` | 7 | **Unused** — the job reads `MeetingTasks:MaxRecordingAgeDays` |
| `KeyVault:Uri` | env-specific | When set, rotated Plaud tokens are written back to KV secret `Plaud--TokensJson` |
| `KnowledgeBase:ChatModel` | `claude-sonnet-4-6` | Model of the shared Anthropic chat client used for extraction |
| `Anthropic:ApiKey`, `Anthropic:HttpTimeoutSeconds` | secret, 180 | Claude access and per-call timeout |

## Runtime facts
- `plaud-polling` is enabled in production; on 2026-09-29 12:05–13:50 Prague runs took
  ~300–440 s each because Claude kept returning malformed JSON ("Meeting task extraction
  failed after 3 attempts") — agent memory `gotcha_hangfire_single_worker_starves_print` — 2026-09-29.
- Production Hangfire runs with a single worker (`WorkerCount = 1`), shared with print jobs —
  same source — 2026-09-29.

## Known quirks
- **A recording whose extraction fails is retried on every run for 7 days.** Nothing is stored
  on failure, so each 5-minute run repeats the CLI calls and up to 3 Claude calls. With one
  Hangfire worker and no `DisableConcurrentExecution`, slow runs pile up and delay every other
  job — on 2026-09-29 manual picking-list prints waited 12–15 min and staff reported
  "printing does not work". After 7 days the recording silently drops out of the window and
  is never imported; re-import cannot rescue it because no meeting row exists.
- **Transport failure on the last attempt stores the meeting with zero tasks** and no
  participants; it is never re-extracted automatically. Use re-import.
- **Plaud auth recovery goes through Key Vault, not the App Service env var.** KV overrides the
  `Plaud__TokensJson` env var at startup. On CLI `AUTH_FAILED` the client refreshes the token
  once (`https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh`,
  form-encoded), writes disk then KV, and retries; a second failure throws
  `PlaudAuthExpiredException` and fails the run. The CLI also rotates the refresh token during
  normal calls; `SyncToKeyVaultAsync` mirrors it to KV only when the file changed. Runbook:
  `docs/integrations/plaud-token-auto-refresh.md` (agent memory `gotcha_plaud_token_kv_wins`).
- **Failure counters undercount**: an exception from a Plaud CLI call for one recording is
  logged and skipped but not added to the run's `failed` count. A failure of the initial
  `plaud recent` call fails the whole run (Hangfire Failed, no retry).
- **Re-import can duplicate tasks**: it keeps Approved/Rejected tasks and adds a fresh
  extraction, which usually proposes the same items again as Pending. A task that was sent to
  Planner and later switched back to Pending is deleted by re-import (its Planner task stays).
- **Deleted meetings stay deleted** even if the recording is still in Plaud (tombstone in
  `DeletedPlaudRecordings`, unique on `PlaudRecordingId`).
- **New meetings are Private**: read-only users (`anela.meetings.read`) do not see them until a
  writer changes access — see `module-meeting-tasks`.
- **`meeting-users.json` is baked into the image** (copied to output by `Anela.Heblo.API.csproj`);
  adding a person needs a code change and deploy. Missing or malformed file → empty directory
  (logged as error), Claude is told to set no e-mails.
- Two leftover files in the adapter (`IPlaudClient.cs`, `PlaudRecordingSummary.cs`) contain
  only comments; the real types live in `Application/Features/MeetingTasks/Services`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` — job id, cron, loop
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs` — skip rules and row creation
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs` — prompt, parsing, retries
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs` — manual re-import
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` — CLI commands, output parsing, auth retry
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefresher.cs` — token refresh and Key Vault mirroring
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingUserDirectory.cs` — name/alias → e-mail
