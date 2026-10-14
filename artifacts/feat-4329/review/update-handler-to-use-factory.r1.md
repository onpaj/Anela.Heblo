# Code Review: update-handler-to-use-factory

## Summary
The handler's invalid-date branch now returns `GetExpeditionListsByDateResponse.InvalidDate()` instead of constructing the response inline, exactly as specified in `spec.r1.md` FR-2 and `task-plan.r1.md`. The substitution is byte-for-byte the code block the task context specifies; nothing else in the handler changed. Automated build/test verification is blocked by a pre-existing, unrelated compile error already on `main` (`RecurringJobSeeder.cs:51`), independently confirmed in this review rather than just re-asserted from the implementation summary.

## Review Result: PASS

### task: update-handler-to-use-factory
**Status:** PASS

## Docs to Update
(none — internal C# refactor, no public API, CLI, or operational behavior change)

## Overall Notes
- Diff matches task-context Step 2 and task-plan Step 2 verbatim: the `if (!DateOnly.TryParseExact(...))` block now contains only `return GetExpeditionListsByDateResponse.InvalidDate();`; the blob-listing/filtering/success-path code is untouched.
- FR-2's acceptance criteria are met: the handler no longer references `ErrorCodes.InvalidFormat` or constructs a `Params` dictionary directly, and delegates entirely to the factory. The `_blobStoreMock.Verify(..., Times.Never)` assertion in the existing `Handle_ReturnsFailure_WhenDateIsInvalid` test still covers "blob store not called on invalid input" — the early `return` before the `await _blobStore.ListBlobsAsync(...)` line is unchanged, so this still holds.
- `using Anela.Heblo.Application.Shared;` was correctly left in place: the task-context only required removing it if the compiler/analyzer actually flagged it unused, and the developer's build attempt reported no such diagnostic for this file. Leaving it is the compliant choice, not a shortcut — the task-context is explicit that removing it without a warning would be speculative.
- Test coverage: no new test was required (task-context is explicit on this), and the change is fully covered in combination by the existing `Handle_ReturnsFailure_WhenDateIsInvalid` (asserts the handler's observable behavior) and the prior task's `InvalidDate_ReturnsExpectedFailureShape` (asserts the factory's shape independently) — since the handler now returns exactly what the factory returns, these two together specify the same contract the removed inline code did.
- **Verified independently (not just trusting the impl summary):** re-read `RecurringJobSeeder.cs` and confirmed line 51 passes `existing` (a `List<RecurringJobConfiguration>`) to `HasSeededFieldsChanged(RecurringJobConfiguration, RecurringJobConfiguration)` where `existingConfig` was clearly intended; confirmed via `git diff origin/main HEAD -- .../RecurringJobSeeder.cs` (empty) and `git merge-base --is-ancestor 882659fe origin/main` (true) that this bug predates this branch and is already on `main`, unrelated to this task's diff. This is a repo-wide build blocker (breaks `Anela.Heblo.Application` and, transitively, the whole test project) and deserves its own fix/issue, but correctly was not touched here — consistent with the "surgical changes" project rule and with the precedent already established in `review/add-invalid-date-factory.r1.md` for the same issue.
- Given the change is a pure, verified-safe mechanical substitution (both sides of the substitution are independently unit-tested to have identical shape) and the only thing blocking a literal green `dotnet test` run is a pre-existing, unrelated repo-wide compile error, this does not meet any of the review's REVISION_NEEDED criteria (no functional requirement unmet, no architecture violation, no missing required test, no correctness bug in the diff itself).
