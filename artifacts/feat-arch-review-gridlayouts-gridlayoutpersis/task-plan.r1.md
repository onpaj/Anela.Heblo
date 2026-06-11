Plan saved to `docs/superpowers/plans/2026-06-10-remove-sqlstate-from-domain-exception.md`.

**Summary of the plan:**

- **Task 1** — Convert `PostgresExceptionTranslator` from `static` to an instance class with `ILogger<PostgresExceptionTranslator>` injection. Emit a structured `LogWarning(SqlState, Operation, Message)` before returning. Register as `Scoped` in `PersistenceModule`. Update `GridLayoutRepository` ctor + the two persistence-layer test files. At end of task the build is green and the Domain exception still has its 3-arg ctor (deferred to Task 2).
- **Task 2** — Drop `SqlState` from the Domain exception (collapse to `(string, Exception)`). Update the translator's ctor call, three handler `LogError` templates, and four handler test fixtures. At end of task NFR-1 (no Npgsql in Domain) is satisfied.
- **Task 3** — Mechanical verification: greps to confirm no `SqlState` leaks in Domain/handlers, `dotnet list package` confirms no Npgsql dep in Domain, full solution build + tests + `dotnet format --verify-no-changes`.

Two clean commits, ~12 production-code edits + ~6 test edits, no new files, no NuGet changes.