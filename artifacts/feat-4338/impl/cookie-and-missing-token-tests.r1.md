# Implementation: cookie-and-missing-token-tests (r1)

Appended 3 tests to `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ETestAuthenticationMiddlewareTests.cs` exactly per task context: `InvokeAsync_CookieAuthSucceeds_SetsUserAndCallsNext`, `InvokeAsync_NoTokenHeader_PassesThroughWithoutResolvingValidator`, `InvokeAsync_EmptyTokenHeader_PassesThroughWithoutResolvingValidator`.

Verification: `dotnet test --filter FullyQualifiedName~E2ETestAuthenticationMiddlewareTests` -> 7 passed, 0 failed. (RecurringJobSeeder.cs:51 compile error on main was patched locally only to run tests, then reverted; unrelated.)
