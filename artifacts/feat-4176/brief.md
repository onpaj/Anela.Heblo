## Module / File
`backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/GetLeafletGenerationHandler.cs`

## Coverage
Line coverage: 16.0% (filter threshold: 60%)

## What's not tested
When the requested leaflet generation record does not exist, the handler returns a response constructed with the `ErrorCodes.LeafletFeedbackNotFound` error code and no populated data fields. No test verifies that this specific error code is returned on the not-found path, nor that all other response properties remain at their defaults (empty/null) rather than being partially populated.

## Why it matters
The frontend uses the returned error code to show an appropriate "not found" message. If the handler silently returns success with empty fields instead of the correct error code, the UI will render an empty state rather than an informative error, and callers will have no way to distinguish "not found" from "generation pending with no content".

## Suggested approach
Unit test with mocked repository returning null: assert that the response error code equals `LeafletFeedbackNotFound` and all data properties are at their defaults. ~30 min effort.

---
_Filed by weekly coverage-gap routine on 2026-09-14. Based on CI run #34699120372 (722ec6efc4c1f7e5db235606c763a2f2b4a9a374)._
