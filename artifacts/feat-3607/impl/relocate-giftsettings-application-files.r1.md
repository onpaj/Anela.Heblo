# Implementation: relocate-giftsettings-application-files

## What was implemented
Moved the 8 Application-layer GiftSettings files from
`backend/src/Anela.Heblo.Application/Features/GiftSettings/` to
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/` using `git mv`
to preserve history, then updated each file's `namespace` declaration (and the one internal
`using` reference in `GiftSettingsModule.cs`) to match the new location. No logic, method
bodies, or public API surfaces were changed. The old `Features/GiftSettings/` directory tree
no longer exists.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/GiftSettingsModule.cs` — moved; updated `using` for `SetGiftSetting` namespace and module `namespace` declaration
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/Dto/GiftSettingDto.cs` — moved; updated `namespace` declaration
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs` — moved; updated `using` for `Dto` namespace and `namespace` declaration
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs` — moved; updated `using` for `Dto` namespace and `namespace` declaration
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs` — moved; updated `namespace` declaration
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs` — moved; updated `namespace` declaration
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs` — moved; updated `namespace` declaration
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs` — moved; updated `namespace` declaration

## Tests
None expected for this task — pure file move with namespace-only changes, no logic touched.

## How to verify
1. `find backend/src/Anela.Heblo.Application/Features/GiftSettings -type f` — should produce no output (directory no longer exists).
2. `find backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings -type f` — should list the same 8 files at their new paths.
3. `grep -rn "Anela\.Heblo\.Application\.Features\.GiftSettings" backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/` — should produce no output (old namespace string fully removed from the moved files).
4. `git log --follow -- backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs` — should show history preserved through the move (rename detected for most files; see Notes for one exception).
5. `dotnet build` on `backend/` is expected to **fail** at this point (see Notes) — this is expected and will be resolved by the next task in the pipeline.

## Notes
- As explicitly called out in the task context file, **the build is expected to be broken** after this task. Call sites outside the 8 moved files — `ApplicationModule.cs`, the GiftSettings controller, and test files — still reference the old namespace `Anela.Heblo.Application.Features.GiftSettings.*`. These are fixed in the next task in the pipeline; no attempt was made to fix them here, per instructions.
- Git's rename similarity detection recognized 7 of the 8 moved files as renames in `git status`/`git show --stat` output. `GetGiftSettingQuery.cs` (an 8-line file) was recorded as a delete + new file instead of a rename, because the namespace/using edit reduced its content similarity below git's default 50% rename-detection threshold. This is purely a display/heuristic artifact of `git diff`'s similarity index — the file was moved via `git mv` identically to the others, and `git log --follow` still recovers its full history. No functional difference.
- No changes were made to any file outside the 8 listed above. An unrelated pre-existing modification to `artifacts/feat-3607/state.json` was left unstaged/uncommitted, since it is not part of this task's scope.

## Status
DONE
