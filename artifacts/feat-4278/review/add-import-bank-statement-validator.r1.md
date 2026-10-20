# Code Review: add-import-bank-statement-validator

## Summary
The implementation adds `ImportBankStatementRequestValidator` (FluentValidation) covering both FR-1 (`AccountName` required + must match a configured account) and FR-2 (`DateFrom <= DateTo`), with a matching xUnit test suite. It correctly identified and fixed a FluentValidation `.When()` scoping bug present in the task-context's own sample code (a trailing `.When()` on a chained rule set gates every validator in that chain, not just the last one), which would otherwise have silently broken the "empty AccountName" acceptance criterion. All 6 tests pass; full solution build is clean (0 errors, pre-existing warnings only); `dotnet format --verify-no-changes` reports no issues on the two new files.

## Review Result: PASS

### task: add-import-bank-statement-validator
**Status:** PASS

Verified against spec.r1.md:
- FR-1 (AccountName required + must match configured account): satisfied — `NotEmpty()` and `Must(...)` rules, both independently triggering (fixed from the buggy sample).
- FR-2 (DateFrom <= DateTo, unconditional since both are non-nullable `DateTime`): satisfied — matches `GetBankStatementListRequestValidator`'s equivalent rule and message text exactly ("DateFrom must not be later than DateTo").
- Scope: correctly limited to just the validator + its tests; does not touch DI registration (FR-3) or the handler's redundant `ArgumentException` (FR-4) — those are separate tasks in this feature's plan (`register-import-bank-statement-validator-in-di`, `remove-redundant-argumentexception-from-import-handler`), so this is correct, not incomplete.
- Architecture adherence: follows the existing `GetBankStatementListRequestValidator` pattern in the same `Validators/` folder (same namespace convention, same `IOptions<T>` DI shape referenced in the spec's API/Interface Design section).
- Tests: all 6 required test cases present, verified passing via `dotnet test --filter "FullyQualifiedName~ImportBankStatementRequestValidatorTests"` → `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.
- Correctness: no logic errors found. The account-name-match rule is correctly ordinal/case-sensitive (`a.Name == name`), matching the existing handler's comparison per FR-1's acceptance criteria. NFR-2 (no new info disclosure) is respected — the new validator message reuses the same `Account name {name} not found in {ConfigurationKey} configuration.` wording already present in the handler's own exception message; it does not additionally enumerate all configured account names to the client (the existing handler message's `Available accounts: ...` suffix is not reproduced in the new validator message, so this is strictly less verbose than the status quo — no regression).

## Docs to Update
None. This is an internal validator addition with no public API shape change, no new CLI, no new config key, and no new agent/pipeline behavior — nothing in the documented "Common candidates" list applies.

## Overall Notes
No cross-cutting concerns. The deviation from the task-context's literal Step 3 code sample (splitting the `AccountName` rule into two `RuleFor` calls instead of one chained `.When(...)`) is a correctness fix, not a scope change, and is clearly called out in the implementation's Notes/PR Summary — appropriate given the task's own acceptance tests would otherwise fail.
