# Unit tests for EcomailOptionsValidator BackfillFrom date guards

## Problem
`EcomailOptionsValidator` (Domain/Features/Ecomail) has 44.4% line coverage. The `BackfillFrom` future-date and pre-2020 guards (an `if / else if` chain) are untested, along with the other rules.

## Scope
Add a unit-test class only. No production code changes.

## Requirements
1. Valid default options -> `Succeeded`.
2. `BackfillFrom` = today (UTC) -> success (boundary of `>`).
3. `BackfillFrom` = tomorrow (UTC) -> fail, message contains "must not be in the future".
4. `BackfillFrom` = 2019-12-31 -> fail, message contains "must not be earlier than 2020-01-01".
5. `BackfillFrom` = 2020-01-01 -> success (lower boundary).
6. Branches independent: a failing future date does not also yield the earliest-date error (else-if).
7. `BaseUrl` relative / empty -> fail "BaseUrl".
8. `HttpTimeoutSeconds` 0 and 601 -> fail; 1 and 600 -> success.
9. `RecomputeWindowMonths` 0 -> fail; 1 -> success.
10. Multiple errors accumulate in `Failures`.

## Acceptance
All tests pass with `dotnet test --filter EcomailOptionsValidatorTests`; validator line coverage >= 60% (expected ~100%).

## Out of scope
Changing validator behavior (e.g. the "current month" comment vs. day-level comparison).
