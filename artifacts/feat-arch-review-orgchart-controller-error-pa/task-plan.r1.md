Plan saved to `docs/superpowers/plans/2026-06-04-fix-orgchart-error-contract.md`.

## Summary

The plan decomposes the OrgChart error-contract fix into 6 tasks following TDD:

1. **Task 1 (RED)** — Replace `Handle_PropagatesException_WhenServiceThrows` with three new handler tests (success, typed error envelope, `OperationCanceledException` rethrow) and verify the failure-path test fails against the current handler.
2. **Task 2 (GREEN)** — Update `GetOrganizationStructureHandler` to catch `Exception`, rethrow `OperationCanceledException`, log via `LogError`, and return `new OrgChartResponse(ErrorCodes.InternalServerError)`. Commit.
3. **Task 3** — Convert `OrgChartController` to inherit `BaseApiController`, drop the `ILogger` dependency and `try/catch`, call `HandleResponse(result)`, and declare 500 as `typeof(OrgChartResponse)`. Commit.
4. **Task 4** — Add `OrgChartControllerTests` integration tests using the existing `HebloWebApplicationFactory` with `WithWebHostBuilder` overriding `IOrgChartService`. Verifies wire-level 200 success, typed 500 envelope, and absence of the sentinel data-source URL / wrapped exception message in the response body. Commit.
5. **Task 5** — Regenerate + commit OpenAPI/TS client artifacts; verify backend + frontend build pass.
6. **Task 6** — Final build/test sweep and `git status` audit.

All concrete code, exact file paths/line numbers, expected test outputs, and commit messages are inlined. The self-review table maps every spec FR/NFR + arch-review amendment to a specific task.