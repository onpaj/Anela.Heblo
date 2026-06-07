Self-review against the spec and architecture review:

**Spec coverage:** FR-1 (Task 1.3.1), FR-2 (Task 2.2.1), FR-3 (empty-group test in Task 2.1.2), FR-4 (Task 3.3.1), FR-5 (Tasks 1.2, 2.1, 3.2), FR-6 (Task 4.4 verification). All NFRs anchored — split-logging in Tasks 1.3.1 + 2.2.1, wire contract preserved, no new ErrorCodes. All five architecture-review amendments incorporated (enum vs string, enum-members terminology, FR-4 location pinned to handler, backfill returns non-success envelope, `UnauthorizedAccessException` flagged as load-bearing).

**Placeholder scan:** No TBDs. The only conceptual block (Task 3.3.1 resolver-result variable name) is explicitly flagged as "adapt to the actual surrounding code" because the resolver's return type isn't fixed by either input artifact — the engineer must read the file. All test code is fully written.

**Type consistency:** `ErrorCodes.X` enum syntax used everywhere; handler/service/resolver names consistent; catch order (typed → generic) consistent across Tasks 2 and 3; mapping (`MsalException → ConfigurationError`, `ODataError → ExternalServiceError`, `UnauthorizedAccessException → Forbidden`, `Exception → InternalServerError`) identical in both handlers per architecture review Decision 1.

Plan saved to `artifacts/feat-arch-review-usermanagement-graphservice-/plan.r1.md`.

**Plan summary:**

- **Task 1** — `GraphService.GetGroupMembersAsync`: replace four swallow-and-return-empty catches with log-and-rethrow, preserving Graph/MSAL structured fields. TDD: write four rethrow tests, RED, implement, GREEN.
- **Task 2** — `GetGroupMembersHandler`: four typed catches mapping to `ErrorCodes.ConfigurationError / ExternalServiceError / Forbidden / InternalServerError`. Single log site with request-scoped context. Empty-group test guards FR-3. Delete the loose `Handle_WhenGraphServiceThrowsException_ReturnsFailureResponse` in favor of strict typed tests. Commit 1+2 together.
- **Task 3** — `BackfillArticleRequestedByHandler`: same four-catch shape wrapped around `_userResolver.ResolveByGroupAsync`, returning `new BackfillArticleRequestedByResponse(errorCode)`. `GraphArticleUserResolver` stays a one-line adapter. Commit.
- **Task 4** — full-solution verification against every FR/NFR, including grep guards that no swallow-pattern remains.

Critical constraint flagged for the implementer: Tasks 1–3 ship in **one PR**. Stopping after Task 2 would turn the backfill endpoint into a 500 generator.