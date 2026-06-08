Plan complete. Summary:

**Saved to:** `docs/superpowers/plans/2026-06-08-gridlayout-upsertasync-atomic.md`

**Shape:** 8 tasks (Pre-flight + 7 working tasks + final validation). Task 1 is the core red→green cycle: a 20-way concurrent integration test fails against the current select-then-insert code, then the rewritten `UpsertAsync` (single `INSERT ... ON CONFLICT` via `ExecuteSqlInterpolatedAsync`) makes it pass. Tasks 2–6 add focused coverage for insert, update-in-place, `TimeProvider`-driven `LastModified`, real-Npgsql translation, and cancellation propagation. Task 7 deletes the stale in-memory `UpsertAsync_*` cases (the in-memory provider can't run `ExecuteSqlInterpolatedAsync`); Task 8 is build + format + full-suite gate.

**Key arch-review amendments baked in:** `DateTime.SpecifyKind(..., Unspecified)` on `LastModified` (the column is `timestamp without time zone`, would otherwise throw `InvalidCastException`); `ON CONFLICT ("UserId","GridKey")` via column inference, not constraint name; in-memory translation tests deleted rather than ported.

**One package add:** `Microsoft.Extensions.TimeProvider.Testing` (for `FakeTimeProvider` in Task 4) — confirmed not currently in `Anela.Heblo.Tests.csproj`.