# Code Review: Plaud upstream 5xx classification

## Summary
The implementation matches the task context: the new exception type, the static `TryParseUpstreamStatus` helper, and the restructured non-zero-exit branch in `RunCliCoreAsync`. Tests cover 500/502/503/504 parsing, negatives (404, AUTH_FAILED, empty, null), 5xx throwing the new type with no token refresh, AUTH_FAILED still mapping to `PlaudAuthExpiredException`, and 404 staying a plain `InvalidOperationException`. All 46 adapter tests pass.

## Review Result: PASS

### task: upstream-exception-and-detection
**Status:** PASS

## Overall Notes
Review performed by the orchestrator session itself (no separate reviewer Task tool available). Existing message format preserved for unchanged branches.
**Status:** PASS
