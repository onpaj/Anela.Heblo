# Implementation: scaffold-file-and-environment-guard-tests (r1)

Created `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs` exactly per task context: shared helpers plus 4 tests (Production pass-through, Staging/Development theory x2, already-authenticated short-circuit).

Verification: `dotnet test --filter FullyQualifiedName~E2ETestAuthenticationMiddlewareTests` -> 4 passed. `dotnet format --verify-no-changes` on the file is clean.

Note: on origin/main, `RecurringJobSeeder.cs:51` fails to compile (`HasSeededFieldsChanged(existing, config)` passes a List instead of `existingConfig`). Unrelated to this task, so NOT fixed in this branch; it was patched locally only to run the tests and then reverted.
