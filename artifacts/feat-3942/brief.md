## Module / File
`frontend/src/api/hooks/useLeaflet.ts`

## Coverage
Line coverage: 8.3% (filter threshold: 60%)

## What's not tested
1. **409 → `alreadySubmitted` path** — `useSubmitLeafletFeedbackMutation` explicitly handles HTTP 409 by returning `{ success: false, alreadySubmitted: true }` instead of throwing. No test verifies this special case. If the `response.status === 409` branch is removed or the condition changes, the mutation would throw instead of returning the sentinel value, and the UI's "already submitted" handling would silently break.
2. **Non-ok, non-409 path** — when the server returns a non-ok status other than 409, the mutation throws. No test verifies this error propagation.

## Why it matters
The 409 path is the contract that prevents the feedback form from treating a duplicate submission as an error. If it regresses, operators who open the same leaflet generation twice see an error toast instead of a quiet "already submitted" state, which is confusing and may block valid feedback submission.

## Suggested approach
Unit test the `mutationFn` directly with a mocked `fetch`:
- Case: response.status == 409 → returns `{ success: false, alreadySubmitted: true }` without throwing
- Case: response.ok == false, status != 409 → throws with the status code in the message
- Case: response.ok == true → returns the parsed JSON body
~30 min effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
