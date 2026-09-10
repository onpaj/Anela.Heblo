### task: full-validation-gate

**Files:**
- None modified — this task only runs the project's standard validation commands per `CLAUDE.md`.

- [ ] **Step 1: Backend build**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Backend format check**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: No formatting violations. If violations are reported, run `dotnet format Anela.Heblo.sln` to fix them, then re-stage and amend the relevant commit from the task that introduced the violation.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test Anela.Heblo.sln`
Expected: All tests pass, including the full `Anela.Heblo.Tests` project (not just the `DataQuality` filter used in earlier tasks) — this catches any unexpected interaction with other DQT-adjacent tests (e.g. `GetDqtRunDetailHandlerTests`, which also uses `ErrorCodes.DqtUnsupportedTestType`).

- [ ] **Step 4: Confirm no frontend/API contract drift**

This change does not alter `RunDqtRequest`/`RunDqtResponse` shapes (same fields, only a previously-unused `ErrorCode` value now reachable). No OpenAPI client regeneration or frontend changes are required. Run `git status` to confirm no `frontend/src/api-client` files were touched by `dotnet build`'s auto-generation step; if any were touched unexpectedly, investigate before proceeding — that would indicate an unintended contract change.
