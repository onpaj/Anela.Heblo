### task: switch-handler-to-reschedule

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs:60-68`
- Test (review only, no edits expected): `backend/test/Anela.Heblo.Tests/Application/Marketing/MoveMarketingActionHandlerTests.cs`

**Depends on:** `add-reschedule-domain-method` (requires `MarketingAction.Reschedule` to exist).

This task implements FR-2 of `spec.r1.md`. It has no new test file of its own — FR-2's acceptance criteria are verified by the *existing* `MoveMarketingActionHandlerTests.cs` suite continuing to pass unmodified, per FR-3's second bullet and the arch review's "Prerequisites"/"Risks" sections, which confirm no test in that file asserts on `UpdateDetails` being called (they assert on resulting `MarketingAction` property values only).

- [ ] **Step 1: Run the existing handler test suite to record the current passing baseline**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MoveMarketingActionHandlerTests"`

Expected: PASS — all existing facts in `MoveMarketingActionHandlerTests` pass before this task's change (baseline, still calling `UpdateDetails`).

- [ ] **Step 2: Replace the `UpdateDetails` call with `Reschedule`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs`, replace lines 60-68:

```csharp
            action.UpdateDetails(
                title: action.Title,
                description: action.Description,
                actionType: action.ActionType,
                startDate: request.StartDate,
                endDate: request.EndDate,
                modifiedByUserId: currentUser.Id,
                modifiedByUsername: currentUser.Name,
                utcNow: now);
```

with:

```csharp
            action.Reschedule(
                startDate: request.StartDate,
                endDate: request.EndDate,
                modifiedByUserId: currentUser.Id,
                modifiedByUsername: currentUser.Name,
                utcNow: now);
```

No other line in this file changes — the authorization check, not-found check, Outlook push block, DB save error handling, logging, and response construction (lines 40-59 and 70-111) stay exactly as they are.

- [ ] **Step 3: Run the existing handler test suite to verify no regression**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MoveMarketingActionHandlerTests"`

Expected: PASS — the exact same set of facts from Step 1 still pass, unmodified, including:
- `Handle_LeavesFolderLinksAndProductAssociationsUnchanged`
- `Handle_LeavesTitleDescriptionActionTypeUnchanged`
- `Handle_UpdatesStartAndEndDate`
- `Handle_ReturnsUnauthorized_WhenUserIsNotAuthenticated`
- `Handle_ReturnsNotFound_WhenActionDoesNotExist`
- `Handle_UpdatesOutlookEvent_WhenActionHasEventIdAndPushEnabled`
- `Handle_SkipsOutlookSync_WhenActionHasNoEventId`
- `Handle_SkipsOutlook_WhenPushDisabled`
- `Handle_ReturnsForbiddenError_WhenOutlookUpdateThrows403`
- `Handle_ReturnsSyncError_WhenOutlookUpdateThrowsNon403`
- `Handle_ReturnsDatabaseError_WhenDbSaveFails`

If any of these fail, do not weaken or delete the assertion — investigate why `Reschedule`'s behavior diverges from `UpdateDetails`'s for that scenario (per FR-2's acceptance criteria, there should be no such divergence; a failure here means Step 2 was applied incorrectly, not that the test is wrong).

- [ ] **Step 4: Run the full backend test suite**

Run: `dotnet test`

Expected: PASS — the full solution's test suite passes, confirming no other code (e.g. `UpdateMarketingActionHandlerTests`, which still calls `UpdateDetails` via a different handler) was affected by this change.

- [ ] **Step 5: Build and format check**

Run: `dotnet build`

Expected: Build succeeds with no new warnings/errors.

Run: `dotnet format --verify-no-changes`

Expected: No formatting violations. If violations are reported, run `dotnet format` (without `--verify-no-changes`) to apply fixes, then re-run Step 3 and Step 4 to confirm tests still pass after formatting.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs
git commit -m "refactor(marketing): MoveMarketingActionHandler calls Reschedule instead of UpdateDetails"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (add `Reschedule` domain method, exact signature, no title/description/actionType touch, no normalization calls) → `add-reschedule-domain-method` Step 3.
- FR-2 (handler calls `Reschedule` instead of `UpdateDetails`, no other logic changes) → `switch-handler-to-reschedule` Step 2.
- FR-3 (dedicated domain test file mirroring sibling structure; existing handler test suite continues to pass unmodified) → `add-reschedule-domain-method` Steps 1-5 (new test file) and `switch-handler-to-reschedule` Steps 1 and 3 (existing suite, before/after comparison, zero edits needed).
- NFR-1 (no perf impact) → satisfied structurally; no additional step needed (five field assignments, no new I/O).
- NFR-2 (no auth/audit behavior change) → covered by `switch-handler-to-reschedule` Step 3's full existing-suite pass, which includes the unauthorized/not-found/audit-field assertions.
- Out of Scope items (no `UpdateDetails` change, no contract/DTO change, no other handler audit, no migration) → nothing in either task touches those areas; no step required.

**Placeholder scan:** No "TBD"/"implement later"/"add appropriate error handling" placeholders — every step has literal, complete code and exact commands.

**Type consistency:** `Reschedule(DateTime startDate, DateTime? endDate, string modifiedByUserId, string? modifiedByUsername, DateTime utcNow)` is used identically in the domain method (task 1, Step 3), its tests (task 1, Step 1), and the handler call site (task 2, Step 2) — same parameter names, types, and order throughout.
