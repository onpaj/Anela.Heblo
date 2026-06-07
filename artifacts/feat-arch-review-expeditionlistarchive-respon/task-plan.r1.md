Plan saved to `docs/superpowers/plans/2026-06-04-expeditionlistarchive-response-error-contract.md`.

## Summary

The plan decomposes the spec + arch-review into 11 TDD-ordered tasks:

1. **Add `ErrorCodes.InvalidBlobPath = 1808`** in the FileStorage range
2. **RED — Download test** — assert `ErrorCode == InvalidBlobPath`
3. **GREEN — Download response + handler** — parameterless `Fail()`, drop `ErrorMessage`
4. **Reprint response + handler + test** — same RED→GREEN sequence
5. **Controller** — `Download` keeps `BadRequest(response)` (binary success body); `Reprint` switches to `HandleResponse(response)` (matches every other controller, per arch amendment)
6. **Frontend `useReprintExpeditionList`** — typed shape (`success`/`errorCode`/`params`), `REPRINT_ERROR_MESSAGES` lookup → Czech string (mirrors `useResetOrderShipment`); avoids the UX regression of showing raw `"InvalidBlobPath"` to the user
7. **Frontend `useRunExpeditionListPrintFix`** — `errorCode` first, defensive `errorMessage` fallback (its endpoint is on an out-of-scope controller, per arch Decision 4)
8. **Page test mock** — `{ success: true, errorCode: null, params: null }` (arch-review gap the spec missed)
9. **Verification grep** for stray `ErrorMessage` / old `Fail("...")` callsites
10. **Backend gate** — `dotnet build` + `dotnet format --verify-no-changes` + filtered tests
11. **Frontend gate** — `npm run build` + `npm run lint` + tests + OpenAPI regen check

Every task has exact file paths, complete code (no placeholders), expected build/test output, and a commit step. Self-review maps each spec FR/NFR to a task. Skipping execution handoff prompt per pipeline rule — plan file is the artifact.