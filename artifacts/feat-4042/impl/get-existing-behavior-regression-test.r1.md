# Implementation: get-existing-behavior-regression-test

## What was implemented
No new test was added. Investigation (per task Step 1) found that
`McpBadRequestMiddlewareTests.InvokeAsync_GetMcpWithoutAcceptHeader_Returns404WithoutCallingNext`
already asserts exactly the FR-3 regression guard this task calls for: a GET
`/mcp` request with no `Accept` header (a headerless probe) short-circuits to
404 and never calls `next` (`nextCalled.Should().BeFalse()` +
`context.Response.StatusCode.Should().Be(404)`). This is the same assertion
pair the task's reinforcing example (`GetHeaderlessProbe_StillReturns404_NotFallthrough`)
would have added, so adding a second, duplicate test would not strengthen
coverage — it would just restate the existing one under a different name.

## Files created/modified
- None. `backend/test/Anela.Heblo.Tests/MCP/McpBadRequestMiddlewareTests.cs` was
  read but not changed.

## Tests
- `McpBadRequestMiddlewareTests.InvokeAsync_GetMcpWithoutAcceptHeader_Returns404WithoutCallingNext`
  already covers: GET `/mcp` headerless probe → 404, `next` not invoked.

## How to verify
```
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"
```
Result: Passed! - Failed: 0, Passed: 32, Skipped: 0, Total: 32.

## Notes
Per the task's own Step 1 instruction ("If one already exists and passes, skip
to Step 3"), this task resolves to a verification-only pass. No regression
risk was found — the GET path's existing behavior is already locked in by test.

## PR Summary
No code or test changes were needed for this task: the existing
`McpBadRequestMiddlewareTests` suite already contains a test that locks in the
FR-3 requirement (GET `/mcp` headerless probe still short-circuits to 404
without falling through to the MCP SDK). Verified the full test class stays
green (32/32 passing) and left the file untouched to avoid adding a
duplicate, functionally-identical test.

### Changes
- (none)

## Status
DONE
