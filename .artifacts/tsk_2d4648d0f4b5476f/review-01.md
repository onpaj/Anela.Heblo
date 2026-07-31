# Review: Require authentication on `ManufactureSettingsController`

## Verdict: done

## What was checked

Read plan-01.md, design-01.md, architecture-01.md, development-01.md, and the actual diff (`git show HEAD`), then independently re-verified the claims rather than trusting the development notes:

1. **Diff matches design exactly.** `ManufactureSettingsController` now:
   - derives from `BaseApiController` (was `ControllerBase`)
   - carries class-level `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]`
   - no longer has `[AllowAnonymous]` / the `Microsoft.AspNetCore.Authorization` using
   - `GetSettings` returns `Task<ActionResult<GetManufactureSettingsResponse>>` and routes through `HandleResponse`

2. **Matches sibling pattern.** Compared against `ManufacturedProductInventoryController` — identical shape (class-level `[FeatureAuthorize(Feature.Manufacture_*)]` + `BaseApiController`).

3. **`Feature.Manufacture_ManufactureOrders` is a real, already-registered feature** — confirmed in `Feature.generated.cs`, `AccessRoles.generated.cs`, `AccessMatrix.generated.cs`. No codegen changes needed, consistent with the design's stated scope.

4. **`HandleResponse<T>` constraint satisfied** — `GetManufactureSettingsResponse : BaseResponse`, confirmed by reading the source.

5. **Independently ran the build and tests** (did not just trust the development note):
   - `dotnet build Anela.Heblo.sln` → 0 errors, 251 pre-existing unrelated warnings, ~9 min.
   - `dotnet test --filter "FullyQualifiedName~ManufactureSettings|FullyQualifiedName~GateConsistencyTests|FullyQualifiedName~AuthorizationIntegrationTests"` → **17 passed, 1 skipped (pre-existing `AdminGroups_ReturnsSeededGroups`, unrelated), 0 failed.** This includes both new `ManufactureSettingsControllerAuthorizationTests`, the two untouched content tests, and `GateConsistencyTests.EveryGatedEndpoint_HasFeatureAuthorize` / `EveryMenuPath_FeatureHasController` passing with this controller now included in the gated set (i.e., the controller is genuinely enforced by the repo-wide consistency check, not just by the two new hand-written tests).
   - `dotnet format --verify-no-changes` on the three changed files → exit 0, no formatting drift.

6. **Test design is sound given a real constraint.** Verified in `MockAuthenticationHandler` that mock auth unconditionally authenticates as `SuperUser` regardless of the `Authorization` header — an HTTP-level anonymous-access test genuinely cannot prove or disprove the gate in this test environment. The reflection-based approach (asserting the class carries `[FeatureAuthorize]` and the action has no `[AllowAnonymous]`), following the existing `GridLayoutsControllerAuthorizationTests`/`DiagnosticsControllerTests` pattern, is the correct substitute and is reinforced by `GateConsistencyTests` picking the controller up automatically.

7. **Frontend impact confirmed nil.** Grepped `useManufactureSettings`/`manufactureSettings_GetSettings` — only consumed by `CreateManufactureOrderModal.tsx`, `ManufactureOrderFilters.tsx`, `BasicInfoSection.tsx`, all via `getAuthenticatedApiClient()`. No anonymous frontend path exists; no frontend changes required or made.

## Findings

None. The change closes the finding precisely: the endpoint no longer opts out of the secure-by-default policy, is gated behind the same `Feature.Manufacture_*` convention as its siblings, and routes through `HandleResponse` so a future `Success == false` response would map to the correct HTTP status. Scope is minimal and surgical — no unrelated files touched, no new `Feature` enum value introduced where an existing one fit.

```json
{"outcome": "done", "summary": "Diff matches design-01.md exactly and fixes the finding: ManufactureSettingsController now derives from BaseApiController, is gated by [FeatureAuthorize(Feature.Manufacture_ManufactureOrders)], drops [AllowAnonymous], and routes through HandleResponse — same pattern as sibling Manufacture controllers. Independently rebuilt and re-ran the test filter (17 passed, 1 pre-existing unrelated skip, 0 failed, including GateConsistencyTests now covering this controller) and dotnet format (clean). Reflection-based test replacement is well-justified by MockAuthenticationHandler's unconditional SuperUser auth. Frontend consumer already authenticates; no frontend changes needed or made. No issues found."}
```
