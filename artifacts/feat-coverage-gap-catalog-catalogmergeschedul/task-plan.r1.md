Plan saved to `docs/superpowers/plans/2026-06-16-catalog-merge-scheduler-test-coverage.md`.

**Summary:** 14 bite-sized tasks (one helper-skeleton task + one task per acceptance criterion in FR-2…FR-9, plus a final stability/coverage verification task). Each task creates or modifies the single new file `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs`, runs the new test with a `--filter` scoped to that method, and commits. No production code is touched.

Key design choices encoded in the plan, all carried over from the architecture review:
- Hand-written `FakeApplicationLifetime` over Moq (cleaner pre-cancelled stopping case for FR-8).
- `TaskCompletionSource`-coordinated positive waits, bounded `Task.Delay` only for negative "did NOT fire" assertions.
- FR-5 forces semaphore contention by setting `MaxMergeInterval = 1 ms` and gating the first callback on a release-TCS.
- Substring `ILogger` verification (resilient to interpolated parameters).
- Per-test scheduler wrapped in `using` to avoid order-dependent flakes.

Final task verifies ≥ 60 % line coverage on `CatalogMergeScheduler.cs` specifically via Cobertura XML, runs the class 10 times for determinism (NFR-2), and runs the full suite for regression safety.