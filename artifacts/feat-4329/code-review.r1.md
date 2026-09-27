# Code Review: GetExpeditionListsByDateResponse.Fail() factory method

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature diff against `main` (merge-base `c2cbcc148ecaae937faf1e23a2016d6b0279cf7b`). The only real source changes are the three files described in `spec.r1.md`:

- `GetExpeditionListsByDateResponse.cs` — adds `public static GetExpeditionListsByDateResponse InvalidDate()`, matching FR-1's required shape exactly (`Success=false`, `ErrorCode=ErrorCodes.InvalidFormat`, `Params={"Field":"Date","ExpectedFormat":"yyyy-MM-dd"}`).
- `GetExpeditionListsByDateHandler.cs` — the invalid-date branch now returns `GetExpeditionListsByDateResponse.InvalidDate()` instead of constructing the response inline, matching FR-2 verbatim. Blob store is still only called on the valid-date path (unchanged control flow).
- `GetExpeditionListsByDateHandlerTests.cs` — adds `InvalidDate_ReturnsExpectedFailureShape` asserting the factory's exact shape; the existing `Handle_ReturnsFailure_WhenDateIsInvalid` (5 theory cases) already covers the handler's observable behavior post-refactor and needed no changes.

This is a pure, mechanical refactor with no behavior, API contract, or serialized-payload change — confirmed by reading both response-construction call sites (invalid-date and success paths) and the untouched blob-listing/filtering logic in between.

Independently re-verified the two developer/reviewer notes rather than trusting them as given:
- `using Anela.Heblo.Application.Shared;` left in the handler: ran `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` myself; no unused-using diagnostic is emitted for either `GetExpeditionListsByDateHandler.cs` or `GetExpeditionListsByDateResponse.cs` among the build's 122 warnings, so leaving it in place is correct per the task-context's own rule (remove only if flagged).
- Pre-existing, unrelated build blocker: confirmed `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51` still has the same `CS1503` (`HasSeededFieldsChanged(existing, config)` should use `existingConfig`) that breaks `dotnet build`/`dotnet test` for the whole `Anela.Heblo.Application` project. `git diff origin/main HEAD -- .../RecurringJobSeeder.cs` is empty, confirming this predates and is untouched by this branch. This is out of scope for issue #4329 and not part of this diff, so it is not reported as a finding here (see prior task reviews for the recommendation to fix it separately).

No correctness bugs found in the diff. No cleanup suggestions — the change is already minimal and consistent with the sibling `Fail()`-style factories in the same module.
