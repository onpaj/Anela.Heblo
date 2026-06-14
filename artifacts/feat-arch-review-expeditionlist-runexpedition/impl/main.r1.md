---

# Implementation: Remove Dead `ErrorMessage` Field from `RunExpeditionListPrintFixResponse`

## What was implemented

Removed the always-null `ErrorMessage` property from `RunExpeditionListPrintFixResponse`, the redundant `Success = true` assignment in its handler, and propagated the change through the generated TypeScript client, the hand-coded frontend mirror type, and the hook test mock. Added a lock-in xUnit test to guard the `BaseResponse()` default.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/RunExpeditionListPrintFixResponseTests.cs` — new lock-in test asserting `Success == true` without explicit assignment
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs` — removed redundant `Success = true,`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixResponse.cs` — removed `public string? ErrorMessage { get; set; }`
- `frontend/src/api/generated/api-client.ts` — regenerated (10 lines removed from `RunExpeditionListPrintFixResponse`/`IRunExpeditionListPrintFixResponse`)
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — removed `errorMessage: string | null` from `RunExpeditionListPrintFixResult`
- `frontend/src/api/hooks/__tests__/useExpeditionList.test.ts` — trimmed `errorMessage: null` from success-path mock body

## Tests

- `backend/test/.../RunExpeditionListPrintFixResponseTests.cs` — 1/1 xUnit test passes; verifies inherited `Success == true` default
- Full `Features.ExpeditionList` backend suite: 34/34 pass
- Frontend Jest tests: **pre-existing Babel parse failure** for all TypeScript test files (from commit `7b837bc5`, not introduced by this change)

## How to verify

```bash
# Backend
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.ExpeditionList"

# Frontend build + lint
cd frontend && npm run build && npm run lint

# Wire shape check
grep -n "RunExpeditionListPrintFixResponse" frontend/src/api/generated/api-client.ts | head -5
```

## Notes

- The `errorData?.errorMessage` line in `useExpeditionList.ts` (inside `if (!response.ok)`) was intentionally left untouched per arch-review Decision 3 — it reads ASP.NET exception middleware payloads, not the typed DTO.
- Jest is broken project-wide (pre-existing from `7b837bc5`) due to missing TypeScript support in the Babel transform config. All 4 test suites that were run had the same `SyntaxError: Missing semicolon` at TypeScript generic syntax. This is not introduced by this change.

## PR Summary

Removes the always-null `ErrorMessage` property from `RunExpeditionListPrintFixResponse` and its redundant `Success = true` handler assignment, then propagates the change to the generated TypeScript client, hand-coded frontend mirror type, and hook test mock.

The field was pure template residue — never assigned by the handler, duplicating the structured error contract already provided by `BaseResponse` (`ErrorCode` + `Params`). Removing it before frontend code grows a dependency on the permanently-null property closes YAGNI surface area and reinforces the project-wide error-reporting pattern established in the `ExpeditionListArchive` realignment.

### Changes
- `backend/test/.../RunExpeditionListPrintFixResponseTests.cs` — new lock-in test for `BaseResponse()` default
- `backend/src/.../RunExpeditionListPrintFixHandler.cs` — removed redundant `Success = true,`
- `backend/src/.../RunExpeditionListPrintFixResponse.cs` — removed dead `ErrorMessage` property
- `frontend/src/api/generated/api-client.ts` — regenerated (NSwag, 10 lines removed)
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — removed `errorMessage` from hand-coded `RunExpeditionListPrintFixResult`
- `frontend/src/api/hooks/__tests__/useExpeditionList.test.ts` — trimmed dead field from success-path mock

## Status

DONE_WITH_CONCERNS

**Concern:** Frontend Jest tests fail project-wide with a Babel TypeScript parsing error (`SyntaxError: Missing semicolon` on TypeScript generic syntax at line 12 of test files). This is a pre-existing issue from commit `7b837bc5` and affects all test suites, not just the ones we touched. Our code changes are correct; the test runner environment needs a separate fix (likely `@babel/preset-typescript` missing or misconfigured in Jest transform config).