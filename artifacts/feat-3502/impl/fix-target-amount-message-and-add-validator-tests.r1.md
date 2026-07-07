# Implementation: fix-target-amount-message-and-add-validator-tests

## Changes

`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs`
- Corrected the `WithMessage` text on the `TargetAmount` upper-bound rule from `"Target amount must be less than 1,000"` to `"Target amount must be less than 100,000"`, matching the actual enforced rule `LessThan(100000)`. The rule itself is unchanged — only the misleading message text was fixed.

`backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs` (new)
- Covers `TargetAmount`: valid values (500, 99999, 0, 1), invalid values (100001, 100000, -1), the corrected upper-bound error message, the exclusive upper bound at exactly 100000, the inclusive lower bound at 0, and the negative-value error message.
- Covers `ProductCode`: typical value, exactly 50 characters (boundary), null/empty (required-message), and 51 characters (exceeds-length message).
- A final `ValidRequest_PassesAllValidation` smoke test asserting a fully valid request produces no errors.

## Test result

`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitStockTakingRequestValidatorTests"` — 19/19 passed.

## Status: PASS
