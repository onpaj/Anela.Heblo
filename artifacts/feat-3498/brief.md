# [coverage-gap] Catalog/StockUpOperationResult: IsSuccess predicate and factory methods untested

## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpOperationResult.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The `IsSuccess` computed property returns `true` for exactly three statuses — `Success`, `AlreadyCompleted`, and `AlreadyInShoptet` — and `false` for the remaining ones (`InProgress`, `PreviouslyFailed`, `Failed`). No test exercises this predicate.

There are also seven static factory methods (`Success`, `AlreadyCompleted`, `PreviouslyFailed`, `InProgress`, `AlreadyInShoptet`, `SubmitFailed`, `VerificationFailed`, `VerificationError`). None are tested for the shape of the result they produce.

## Why it matters
`IsSuccess` is used by callers to decide whether a stock-up operation succeeded. If a new `StockUpResultStatus` value is added later (e.g. a new retry-eligible state), it will default to `false` in `IsSuccess` — which may or may not be correct. Without a test pinning the current set of success statuses, a refactor that restructures the enum could silently change which operations the caller treats as successful.

## Suggested approach
- Parameterized test over all `StockUpResultStatus` values asserting that `IsSuccess` returns `true` for `Success`, `AlreadyCompleted`, `AlreadyInShoptet` and `false` for all others.
- One test per factory method asserting the `Status`, `Message`, and `Operation`/`Exception` fields are populated correctly. ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-07-06. Based on CI run #28716987459 (2ad2a2593e1834798a3def9ac2551b46c2e595cb)._
