Plan saved to `docs/superpowers/plans/2026-06-04-fix-orgchart-error-contract.md`.

**Summary of the plan:**

6 tasks, all TDD-ordered with bite-sized steps and frequent commits:

1. **Replace handler failure-path test (red)** — Rewrites `Handle_PropagatesException_WhenServiceThrows` to assert the new typed-envelope behavior + `LogError` invocation; adds an `OperationCanceledException` re-throw test.
2. **Implement handler failure conversion (green)** — Adds try/catch in handler, returns `new OrgChartResponse(ErrorCodes.InternalServerError)`, re-throws cancellation.
3. **Refactor controller onto `BaseApiController`** — Drops `ILogger`, deletes try/catch, switches to `HandleResponse`, types the 500 `[ProducesResponseType]`.
4. **Full suite + format validation gate** — Greps for stale references to the old shape, runs all tests, runs `dotnet format`.
5. **Integration test for FR-2 leakage assertion** — Uses `HebloWebApplicationFactory` + a sentinel URL exception to prove the 500 body never contains the data-source URL or wrapped-error prefix.
6. **Final build + TS client regeneration check** — Runs `dotnet build`, `dotnet format --verify-no-changes`, full `dotnet test`, and inspects the regenerated TypeScript client diff.

The plan covers all FR/NFR requirements in the spec, both arch-review amendments (test replacement and `_logger` removal from controller), and applies the dominant project pattern (option A) per the architectural decision in the review.