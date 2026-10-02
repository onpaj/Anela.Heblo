## Module / File
`backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ETestAuthenticationMiddleware.cs`

## Coverage
Line coverage: 6.7% (filter threshold: 60%)

## What's not tested
The middleware has several important branches with no test coverage:

1. **Environment guard** (line 31): the check that only allows the middleware to act in Staging or Development is not tested — specifically the path where a non-Staging/non-Development request flows straight through without any E2E auth logic.
2. **Already-authenticated short-circuit** (line 37): the path where `context.User.Identity.IsAuthenticated == true` skips the E2E logic is untested.
3. **Cookie auth path** (lines 45–57): the branch where an E2E session cookie is present and succeeds authentication is untested.
4. **Token validation failure path** (lines 83–89): the path where `tokenValidator.ValidateAsync` returns false (returns 401 with error message) is untested.
5. **Exception path in token validation** (lines 113–119): the catch block that returns 500 is untested.
6. **`ShouldBeRegistered`** static method (lines 128–132): the combined logic of environment + `UseMockAuth` config flag is untested.

## Why it matters
A regression in the environment guard could cause the middleware to be active on a request type it should not handle, or in the worst case expose mock authentication paths where they should be blocked. The `ShouldBeRegistered` method controls whether the middleware is registered at startup at all — a bug there could silently disable or always-enable E2E bypass.

## Suggested approach
Unit tests against `InvokeAsync` and `ShouldBeRegistered` using a mock `IWebHostEnvironment`:
- Production environment → middleware delegates immediately without calling any auth services
- Already-authenticated → delegates without calling cookie or token auth
- Cookie auth success → user is set on the context
- Invalid token → 401 response
- Token validator throws → 500 response
- `ShouldBeRegistered` matrix: Staging+no mock → true, Production → false, Staging+UseMockAuth=true → false

Effort: ~2–3 hours, requires mocking `IWebHostEnvironment`, `IServicePrincipalTokenValidator`, `IE2ESessionService`.

---
_Filed by weekly coverage-gap routine on 2026-09-28. Based on CI run #35977921040 (22bb3b8ff6194bdd055cb438d08b7c6633a85221)._
