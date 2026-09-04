# Code Review: extract-log-helper-widen-fields

## Summary
The GET-only 400 log call in `McpBadRequestMiddleware` was extracted into a shared `LogBadMcpRequest` helper and widened to the full 11-field union schema, gated behind a shared `EventName = "McpBadRequest"` EventId (ids 5931/5932). The change is additive-only on the GET path, both the widened existing test and the new union-fields test pass, the full test class (19/19) passes, and the API project builds with 0 errors and no new warnings.

## Review Result: PASS

### task: extract-log-helper-widen-fields
**Status:** PASS

Verified:
- `LogBadMcpRequest(HttpContext, EventId, double elapsedMs)` emits all 11 required fields (`HTTPMethod`, `Path`, `StatusCode`, `UserAgent`, `Origin`, `Accept`, `ContentType`, `McpSessionIdPresent`, `McpSessionIdPrefix`, `RemoteIp`, `ElapsedMs`) as named placeholders, so the structured log state carries these exact keys regardless of message-template wording.
- `GetBadRequestEvent`/`PostBadRequestEvent` EventId constants added with ids 5931/5932 and shared `EventName = "McpBadRequest"`, matching the spec and the values the next task's own test (`add-post-diagnostics-logging`) expects (`e.Id == 5932`).
- GET branch calls `LogBadMcpRequest(context, GetBadRequestEvent, elapsedMs)`; elapsed time measured via `Stopwatch.GetTimestamp()`/`GetElapsedTime` captured at the top of the GET flow, per spec.
- No previously-emitted field was renamed or removed — additive only. Pre-existing GET tests (17) all still pass unmodified.
- `McpTelemetryHelpers.TruncateSessionId` reused for `McpSessionIdPrefix`, avoiding drift from `McpDiagnosticsMiddleware`'s own truncation, consistent with the plan.
- Test file: existing `InvokeAsync_GetMcpWith400Response_LogsWarningWithDiagnostics` widened to assert the new fields and `EventId.Name == "McpBadRequest"`; new `GetBadRequest_Log_IncludesAllUnionFields` added exactly as specified in the task context, asserting all 11 field names are present in the logged state.
- `dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"` → 19/19 passed.
- `dotnet build` (API project) → 0 errors, 0 new warnings from the touched files.

Reasonable, spec-aligned deviation: the message template uses each field's exact name as its literal `Name=Value` prefix (e.g. `McpSessionIdPresent={McpSessionIdPresent}`) rather than the abbreviated prefixes shown in the task's Step 3 snippet (`SidPresent=`, `UA=`, `IP=`). This was necessary because the task's own Step 1 test asserts the formatted log state's `ToString()` contains the full field names as literal substrings, and `FormattedLogValues.ToString()` substitutes placeholder values, not names — an abbreviated prefix would not satisfy that assertion. The impl notes flag this clearly and also flag that the next task's own draft assertion (`Contains("SidPresent=False")`) will need to be updated to `McpSessionIdPresent=False` to match; this is out of scope for this task and does not block it.

`PostBadRequestEvent` is intentionally unused until the next task wires up the POST branch, per the task's explicit instruction not to add POST handling yet.

## Docs to Update
(Omit — no public behavior or operational surface changed in this task; `update-mcp-server-docs` is a later task in the plan.)

## Overall Notes
Implementation matches the task context precisely, including the exact test code given verbatim in the spec. The one adjustment made (literal field-name prefixes instead of abbreviations) was required to make the spec's own test pass and is documented transparently in the impl notes for the next task's developer to account for.
