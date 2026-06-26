Plan saved to `docs/superpowers/plans/2026-06-11-authorization-effective-permissions-handler-tests.md`.

**Summary**

Five-task plan covering the surgical addition of one new test file at `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`:

- **Task 1:** Scaffold the file + first `[Fact]` for user-not-found (FR-2).
- **Task 2:** Inactive-user fact (FR-3) — the security-critical guard, with all three required assertions (empty permissions, no `AccessRoles.Base`, `GetGroupGraphAsync` never called).
- **Task 3:** Active-user merge/dedup/sort fact (FR-4), using a parent-edge graph to prove closure resolution.
- **Task 4:** Dedicated dedup-of-`AccessRoles.Base` fact (arch-review amendment 2, promoted from optional).
- **Task 5:** Full validation — run all four facts, `dotnet format`, confirm no `backend/src/` changes, regression-check sibling Authorization tests.

Each task contains complete code, exact `dotnet` commands, expected output, and a commit. Tests are characterization tests on existing handler behavior, so they should pass green on first run (any failure indicates an unintended production regression or a typo in the assertions). Verified all referenced types, properties, and constants against the actual source files in the worktree before writing the plan.