# Implementation: token-validation-outcome-tests (r1)

## What was implemented
Appended 3 tests to `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs` exactly per task context: `InvokeAsync_InvalidToken_Returns401AndDoesNotCallNext` (FR-5), `InvokeAsync_ValidToken_SetsSyntheticUserAndOverrideCookieAndCallsNext` (FR-6), `InvokeAsync_TokenValidatorThrows_Returns500AndDoesNotCallNext` (FR-7).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs`

## Tests
`dotnet test --filter FullyQualifiedName~E2ETestAuthenticationMiddlewareTests` -> 10 passed, 0 failed. dotnet format verify clean. (RecurringJobSeeder.cs:51 compile error on main patched locally only to run tests, then reverted; unrelated.)

## How to verify
Run the filter above.

## Notes
None.

## PR Summary
Added token-validation outcome tests (invalid 401, valid synthetic user + override cookie, validator exception 500) for E2ETestAuthenticationMiddleware.

### Changes
- `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs` — 3 new tests

## Status
DONE
