### task: controller-simplification

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs`

- [ ] **Step 1: Confirm the existing controller lint test still targets `Put` correctly (no change expected, run before editing as a baseline)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlagsControllerLintTests"`
Expected: PASS (this test uses reflection over method attributes, unaffected by the body changes below — this step is a baseline confirmation, not new coverage).

- [ ] **Step 2: Simplify `Put()` to a straight-through dispatcher**

In `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs`, replace the `Put` method body:

```csharp
    [HttpPut("admin/{key}")]
    [FeatureAuthorize(Feature.Admin_FeatureFlags, AccessLevel.Write)]
    [ProducesResponseType(typeof(UpsertFlagOverrideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UpsertFlagOverrideResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UpsertFlagOverrideResponse>> Put(
        string key,
        [FromBody] UpsertFlagOverrideBodyDto body,
        CancellationToken ct)
        => HandleResponse(await _mediator.Send(new UpsertFlagOverrideRequest
        {
            Key = key,
            IsEnabled = body.IsEnabled,
        }, ct));
```

This removes the `var name = User.Identity?.Name;` / `Logger.LogWarning(...)` / `var updatedBy = name ?? "unknown";` lines entirely, matching the expression-bodied dispatcher style already used by `Get`, `GetAdmin`, and `Delete` in this same file.

- [ ] **Step 3: Run the full FeatureFlags test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlags"`
Expected: PASS — all tests under `Anela.Heblo.Tests.Features.FeatureFlags` (including `FeatureFlagsControllerLintTests`, `FeatureFlagRegistryFrontendMirrorTests`, and the new `UpsertFlagOverrideHandlerTests` from task 1) pass with 0 failures.

- [ ] **Step 4: Full backend build and format check**

Run: `cd backend && dotnet build`
Expected: Build succeeds, 0 errors (confirms no other reference to the removed `UpdatedBy` property or the old 2-arg handler constructor was missed).

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports changes, run `dotnet format` and re-verify.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs
git commit -m "fix(feature-flags): simplify FeatureFlagsController.Put to a straight-through dispatcher"
```

---

## Self-review notes

**Spec coverage:**
- FR-1 (resolve `UpdatedBy` inside the handler) → `task: handler-identity-resolution`, Step 4.
- FR-2 (remove `UpdatedBy` from the request) → `task: handler-identity-resolution`, Step 3.
- FR-3 (simplify the controller) → `task: controller-simplification`, Step 2.
- FR-4 (preserve behavior for the authenticated case; document the `"unknown"` → `"System"` fallback change) → covered by `UpsertFlagOverrideHandlerTests.Handle_AuthenticatedUser_PersistsResolvedDisplayNameAsUpdatedBy` and `Handle_UnauthenticatedCurrentUser_PersistsSystemFallback` respectively.
- NFR-1/NFR-2 — no code artifact required (architectural properties, not testable behavior); satisfied by construction since `ICurrentUserService` is the same shared, already-audited implementation every other handler uses.

**Placeholder scan:** none — every step includes complete, runnable code and exact commands with expected output.

**Type consistency:** `UpsertFlagOverrideHandler`'s constructor parameter order (`repo, cache, currentUserService`) is used identically in the plan's test (`CreateHandler()`) and in the handler implementation (Step 4) — verified consistent. `FeatureFlagKeys.LabelPrintingEnabled` (existing registry entry, confirmed present in `FeatureFlagRegistry.cs`) is used as the "valid key" fixture across all three new test cases instead of inventing an unregistered constant.
