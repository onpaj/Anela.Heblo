# Implementation: update-handler-to-use-factory

## What was implemented
`GetExpeditionListsByDateHandler.Handle` now delegates its "invalid date format" failure response to the `GetExpeditionListsByDateResponse.InvalidDate()` factory (added in the previous task, `add-invalid-date-factory`) instead of constructing the response inline. The replacement is a byte-for-byte match of the task-context's Step 2 code block: the `if (!DateOnly.TryParseExact(...))` block now contains only `return GetExpeditionListsByDateResponse.InvalidDate();`. No other line in the handler changed — the blob-listing, PDF filtering, and success-path construction (`return new GetExpeditionListsByDateResponse { Items = items };`) are untouched.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — replaced the inline `GetExpeditionListsByDateResponse` construction (Success/ErrorCode/Params block) in the invalid-date branch with a call to `GetExpeditionListsByDateResponse.InvalidDate()`. No test files changed (per task-context, the existing `Handle_ReturnsFailure_WhenDateIsInvalid` test already specifies the required post-refactor contract and needs no changes).

## Tests
No new tests were added (task-context explicitly says none are needed — behavior is unchanged). The tests that cover this change:
- `Handle_ReturnsFailure_WhenDateIsInvalid` (existing, 5 theory cases) — asserts `Success`, `ErrorCode`, `Params["Field"]`, `Params["ExpectedFormat"]`, `Items` empty, and that `ListBlobsAsync` is never called for an invalid date.
- `InvalidDate_ReturnsExpectedFailureShape` (added in the prior task) — asserts the exact same shape directly on `GetExpeditionListsByDateResponse.InvalidDate()`.

Because the handler's invalid-date branch now returns exactly what `InvalidDate()` returns, and `InvalidDate()`'s shape is already independently unit-tested and matches the spec's required shape field-for-field (`Success=false`, `ErrorCode=ErrorCodes.InvalidFormat`, `Params={"Field":"Date","ExpectedFormat":"yyyy-MM-dd"}`), the two tests together fully cover the change by construction/inspection.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests"
```
Expect all cases in the file to pass once the pre-existing blocker below is resolved (see Notes).

## Notes

**Step 3 (unused `using` check):** Left `using Anela.Heblo.Application.Shared;` in place. The task-context says to remove it only if the compiler/analyzer actually emits an unused-using warning; a full solution build attempt (see below) reached and compiled this file with zero diagnostics reported against it (only unrelated warnings from other files), so no such warning was observed. Per the task-context's explicit instruction ("If no such warning appears, leave the `using` directives untouched — do not remove anything speculatively"), it was left untouched.

**Steps 4/5/6 (test run, full suite run, format+build) — BLOCKED by a pre-existing, unrelated compile error, same one already flagged in `impl/add-invalid-date-factory.r1.md` and `review/add-invalid-date-factory.r1.md`:**

`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51` calls `HasSeededFieldsChanged(existing, config)` where `existing` is a `List<RecurringJobConfiguration>` (from `_repository.GetAllAsync(...)`, declared a few lines above) but the method requires a single `RecurringJobConfiguration` — the call should use the already-declared `existingConfig` instead. This is `CS1503`, a hard compile error, not a warning.

Verified independently in this session (not just re-asserted from the prior task's note):
- `git diff origin/main HEAD -- backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` → empty. The file is byte-identical between this branch's HEAD (before this task's edit) and `origin/main`. This bug is **not** introduced by feat-4329 or this task; it is already on `main` (`git merge-base --is-ancestor` confirms commit `882659fe` — "#4318 ... (#4324)" — is an ancestor of `origin/main`).
- `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` → 1 error (exactly this `CS1503`), 122 warnings, none of the warnings or the error reference `GetExpeditionListsByDateHandler.cs` or `GetExpeditionListsByDateResponse.cs`.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests"` → also fails at the build step with the same `CS1503` (the test project references the Application project, so it inherits the same blocker) — the filtered tests never actually execute. Confirmed by direct observation of the build output.
- `dotnet format Anela.Heblo.sln ...` was attempted but the solution-wide MSBuild restore/build (31 projects) did not complete within a reasonable time in this session and was stopped; given the same Application project is in its dependency graph, it would hit the identical blocker.

**Confidence in correctness despite the blocked verification:** The change is a pure, mechanical substitution specified verbatim by the task-plan and spec (`spec.r1.md` FR-2). `GetExpeditionListsByDateResponse.InvalidDate()` (added and unit-tested in the prior task) already asserts the exact same `Success`/`ErrorCode`/`Params`/`Items` shape that the handler's replaced inline block produced, so the substitution cannot change observable behavior. No other line of the handler was touched.

**Recommendation:** `RecurringJobSeeder.cs:51` blocks `dotnet build`/`dotnet test` for the entire `Anela.Heblo.Application` project (and therefore the whole backend test suite) on `main` right now, for every branch, not just this one. This should be fixed via its own dedicated issue/PR — out of scope for #4329 and not touched here, consistent with the "surgical changes" project rule and with the precedent already set in the prior task of this same feature.

## PR Summary
Completes issue #4329 (task 2 of 2): `GetExpeditionListsByDateHandler` no longer constructs its "invalid date format" failure response inline — it now delegates to `GetExpeditionListsByDateResponse.InvalidDate()`, the static factory added in the previous task. This brings `GetExpeditionListsByDateResponse` in line with the pattern already used by its sibling response types (`DownloadExpeditionListResponse.Fail()`, `ReprintExpeditionListResponse.Fail()`) in the same module. Pure refactor: no behavior, API contract, or response payload change — the handler's invalid-date branch now returns the same `Success`/`ErrorCode`/`Params`/`Items` values it always did, just constructed via the factory instead of inline.

Automated build/test verification for this change is currently blocked repo-wide by a pre-existing, unrelated compile error in `RecurringJobSeeder.cs` (already on `main`, introduced by PR #4324/#4318 — see Notes above for verification details and a recommendation to fix it separately).

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — invalid-date branch now returns `GetExpeditionListsByDateResponse.InvalidDate()` instead of constructing the response inline

## Status
DONE_WITH_CONCERNS
