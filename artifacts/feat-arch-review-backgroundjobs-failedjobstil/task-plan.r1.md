Plan saved to `docs/superpowers/plans/2026-06-01-decouple-failedjobstile-from-hangfire.md`.

**Summary of the implementation plan:**

Seven tasks, organized as RED/GREEN TDD pairs and committed per logical pair:

1. **Add `IFailedJobCounter`** in `Application/Features/BackgroundJobs/Services/` (interface only) → commit.
2. **Rewrite `FailedJobsTileTests`** to mock the new abstraction (RED — won't compile until Task 3).
3. **Refactor `FailedJobsTile`** to depend on `IFailedJobCounter`, convert `LoadDataAsync` to `async` → GREEN, single commit covering Tasks 2+3.
4. **Write `HangfireFailedJobCounterTests`** (RED — adapter doesn't exist) at `test/Features/BackgroundJobs/` per arch-review Amendment #2.
5. **Implement `HangfireFailedJobCounter`** sealed adapter in `API/Infrastructure/Hangfire/` → GREEN, single commit covering Tasks 4+5.
6. **Add DI line** `services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>()` in `AddHangfireServices` (~line 346, in the existing adapter-registration block) → commit.
7. **Whole-solution gate** — build, full test suite, csproj diff check, Hangfire-free folder grep, JSON-envelope byte-identical check.

The plan grounds every step in exact file paths and line numbers, embeds the complete code for both the contract and the adapter, names every literal string that must stay byte-identical (`"Hangfire"`, `"/hangfire/jobs/failed"`, `"Failed to load Hangfire failed job count"`, `"Failed to retrieve job count. See server logs."`), and folds in all four arch-review amendments. Spec coverage matrix at the end maps every FR/NFR/amendment to its task.