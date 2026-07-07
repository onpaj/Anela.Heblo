# Code Review: fix-target-amount-message-and-add-validator-tests

## Summary
The implementation fixes the misleading `TargetAmount` upper-bound error message to say "100,000" (matching the actual `LessThan(100000)` rule, which is left unchanged) and adds thorough FluentValidation test coverage for `SubmitStockTakingRequestValidator`, including the previously-uncovered lower bound and `ProductCode` rules.

## Review Result: PASS

### task: fix-target-amount-message-and-add-validator-tests
**Status:** PASS

## Docs to Update
(none — internal validator message fix and test coverage only, no public behavior or docs affected)

## Overall Notes
- Verified the diff: only the `WithMessage` string changed on the `TargetAmount` upper-bound rule; the `LessThan(100000)` rule itself is untouched, matching the brief's guidance not to tighten the rule.
- 19/19 tests pass (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitStockTakingRequestValidatorTests"`).
- Coverage now includes: valid/invalid `TargetAmount` boundary values (0, 1, 99999, 100000, 100001, -1), the corrected error message text, and `ProductCode` required/length rules (50-char boundary, 51-char failure, null/empty).
