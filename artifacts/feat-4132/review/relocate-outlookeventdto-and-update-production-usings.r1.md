# Code Review: relocate-outlookeventdto-and-update-production-usings

## Summary
The implementation matches the task-context exactly for all six specified files — `git mv` preserved history (95% similarity), the namespace line changed and nothing else in the moved file, and every listed consumer got the precise `using` edit the plan specified (add vs. swap, correctly per-file). It also caught and fixed a real gap in the plan's own consumer inventory (`MarketingCalendarSyncService.cs`), verified by an actual failing build before the fix and a clean one after.

## Review Result: PASS

### task: relocate-outlookeventdto-and-update-production-usings
**Status:** PASS

**Verification performed:**
- Diffed every touched file against the task-context's specified before/after text — all six planned files match verbatim (only the intended lines changed).
- `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` → `Build succeeded`, `0 Error(s)`.
- `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj` → `Build succeeded`, `0 Error(s)`.
- `grep -n "Marketing.Services" OutlookInternalDtos.cs` → no matches, confirming FR-6's swap (not add) was applied correctly.
- `dotnet format` was run scoped to all seven touched files; no changes produced (both formatting runs were silent/clean).
- Commit contains exactly the seven touched files (six planned + the one extra fix below); `git show --stat` confirms the rename is tracked as a rename, not a delete+add.

**Deviation from task-context, and why it's correct:**
`MarketingCalendarSyncService.cs` was not in the task-context's file list — the project's own `spec.r1.md` (§ "Full consumer inventory") explicitly states this file "does not reference any of the three type names directly... no change needed there." That statement is factually wrong: the file is in the `Services` namespace itself (the same namespace `OutlookEventDto` used to live in), so it referenced `OutlookEventDto` directly at 8 call sites without ever needing a `using` for it — the move broke exactly that. The developer discovered this via the actual build (`CS0246` errors, 8 occurrences), added the single missing `using` line, and re-verified a clean build. This is not scope creep: it is the minimum fix required to satisfy the task's own step-6 acceptance criterion ("Build succeeded. with 0 Error(s)"), it is a one-line `using` addition with no other change to the file (same pattern as every other consumer in this task), and it is honestly disclosed in both the commit message and the impl artifact rather than silently folded in. Marking this REVISION_NEEDED would only force reverting a correct, necessary, minimally-scoped fix and reintroducing a compile break — there is no alternative implementation that both matches the plan's file list and produces a compiling solution.

**Spec/architecture compliance:**
- FR-1 through FR-6 (per spec.r1.md) all satisfied exactly as specified.
- Architecture guidance ("interface stays in Services/, only its using changes") respected — `IOutlookCalendarSync` was not moved or renamed.
- No `.csproj` changes made, matching FR-1's acceptance criterion and confirmed by a clean SDK-glob build.
- No behavior change anywhere: every edit is either a namespace-declaration line, a `using` line, or (for the move) neither — all method bodies, signatures, and logic are byte-identical to before.

**Scope check:** Task 2 (`update-outlookeventdto-test-usings`) is correctly untouched — no test file was modified in this commit, consistent with this being task 1 of 2.

## Docs to Update
None. This is an internal namespace relocation with no public API, CLI, environment-variable, or operational-behavior change; `docs/architecture/filesystem.md`'s existing description of `Features/{Feature}/Infrastructure/` already covers this placement without needing an update.

## Overall Notes
Recommend a follow-up note (not a blocker for this task, and not this task's job to fix) that `spec.r1.md`'s consumer inventory table should be corrected for anyone reading it later — it currently asserts `MarketingCalendarSyncService.cs` needs no change, which this task's own build run disproved. Task 2's build/test steps will exercise the same file indirectly but the spec document itself is not automatically corrected by that.
