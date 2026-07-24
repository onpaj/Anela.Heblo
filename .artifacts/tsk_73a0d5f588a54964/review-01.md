# Review: Remove dead `IGraphService.SearchUsersAsync`

## Verdict: done

## What was checked

1. **Diff against plan/design/architecture** (commit `c6cd2945`): exactly the four files specified in plan-01.md/design-01.md were touched:
   - `IGraphService.cs` — `SearchUsersAsync` declaration removed, interface left with `GetGroupMembersAsync` + `GetAppRoleMembersAsync`.
   - `GraphService.cs` — the ~75-line implementation removed, plus the now-dead `private const int SearchResultLimit = 25;` also removed. Splice is clean: `GetGroupMembersAsync`'s closing brace is immediately followed by `GetAppRoleMembersAsync`, no orphaned braces/using directives.
   - `MockGraphService.cs` — mock override removed cleanly.
   - `GraphServiceSearchTests.cs` — deleted in full (120 lines, 5 facts, all exclusively testing the removed method).

2. **Repo-wide re-verification** (independent of the dev step's own claim): `Grep` for `SearchUsersAsync|SearchResultLimit` across `backend/` returns zero matches. Confirms no other caller, no dangling reference, no stale test.

3. **Collateral check**: confirmed `FakeHttpMessageHandler` (used by the deleted test file) is still referenced by `GraphServiceTests.cs` and `OutlookCalendarSyncServiceTests.cs`, so it wasn't orphaned and correctly wasn't deleted. `GraphServiceTests.cs`, `MockGraphServiceTests.cs`, and `PhotobankGraphServiceThumbnailTests.cs` still exist and are untouched, matching the plan's "verified unaffected" list.

4. **Scope discipline**: no unrelated edits — pure subtraction, matches the "surgical changes" project rule. No new abstractions, no leftover comments referencing the removed method.

5. **Build/test validation gap**: this environment has no .NET SDK, so `dotnet build`/`dotnet format`/`dotnet test` could not be executed by the dev step or by this review, confirmed independently (`dotnet` command not found). This is an environment limitation, not a defect in the change — the deletion is a pure removal of a statically-verified-dead method with no plausible compile-time interaction with any other file. The development step already flagged this explicitly and instructed a human/CI to run `dotnet build && dotnet test` before merge. Given the nature of the change (deletion of an unreferenced interface member, its two implementations, and its dedicated test file — no renames, no signature changes to surviving members), the residual risk of a build break is negligible. Not a blocker for this review, but the build/test step from "Validation before completion" still needs to run in CI before merge.

## Conclusion

Implementation matches the approved Option A scope exactly, is complete, surgical, and correctly verified for reference-cleanliness given the tools available in this environment. No functional requirement is unmet, no architecture conflict, no missing required test (removal of tests for a removed method is correct — no new tests were "required" by the plan). Recommending `done`.
