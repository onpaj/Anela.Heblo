### task: manual-handlers-isalldy

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs`

Depends on `task: domain-isalldy-property`. This closes the last two production call sites of the (now three-parameter) constructor, so `dotnet build` is fully green across the solution.

- [ ] **Step 1: Update `CreateMarketingActionHandler` to compute and pass `IsAllDay`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`, change the `new MarketingAction(...)` call:

```csharp
            var action = new MarketingAction(
                title: request.Title,
                description: request.Description,
                actionType: request.ActionType,
                startDate: request.StartDate,
                endDate: request.EndDate,
                isAllDay: MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate),
                createdByUserId: currentUser.Id,
                createdByUsername: currentUser.Name,
                utcNow: now);
```

- [ ] **Step 2: Update `UpdateMarketingActionHandler` to compute and pass `IsAllDay`**

In `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`, change the `action.UpdateDetails(...)` call:

```csharp
            action.UpdateDetails(
                title: request.Title,
                description: request.Description,
                actionType: request.ActionType,
                startDate: request.StartDate,
                endDate: request.EndDate,
                isAllDay: MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate),
                modifiedByUserId: currentUser.Id,
                modifiedByUsername: currentUser.Name,
                utcNow: now);
```

- [ ] **Step 3: Fix the remaining test-only constructor call sites**

In `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs`, update the seeding call:

```csharp
        var action = new MarketingAction(
            title: $"Action {Guid.NewGuid():N}",
            description: null,
            actionType: MarketingActionType.Blog,
            startDate: DateTime.UtcNow,
            endDate: null,
            isAllDay: false,
            createdByUserId: "seed-user",
            createdByUsername: "Seeder",
            utcNow: DateTime.UtcNow);
```

In `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs`, update the seeding call:

```csharp
        var action = new MarketingAction(
            title: $"Action {Guid.NewGuid():N}",
            description: null,
            actionType: MarketingActionType.Blog,
            startDate: startDate,
            endDate: endDate,
            isAllDay: false,
            createdByUserId: "seed-user",
            createdByUsername: "Seeder",
            utcNow: DateTime.UtcNow);
```

- [ ] **Step 4: Build the whole solution to confirm no remaining call site was missed**

Run: `cd backend && dotnet build Anela.Heblo.sln`
Expected: build succeeds with 0 errors. If any error remains about a missing `isAllDay` argument, it means a constructor/`UpdateDetails` call site exists that this plan did not enumerate — locate it with `grep -rn "new MarketingAction(\|\.UpdateDetails(" backend --include=*.cs` from the repo root, and add the missing `isAllDay:` argument following the same pattern as the sites above (Graph-originated calls pass `evt.IsAllDay`; every other call passes `MarketingAction.ComputeIsAllDay(startDate, endDate)` or, for a pure test fixture with no request context, an explicit literal `false`/`true` matching the scenario under test) before proceeding.

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet test Anela.Heblo.sln`
Expected: PASS — 0 failures. This exercises every test file this plan touched (`domain-isalldy-property`, `import-mapper-isalldy`, `export-service-isalldy`, and this task) plus every other existing test in the solution, confirming nothing else broke.

- [ ] **Step 6: Run `dotnet format` to match project formatting conventions**

Run: `cd backend && dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: no formatting changes needed. If changes are reported, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then re-run the verify command to confirm.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs
git commit -m "fix(marketing): derive IsAllDay for manually created/edited actions

Manually-created actions have no UI-level all-day toggle (out of scope for
#4238 per spec NFR-2), so CreateMarketingActionHandler and
UpdateMarketingActionHandler derive IsAllDay from the same midnight-to-
midnight rule the old export-side guess used, preserving today's observable
export behavior for actions that don't originate from Outlook import."
```

---

## Follow-ups (not implemented by this plan — carried forward from spec Open Questions)

- **`docs/integrations/microsoft-graph-calendar.md` does not exist** in this repository (verified against the full working tree and git history during the analyzing phase), despite the issue referencing it for context. This plan does not create it — creating/backfilling that doc is a product/documentation decision outside this bug-fix's scope (spec Open Question 1). Recommend a separate follow-up issue if the doc is wanted.
- **Exposing `IsAllDay` on `MarketingActionDto`** for read-side transparency (spec Open Question 3) is not implemented here — no consumer needs it yet, and adding it later is a small, additive, non-breaking change with no migration impact.

## Self-Review (performed against spec.r1.md, arch-review.r1.md, design.r1.md)

**Spec coverage:**
- FR-1 (persist `IsAllDay`) → `task: domain-isalldy-property`.
- FR-2 (set from Graph on import, including `HasChanges`) → `task: import-mapper-isalldy`.
- FR-3 (read on export, delete `IsDateOnly`) → `task: export-service-isalldy`.
- FR-4 (manual-path default, shared helper) → `ComputeIsAllDay` in `task: domain-isalldy-property`, consumed in `task: manual-handlers-isalldy`.
- FR-5 (import→export round-trip regression test) → `task: export-service-isalldy`, Step 2 (two new tests: timed-midnight-to-midnight and genuine-all-day).
- FR-6 / NFR-1 (migration + backfill) → `task: persistence-migration-isalldy`.
- NFR-2 (no DTO/API/UI change) → honored throughout; no task touches `MarketingActionDto`, `CreateMarketingActionRequest`, `UpdateMarketingActionRequest`, or any frontend file.
- Architect Decision 2 (`Reschedule` untouched) → honored in `task: domain-isalldy-property`, Step 3 (XML-doc comment added, no parameter added); `MoveMarketingActionHandler` is not listed in any task's Files section because it needs no change.

**Placeholder scan:** no "TBD"/"similar to Task N"/unshown code remains — every step above shows the exact code to write or the exact command to run.

**Type consistency:** `IsAllDay` (bool), `ComputeIsAllDay(DateTime, DateTime?)` (static on `MarketingAction`), and the constructor/`UpdateDetails` parameter name `isAllDay` are used identically across all five tasks.
