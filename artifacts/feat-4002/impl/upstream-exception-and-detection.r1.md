# Implementation: upstream-exception-and-detection

## What was implemented
Added `PlaudUpstreamUnavailableException` (subclass of `InvalidOperationException`, with `StatusCode`) and `PlaudCliClient.TryParseUpstreamStatus`. `RunCliCoreAsync` now classifies non-zero exits: AUTH_FAILED unchanged, upstream 5xx -> LogWarning + `PlaudUpstreamUnavailableException`, otherwise LogError + plain `InvalidOperationException` (message format unchanged).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudUpstreamUnavailableException.cs` - new exception
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` - regex, `TryParseUpstreamStatus`, restructured non-zero-exit branch
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs` - TryParseUpstreamStatus theories
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs` - 500 / AUTH_FAILED / 404 shim tests, no refresh on 5xx

## Tests
`dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests`: 46 passed, 0 failed. `dotnet format whitespace --verify-no-changes` clean.

## How to verify
Run the test command above.

## Notes
Implemented directly by the orchestrator session (no subagent Task tool available in this run). A 5xx is not caught by `RunCliAsync`'s `PlaudAuthExpiredException` handler, so no token refresh occurs.

## PR Summary
Plaud CLI upstream 5xx failures are now thrown as a dedicated `PlaudUpstreamUnavailableException` (logged as a warning) instead of a generic `InvalidOperationException`, so callers can treat them as transient.

### Changes
- `PlaudUpstreamUnavailableException.cs` - new exception carrying the HTTP status
- `PlaudCliClient.cs` - 5xx detection and classification
- Plaud adapter tests covering detection and classification

## Status
DONE
