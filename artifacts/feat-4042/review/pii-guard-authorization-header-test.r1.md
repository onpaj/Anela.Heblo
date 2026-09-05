# Code Review: pii-guard-authorization-header-test

## Summary
The implementation adds the exact `PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer`
fact test specified in the task context, verbatim, matching the existing file's conventions. I
independently confirmed by inspection that `McpBadRequestMiddleware.LogBadMcpRequest` only reads
the allow-listed headers (`UserAgent`, `Origin`, `Accept`, `ContentType`, `Mcp-Session-Id`) and
never `Authorization`, `Cookie`, or `X-Api-Key`, and confirmed the test and the full
`McpBadRequestMiddlewareTests` class (32/32) pass.

## Review Result: PASS

### task: pii-guard-authorization-header-test
**Status:** PASS

## Docs to Update
(none — this is test-only coverage of existing middleware behavior, no public behavior or docs changed)

## Overall Notes
Test correctly exercises NFR-2 (arch-review amendment 4): sensitive request headers
(`Authorization`, `Cookie`, `X-Api-Key`) must never appear in the bad-request log output. The
`loggerMock.Verify(..., Times.Never)` assertion checks the formatted log state string for each
secret substring, which is the right level of rigor for a "never leak this value" guard. No
functional code was touched, and none was needed — the middleware already satisfies the guard by
construction.
