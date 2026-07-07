## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The validator applies `LessThan(100000)` to `TargetAmount` but the `WithMessage` reads `"Target amount must be less than 1,000"`. The actual enforced limit is **100,000**, not 1,000 — but users who trigger the validation will see a misleading error message.

No test exists to detect this discrepancy. A test asserting the error message for a value of, say, 500 (which would be valid per the rule but below the stated 1,000 threshold) would immediately expose the mismatch.

The lower bound (`GreaterThanOrEqualTo(0)`) and the `ProductCode` required/length rules are also entirely uncovered.

## Why it matters
A misleading validation message confuses operators submitting stock takes. More importantly, the absence of tests means the wrong limit could be silently "fixed" in the wrong direction — tightening the rule to match the message (100,000 → 1,000) could reject legitimate quantities that are currently accepted.

## Suggested approach
- Write a FluentValidation test that submits `TargetAmount = 500` and asserts it passes validation (proving the effective limit is 100,000, not 1,000).
- Add a test for `TargetAmount = 100001` that fails with the correct message.
- Correct the error message to read "less than 100,000" (or tighten the rule to `LessThan(1000)` if that was the intent, after confirming with the domain owner). ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-07-06. Based on CI run #28716987459 (2ad2a2593e1834798a3def9ac2551b46c2e595cb)._
