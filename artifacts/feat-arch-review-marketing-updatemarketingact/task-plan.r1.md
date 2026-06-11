Plan saved to `docs/superpowers/plans/2026-06-09-marketing-action-replace-collections.md`.

## Summary

The plan decomposes the refactor into 14 TDD tasks:

- **Tasks 1–5:** Add `ReplaceProductAssociations` to `MarketingAction` (empty/null/dedup/throw/delta + utcNow + Id propagation).
- **Tasks 6–10:** Add `ReplaceFolderLinks` (empty/null/trim/composite-key dedup/distinct-type/throw/delta + utcNow + Id propagation).
- **Tasks 11–12:** Lock the handler's externally-observable contract (clear-on-null, delta composition) with new tests against the *current* implementation before refactoring.
- **Task 13:** Replace the 17-line block in `UpdateMarketingActionHandler.cs` with two delegated calls, plus a grep gate proving no direct mutations remain in the Application layer.
- **Task 14:** Full `dotnet format` + `dotnet build` + full test-suite validation.

Each task is fully self-contained with exact code, exact filter commands, expected outputs, and frequent commits. All FRs and NFRs from spec + arch-review amendments are covered (see the self-review matrix in the plan).

Per the pipeline note, skipping the execution handoff prompt — the plan file is the artifact.