Plan saved to `docs/superpowers/plans/2026-06-02-marketing-update-delete-db-error-handling.md`.

The plan covers all spec requirements (FR-1 through FR-4, NFR-1 through NFR-4) in 6 bite-sized tasks, follows strict TDD (failing test → impl → verify per change), incorporates the arch-review's specification amendments (log-assertion mechanism, integration-test scoping, disambiguated Delete-handler phrase), and matches the Create handler reference shape exactly.

**Summary of the plan:**
- **Task 1–2**: TDD pair for `UpdateMarketingActionHandler` — failing test + guarded `UpdateAsync`/`SaveChangesAsync` returning `ErrorCodes.DatabaseError`
- **Task 3–4**: TDD pair for `DeleteMarketingActionHandler` — failing test + guarded `DeleteSoftAsync`
- **Task 5**: Grep verification of FR-3 greppable phrases and no-interpolation rule across all three handlers
- **Task 6**: Final Marketing suite + full backend build/format/test sweep

Three production files modified (Update + Delete handlers, Create untouched), two test files modified (one new `[Fact]` each), no new files, no schema/DI/package changes. Spec coverage, placeholder, and type-consistency self-checks completed inline.