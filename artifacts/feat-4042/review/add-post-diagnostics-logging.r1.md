# Code Review: add-post-diagnostics-logging

## Summary
The implementation adds a self-contained `IsMcpPostRequest`/`HandlePostAsync` pair that mirrors the existing GET path exactly: it awaits `_next(context)`, checks for a 400 response, and on 400 calls the shared `LogBadMcpRequest` with the pre-existing `PostBadRequestEvent` — never touching status code, body, or headers. The required P1 test is present, correctly exercises the behavior, and was committed alongside the middleware change in `6d51fc0c`. Manual trace of every branch combination (non-`/mcp`, GET `/mcp`, POST `/mcp`, other methods on `/mcp`) confirms no other request path's behavior changed.

## Review Result: PASS

### task: add-post-diagnostics-logging
**Status:** PASS

## Overall Notes

**Spec compliance / correctness** — Verified against the real diff (`git show 6d51fc0c`), not just the impl summary:
- `HandlePostAsync` awaits `_next(context)` first, then checks `context.Response.StatusCode == StatusCodes.Status400BadRequest` before calling `LogBadMcpRequest(context, PostBadRequestEvent, elapsedMs)`. This is the same "observe-then-log" shape as the existing GET path's post-`_next` block (FR-2 satisfied).
- The handler makes no writes to `context.Response.StatusCode`, `.Body`, or `.Headers` anywhere in its body — confirmed by reading the full method.
- `PostBadRequestEvent` (EventId 5932, name `"McpBadRequest"`) was already declared pre-existing in the file (from an earlier task in this pipeline) and is reused correctly, distinct from `GetBadRequestEvent` (5931).

**Branch-ordering deviation** — The developer placed `if (IsMcpPostRequest(context)) { await HandlePostAsync(context); return; }` as the very first check in `InvokeAsync`, ahead of the pre-existing `if (!IsMcpGetRequest(context)) { await _next(context); return; }` guard, rather than literally between the GET branch and a final fallthrough (the GET branch here is inlined, not extracted into a `HandleGetAsync`, matching the task's fallback instruction to leave GET inline if that's how it already is). This is a reasonable, explicitly-permitted deviation: `IsMcpPostRequest` and `IsMcpGetRequest` both independently gate on method *and* path, so their relative check order cannot change the outcome for any other request. Traced all four cases by hand:
  - non-`/mcp` (any method): `IsMcpPostRequest` false → `IsMcpGetRequest` false → `!false` → fallthrough to `_next`. Unchanged.
  - GET `/mcp`: `IsMcpPostRequest` false (method mismatch) → `IsMcpGetRequest` true → proceeds into existing inline GET logic. Unchanged.
  - POST `/mcp`: `IsMcpPostRequest` true → `HandlePostAsync`, return. New behavior, as required.
  - Other method (e.g. PUT) on `/mcp`: `IsMcpPostRequest` false → `IsMcpGetRequest` false → fallthrough to `_next`. Same as before the change (previously all non-GET went straight to `_next`).
  This satisfies the task's stated correctness constraint ("POST /mcp goes into the new branch; every other request/method behaves exactly as before").

**Test assertion substring deviation** — The developer changed the illustrative snippet's `"SidPresent=False"` to `"McpSessionIdPresent=False"`. Confirmed against `LogBadMcpRequest`'s actual format string (`McpSessionIdPresent={McpSessionIdPresent}`), which was established by the earlier `extract-log-helper-widen-fields` task in this same pipeline. The illustrative snippet's substring would never have matched the real field name, so this correction was necessary for the test to be meaningful (not just to pass) and preserves the stated intent (assert session id logged as absent).

**Test correctness** — Read the committed test (`PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent`): it builds a POST `/mcp` context with User-Agent/Accept but no `Mcp-Session-Id`, a `next` delegate that sets 400, invokes the middleware, and verifies exactly one `Warning`-level log with `EventId(5932, "McpBadRequest")` whose formatted message contains `HTTPMethod`, `POST`, and `McpSessionIdPresent=False`, plus asserts the response status is still 400 (untouched). This genuinely exercises the new branch and the "never rewrite" constraint — it is not a tautological or vacuous test.

**Interaction with pre-existing tests** — There is a pre-existing test `InvokeAsync_PostMcpPath_PassesThrough` (POST `/mcp`, default response status 200) asserting `nextCalled` true and no log call. Traced this against the new code: `HandlePostAsync` calls `_next` (setting `nextCalled`), status stays 200, so `LogBadMcpRequest` is not invoked — this pre-existing test still passes unmodified, and the diff does not touch it.

**Completeness** — Confirmed via `git log` that both the middleware change and the test were committed together in a single commit `6d51fc0c` (`feat(mcp): observe POST /mcp 400 responses in bad-request middleware`), matching the task's required commit step. `git status --short` on the worktree shows only `artifacts/feat-4042/state.json` as modified (pipeline bookkeeping, correctly excluded from the commit per the task's explicit file list) — no other uncommitted/missing changes.

**Architecture adherence** — `HandlePostAsync` mirrors the shape of the existing GET path's post-`_next` block (stopwatch start → await `_next` → status check → shared log helper), reusing `LogBadMcpRequest` and the `McpTelemetryHelpers`/`EventId` conventions established by prior tasks in this same feature pipeline. No new patterns introduced.

No documentation updates are needed for this task (it is a small internal middleware extension building on already-documented telemetry helpers); no "Docs to Update" section is included.
