# Specification: Fix Smartsupp Webhook 500 Errors Caused by Oversized String Fields

## Summary
The `POST api/webhooks/smartsupp` endpoint returns HTTP 500 when an incoming Smartsupp payload contains string content exceeding the `varchar(500)` limits configured on `SmartsuppConversations` columns (`Subject`, `ContactAvatarUrl`, `Referer`). EF Core throws `Npgsql.PostgresException 22001` during `SaveChangesAsync` and Smartsupp retries the delivery, amplifying the failure rate. This feature widens the affected persistence columns, defensively guards the application layer against oversized strings, and adds regression coverage so the webhook absorbs realistic Smartsupp content without losing data or returning 5xx.

## Background
Telemetry captured 6 × HTTP 500 responses against `SmartsuppWebhook/Receive` in a 4-second burst at 2026-06-13 12:25 UTC (3 distinct `operation_Id` values, indicating Smartsupp's automatic retry behaviour). Root cause: `Npgsql.PostgresException: 22001: value too long for type character varying(500)` raised from `SmartsuppRepository.SaveChangesAsync` via `ProcessWebhookEventHandler.Handle`.

Audit of `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppConversationConfiguration.cs` shows four `HasMaxLength(500)` columns on `SmartsuppConversation`:
- `Subject` (line 15)
- `ContactAvatarUrl` (line 19)
- `Referer` (line 22)
- `LastMessagePreview` (line 27) — already protected by application-level truncation in `SmartsuppPayloadMapper.MapConversation` (constant `LastMessagePreviewMaxLength = 200`).

The remaining three columns are populated directly from raw Smartsupp payload strings (`subject`, `contact_avatar_url`, `referer`) with no truncation or validation. `Referer` is the most probable trigger — origin URLs from third-party advertising and tracking platforms frequently exceed 500 characters once query parameters and UTM fragments are included.

No code change was deployed in the surrounding window. The incident is therefore a content-driven failure, not a regression, and is expected to recur on any payload carrying long strings in these fields.

The controller (`SmartsuppWebhookController.Receive`) already wraps the mediator call in a try/catch and returns `Ok()` on handler failure (lines 158–171). The observed 500 must therefore originate from a database write that occurs **outside** that catch — most likely `_audit.UpdateOutcomeAsync` flushing the audit entry, or a synchronous exception thrown before the catch is reached. This must be confirmed during implementation; the fix must cover both the persistence schema (so the underlying violation stops) and the controller boundary (so any future persistence failure cannot escape as 5xx).

## Functional Requirements

### FR-1: Widen oversized varchar(500) columns on SmartsuppConversations
Update the EF Core entity configuration and add a database migration so the affected columns can hold realistic Smartsupp payload values without truncation.

**Scope of change:**
- `SmartsuppConversation.Subject` — increase to `varchar(2000)`.
- `SmartsuppConversation.ContactAvatarUrl` — increase to `varchar(2000)`.
- `SmartsuppConversation.Referer` — change to unlimited `text` (URLs have no meaningful upper bound and are not indexed).
- `LastMessagePreview` (varchar(500)) — leave as-is; already truncated to 200 chars in the mapper.

**Acceptance criteria:**
- `SmartsuppConversationConfiguration` reflects the new column widths.
- A new EF Core migration is generated under `backend/src/Anela.Heblo.Persistence/Migrations/` that alters the three columns. Migration uses `ALTER TABLE ... ALTER COLUMN ... TYPE varchar(2000)` / `TYPE text`, never `DROP/RECREATE`.
- Migration runs cleanly forward and backward against PostgreSQL (manual deploy per project convention).
- `dotnet build` and `dotnet format` pass.

### FR-2: Defensive truncation at the application boundary
Add explicit, logged truncation in `SmartsuppPayloadMapper.MapConversation` so any future column-size mismatch (e.g. unexpected schema drift, new long fields) cannot cause `DbUpdateException`. Truncation is silent storage of the truncated value plus a structured log entry; raw original content is already preserved in `SmartsuppWebhookAuditEntry.RawBody`.

**Scope of change:**
- Introduce per-field max-length constants in `SmartsuppPayloadMapper` (matching the new column widths from FR-1).
- Truncate `subject`, `contact_avatar_url`, and `referer` defensively before constructing `SmartsuppConversation`.
- Log a `LogWarning` with structured properties (`Field`, `OriginalLength`, `TruncatedLength`, `ConversationId`) when truncation occurs.
- Apply the same defensive pattern to other free-text fields populated from the payload that map to bounded columns: `ContactName` (200), `ContactEmail` (200), `Domain` (200), `LocationCountry` (100), `LocationCity` (100), `LocationIp` (50), `LocationCode` (10), `CloseType` (50), `Channel` (50), `RatingText` (1000).

**Acceptance criteria:**
- Unit tests in `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Mappers/SmartsuppPayloadMapperTests.cs` cover:
  - Subject longer than the limit is truncated and logged.
  - Referer longer than the limit is truncated and logged.
  - Each bounded field truncates correctly at its own limit.
  - Strings at or below the limit pass through unchanged.
- Truncation is byte-safe for UTF-8: must not split a multi-byte grapheme (Smartsupp messages may contain emoji or non-Latin characters). Use `StringInfo`/string slicing by code unit at a boundary that does not corrupt the string.

### FR-3: Audit-writer hardening
Investigate and harden the audit write path so a future schema-size violation in the audit table cannot bubble out as HTTP 500.

**Scope of change:**
- Review `SmartsuppWebhookAuditWriter.CreateAsync` and `UpdateOutcomeAsync` for `HasMaxLength` columns that could receive oversized values from `SmartsuppWebhookAuditEntry` (e.g. `RemoteIp`, `SignatureHeader`, `EventName`, `AccountId`, `AppId`, `ProcessingError`).
- Where columns are bounded, truncate the input defensively before persisting (same logging pattern as FR-2).
- `SmartsuppWebhookController.Receive`: wrap the `_audit.UpdateOutcomeAsync` calls inside the success and failure branches in their own try/catch so audit-write failures degrade to a log line and an `Ok()` response, not 500.

**Acceptance criteria:**
- New/updated tests in `SmartsuppWebhookAuditWriterTests` and `SmartsuppWebhookControllerTests` prove:
  - An audit write that throws `DbUpdateException` still returns HTTP 200 to the caller.
  - The exception is logged with structured context (`AuditId` when available, `EventName`, exception details).
- No raw exception details or stack traces are returned in the HTTP response body.

### FR-4: Regression tests for oversized payloads
Add automated tests that simulate the failure mode end-to-end so this class of bug cannot reappear silently.

**Scope of change:**
- `ProcessWebhookEventHandlerTests` (or a sibling test class) gains a parameterised test that posts a `conversation_*` event whose `subject` / `referer` / `contact_avatar_url` exceeds the column width, and verifies:
  - The handler completes without throwing.
  - `SmartsuppConversations` row is persisted with the truncated (or now-fitting) value.
  - A warning log is emitted naming the field.
- `SmartsuppWebhookControllerTests` gains a test that posts the same oversized payload via the controller and asserts the response is HTTP 200.

**Acceptance criteria:**
- All new and existing tests pass: `dotnet test backend/test/Anela.Heblo.Tests` covering the Smartsupp test suite.
- Coverage for the affected files (`SmartsuppPayloadMapper`, `SmartsuppWebhookController`, `SmartsuppWebhookAuditWriter`) remains at or above 80%.

### FR-5: Replay capability for the affected events
The brief's "Minimal next step" mentions replaying the failed webhooks via the existing `tools/SmartsuppWebhookReplay` tool. After the fix is deployed, the operator must be able to replay the captured payloads from the audit log to restore data parity with Smartsupp.

**Scope of change:**
- Verify that `SmartsuppWebhookAuditEntry.RawBody` preserved the offending payloads from the 2026-06-13 burst (the controller writes the audit entry before the handler runs, so they should be intact).
- Confirm `ReplayWebhookEventHandler` re-processes a stored audit entry by ID against the current persistence pipeline, exercising the new truncation logic.
- No new code is mandatory unless the replay flow has gaps; documentation (a one-paragraph operator note in this PR description) is sufficient if the existing tool already covers the scenario.

**Acceptance criteria:**
- Operator can replay an audit entry recorded before the fix and have it succeed against the new schema.
- Replay test in `ReplayWebhookEventHandlerTests` (if not already present) confirms the round-trip.

## Non-Functional Requirements

### NFR-1: Performance
- Migration `ALTER COLUMN ... TYPE varchar(2000)` and `TYPE text` on `SmartsuppConversations` is metadata-only on PostgreSQL when the new size is greater than the old and no `USING` clause is required; it should not rewrite the table. Confirm during testing on staging that the migration completes in seconds, not minutes.
- Defensive truncation in the mapper adds O(1) length check and at most one substring per field per webhook event; negligible compared to network and EF Core overhead. No new allocations on the happy path.

### NFR-2: Security
- Audit-trail integrity: the original (untruncated) payload is preserved in `SmartsuppWebhookAuditEntry.RawBody` (text column). No PII or business data is lost.
- Log statements added by truncation must not include the truncated content itself, only the metadata (`Field`, `OriginalLength`, `TruncatedLength`, `ConversationId`). This avoids accidentally logging PII (visitor messages, email subjects).
- No change to the HMAC verification path or to anonymous access on the endpoint.

### NFR-3: Observability
- All truncation events emit `LogWarning` with a stable message template so they can be alerted on in Application Insights.
- A new counter / metric on `ISmartsuppWebhookMetrics` records truncation occurrences by field name. Operators can quantify how often the new ceilings are still being exceeded and decide whether further widening is warranted. (Optional — implement if the metrics abstraction makes it a one-line addition; otherwise capture in Open Questions.)

### NFR-4: Reliability
- Smartsupp retries on 5xx. The fix is judged successful when the same payload that triggered the 2026-06-13 burst can be delivered (or replayed) once and yields a single HTTP 200 with the conversation persisted.

## Data Model

Existing entity `SmartsuppConversation` (no domain shape change — only column widths):

| Property | Old DB type | New DB type | Notes |
|---|---|---|---|
| `Subject` | `varchar(500)` | `varchar(2000)` | Free-form text from `subject` payload field |
| `ContactAvatarUrl` | `varchar(500)` | `varchar(2000)` | URL — practical headroom for CDN URLs with signing tokens |
| `Referer` | `varchar(500)` | `text` | URL with arbitrary query string; unbounded is safer than guessing |
| `LastMessagePreview` | `varchar(500)` | `varchar(500)` *(unchanged)* | Already truncated to 200 in mapper |

No new entities, no new tables, no foreign-key changes.

## API / Interface Design
- No changes to the public webhook contract (`POST /api/webhooks/smartsupp`).
- No changes to controller routes, response shapes, or status codes on the happy path.
- Failure-mode contract change: oversized payloads now return HTTP 200 (currently return HTTP 500). Smartsupp does not retry on 200, so this also stops the retry storm observed in telemetry.

## Dependencies
- Entity Framework Core / Npgsql — existing.
- Existing migration tooling (`dotnet ef migrations add ...`).
- Existing `ISmartsuppWebhookMetrics` (optional, for NFR-3).
- `tools/SmartsuppWebhookReplay` for post-deploy replay.

## Out of Scope
- Changing the Smartsupp domain entities beyond the column-width adjustments above.
- Widening every `HasMaxLength` column across the Smartsupp schema. Only fields demonstrably populated from free-form Smartsupp content and at risk of overflow are touched. Other bounded fields (e.g. status enums, IDs from Smartsupp's API which have known formats) keep their existing limits.
- Refactoring the webhook controller's transactional shape (e.g. moving audit writes into the handler, introducing outbox pattern). Audit-write hardening in FR-3 is a targeted fix, not an architectural redesign.
- Backfilling historical truncated values. The audit log preserves originals; if business need arises, a separate one-off backfill task can be scoped from `RawBody`.
- Changes to Smartsupp's own configuration (e.g. asking Smartsupp to truncate or omit fields). The fix is owned end-to-end in this codebase.

## Open Questions
None.

## Status: COMPLETE