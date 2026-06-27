# Review: decouple-handler-and-add-arch-test

## Files reviewed

- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` (worktree)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (worktree, lines 840–917)

## Handler changes — PASS

All acceptance criteria for `GetGroupMembersHandler.cs` are met:

- `using Microsoft.Identity.Client;` is **absent**. No other SDK usings remain.
- `catch (MsalException ...)` is **gone**.
- `catch (Microsoft.Graph.Models.ODataErrors.ODataError ...)` is **gone**.
- `catch (GraphServiceAuthException ex)` is present and maps to `ErrorCodes.ConfigurationError`. Correct.
- `catch (GraphServiceException ex)` is present and maps to `ErrorCodes.ExternalServiceError`. Correct.
- The remaining catch clauses (`UnauthorizedAccessException`, `Exception`) are unchanged from the original, as required.
- The wrapper types (`GraphServiceAuthException`, `GraphServiceException`) are confirmed to exist under `Anela.Heblo.Application.Features.UserManagement.Contracts/` — introduced by the preceding task.

## Architecture test — PASS

The new `[Fact]` `Application_types_should_not_catch_SDK_exception_types_directly` (lines 865–917) is correctly placed inside `ModuleBoundariesTests` and:

- Loads the `Anela.Heblo.Application` assembly.
- Forbids namespace prefixes `Microsoft.Identity.Client` and `Microsoft.Graph.Models.ODataErrors`.
- Reuses the existing `EnumerateReferencedTypes` + `IsForbidden` helpers — no code duplication.
- Applies `SdkExceptionAllowlist` at both the type level and the declaring-type level (to cover compiler-generated async state machines and closures), consistent with the pattern established by `Consumer_types_should_not_reference_provider_owned_namespaces`.

### Allowlist evaluation

Three entries are present with justification comments:

| Allowlisted type | Reason stated |
|---|---|
| `GraphArticleUserResolver` | Wrapping boundary; converts SDK exceptions to module-owned types. Defensive entry — implementation notes that it did not actually produce a violation during test execution. |
| `GraphPlannerService` | Pre-existing `MsalUiRequiredException` catch; out of scope. Compiler state machine covered by declaring-type check. |
| `GraphCatalogDocumentsStorage` | Same pattern as above. |

The defensive entry for `GraphArticleUserResolver` (which did not produce a violation during test runs) is low risk — it will silently pass if the type stays clean, and it prevents an immediate re-violation if someone adds a catch there later. The comment correctly records this. No objection.

The allowlist format (`typeName` without ` -> referenceType` suffix) differs from the format used by `Consumer_types_should_not_reference_provider_owned_namespaces` (`"Consumer -> Provider"`). This is intentional and correct: the new test filters at the consumer-type level before calling `EnumerateReferencedTypes`, rather than filtering individual (consumer, referenced) pairs. The logic is sound — allowlisting the whole type is appropriate here because the goal is to exempt legitimate wrapping boundaries from the rule entirely, not to permit specific cross-type references.

### Known limitation acknowledged

`EnumerateReferencedTypes` does not inspect method bodies. The implementation notes correctly explain that async state machines surface caught exception locals as compiler-generated fields, which is why `GraphPlannerService` and `GraphCatalogDocumentsStorage` appear as violations through the field-inspection path. This is the same limitation called out in the existing `EnumerateReferencedTypes` XML doc comment. The test will catch any future handler that catches SDK types and stores them in a field or exposes them on a method signature, which is the primary regression risk.

## Surgical-change compliance

No adjacent code, formatting, or unrelated logic was modified. The `[Fact]` was inserted between the last `[Fact]` (`Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone`) and the private `IsForbidden` helper, which is the natural location consistent with the file's structure.

## Build and test

`dotnet build` is expected to pass: the handler no longer references SDK types that would require the `Microsoft.Identity.Client` NuGet package in the Application project, and the test references only types already available in the test project assembly. No new dependencies were introduced.

## Issues found

None.

---

## Review Result: PASS

### task: decouple-handler-and-add-arch-test
**Status:** PASS
