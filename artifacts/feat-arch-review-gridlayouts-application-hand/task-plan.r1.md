Plan saved to `docs/superpowers/plans/2026-06-02-decouple-gridlayouts-app-handlers-from-npgsql.md`.

**Summary of the plan (13 tasks):**

1. **Task 1** — Create `GridLayoutPersistenceException` (Domain) with `SqlState` property per arch-review Spec Amendment 1.
2. **Task 2** — Create `PostgresExceptionTranslator` in `Persistence/Infrastructure/`, mirroring the unwrap recursion from `PostgresExceptionLoggingInterceptor`.
3. **Task 3** — Add 5 translator unit tests (direct Npgsql, `DbUpdateException` wrapping, non-Pg passthrough cases).
4. **Task 4** — Wrap `GridLayoutRepository.GetAsync/UpsertAsync/DeleteAsync` with the translator.
5. **Task 5** — Add 4 repository-level translation tests using `ThrowingApplicationDbContext` (satisfies FR-8 with both `NpgsqlException` and `DbUpdateException` wrapping, plus passthrough verification).
6. **Tasks 6–8** — Refactor the three handlers to catch `GridLayoutPersistenceException` and drop `using Npgsql;` (log templates byte-for-byte preserved, SqlState read from domain exception).
7. **Tasks 9–11** — Update the three handler test files to throw/verify `GridLayoutPersistenceException` instead of `NpgsqlException` (per Spec Amendment 4).
8. **Task 12** — Optional: add `<exception>` XML docs to `IGridLayoutRepository`.
9. **Task 13** — Final verification: grep checks (FR-6 reinterpretation), full solution build, `dotnet format`, full test run (expect 16 passing GridLayouts tests).

Key spec amendments documented inline: `SqlState` property on the exception (FR-1), `DbUpdateException` wrapping coverage (FR-2 — flagged as deliberate latent-bug fix), FR-6 reinterpreted as grep verification only (no csproj change). The plan also includes PR-description notes for the engineer to surface the latent-bug fix and out-of-scope follow-ups.