# Code Review: add-desired-state-name-option

## Summary
The implementation adds the `DesiredStateName` string property to `PrintPickingListOptions` (default `"Balí se"`, positioned immediately after `DesiredStateId`) and mirrors it in `appsettings.json`'s `ExpeditionList` section. Both edits match the exact diffs specified in the task context, the build succeeds with 0 errors, and the commit was made with only the two intended files changed.

## Review Result: PASS

### task: add-desired-state-name-option
**Status:** PASS

## Docs to Update
(None)

## Overall Notes
- Verified `PrintPickingListOptions.cs` directly: `public string DesiredStateName { get; set; } = "Balí se";` sits exactly between `DesiredStateId` and `NoteStateId`, matching the spec's target diff character-for-character.
- Verified `appsettings.json` directly: `git show` on commit `bf951e4` shows a single added line, `"DesiredStateName": "Balí se",`, inserted right after `"DesiredStateId": 26, // Bali se` — exactly as specified. The `Balí se` value round-trips correctly (UTF-8 diacritics preserved).
- The file's pre-existing `//` trailing comments (e.g. `// Vyrizuje se`) make it non-strict-JSON (JSONC), but this is a pre-existing characteristic of the file untouched by this task — not something introduced by this change, and not in scope to fix here.
- Ran `dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` independently: `Build succeeded`, 0 errors, 139 warnings — all pre-existing and unrelated to `ExpeditionList`/`PrintPickingListOptions`, matching the implementation report's claim exactly.
- Confirmed via `git show bf951e4 --stat` that only the two specified files were touched (1 insertion each, 2 total). `PrintExpeditionOrderHandler.cs` was not modified, correctly respecting the out-of-scope boundary for the follow-up task `wire-handler-desired-state-name`.
- Commit exists on the branch (`bf951e4`, message `feat(expedition-list): add DesiredStateName option paired with DesiredStateId`), matching the spec's Step 4 instruction.
- No functional requirement gaps, no logic errors, no scope violations found.
