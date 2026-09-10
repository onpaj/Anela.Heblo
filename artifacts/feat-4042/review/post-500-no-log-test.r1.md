# Code Review: post-500-no-log-test

## Summary
The test `PostServerError500_EmitsNoLog` has been correctly added to `McpBadRequestMiddlewareTests`, verifying that a POST `/mcp` request whose downstream handler sets a 500 status produces no logger invocation. This confirms the FR-4 requirement that bad-request diagnostics logging is scoped strictly to 400 responses. The test body matches the task context's specification exactly, and the full test file (23 tests) passes.

## Review Result: PASS

### task: post-500-no-log-test
**Status:** PASS

## Docs to Update
No documentation updates needed. This is a test addition with no changes to public behavior or system operation.

## Overall Notes
- **Correctness**: The test constructs its own local `loggerMock`/`middleware` (matching the established pattern for local-mock POST tests in this file, e.g. `PostSuccess200_EmitsNoLog`, `PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent`), sets the downstream response to 500, and asserts `Log` is never called at any level. This correctly exercises the `if (context.Response.StatusCode == StatusCodes.Status400BadRequest)` guard in `McpBadRequestMiddleware.HandlePostAsync`, confirming 500 falls outside the logging scope.
- **Placement**: Added directly after `PostSuccess200_EmitsNoLog` and before the `HasValidMcpAcceptHeader` static-helper tests, consistent with the file's existing section grouping.
- **Test verification**: Filtered run confirms `Passed: 1, Failed: 0`; full-file run confirms all 23 tests in `McpBadRequestMiddlewareTests` pass together, so the new test does not conflict with or duplicate existing coverage.
- **Compatibility**: No existing code was modified.
