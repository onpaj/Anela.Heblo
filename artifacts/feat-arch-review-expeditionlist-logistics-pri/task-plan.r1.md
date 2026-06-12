Plan saved to `docs/superpowers/plans/2026-06-11-remove-print-picking-list-default-carriers.md`.

**Summary:** Three bite-sized tasks executed in strict order to avoid a mid-edit build break:

1. **Task 1 — Retarget the integration test first.** Add `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` and change `PickingListIntegrationTests.cs:88` from `PrintPickingListRequest.DefaultCarriers` to `ExpeditionPickingRequest.DefaultCarriers`. Build to confirm both constants still coexist cleanly.
2. **Task 2 — Delete the dead property.** Remove lines 16-22 (the public-setter static `DefaultCarriers`) from `PrintPickingListRequest.cs`. Keep the `using`, both `Default*StateId` consts, and every instance member. Build the full solution to confirm zero remaining consumers.
3. **Task 3 — Verify and commit.** Repo-wide grep for zero remaining `PrintPickingListRequest.DefaultCarriers` references, `dotnet format` the touched files, run `dotnet test --filter "Category!=Integration"`, confirm the canonical `ExpeditionPickingRequest.DefaultCarriers` is untouched, then stage and commit exactly the two files with a conventional-commit message.

Self-review confirmed every FR/NFR from the spec maps to a concrete step, the arch review's `using` directive caveat is explicitly handled, no placeholders, and naming/types are consistent throughout.