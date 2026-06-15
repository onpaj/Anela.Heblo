Plan saved to `docs/superpowers/plans/2026-06-15-atomic-gridlayout-upsert.md`. It covers the spec end-to-end:

- **Tasks 1–2:** documentation pointer to the raw-SQL/EF drift gotcha, then scaffold the Postgres integration test class against `PostgresSharedContainerFixture` (the same pattern `BankStatementImportRepositoryIntegrationTests` uses, including the manual schema bootstrap that avoids the `vector` extension).
- **Tasks 3–6:** TDD-style test additions for insert/update paths (with `FakeTimeProvider`), the concurrent-insert race (the bug repro), concurrent updates, single-statement assertion (via `LogTo` on `RelationalEventId.CommandExecuted`), cancellation, and a non-race Postgres failure (`GridKey` > 100 chars).
- **Task 7:** replace `UpsertAsync` body with `ExecuteSqlInterpolatedAsync("INSERT … ON CONFLICT … DO UPDATE")`, omitting `Id` (per the arch review — `int IdentityByDefaultColumn`), and add the one-line `DeleteAsync` rationale comment.
- **Task 8:** drop the three obsolete `UpsertAsync_*` translation unit tests (their `SaveChangesAsync` seam is bypassed by raw SQL); keep the `DeleteAsync_*` one and the `ThrowingApplicationDbContext` helper.
- **Task 9:** full `dotnet build` / `dotnet format` / GridLayout-filtered tests / full suite verification, plus a final grep to confirm the unique-index columns match the `ON CONFLICT` clause.

Per the pipeline note, no execution-handoff prompt is presented — the plan file content is the artifact.