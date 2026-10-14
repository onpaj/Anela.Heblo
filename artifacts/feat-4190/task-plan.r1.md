# Dedicated `Reschedule` domain method for `MarketingAction` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a focused `Reschedule` domain method to `MarketingAction` and switch `MoveMarketingActionHandler` to call it instead of the general-purpose `UpdateDetails`, removing the read-then-write-unchanged `Title`/`Description`/`ActionType` pattern.

**Architecture:** No new components. One new narrow-purpose mutator method added to the `MarketingAction` domain entity (alongside its existing `SoftDelete`/`Restore`/`MarkOutlookSynced`-style methods), plus a one-call-site swap in `MoveMarketingActionHandler.Handle`. `UpdateDetails` is untouched and keeps serving `UpdateMarketingActionHandler`.

**Tech Stack:** .NET 8, C# domain entity + MediatR application handler, xUnit + FluentAssertions + Moq for tests.

---

## Task Overview

1. `add-reschedule-domain-method` — add `MarketingAction.Reschedule(...)` with TDD test coverage.
2. `switch-handler-to-reschedule` — update `MoveMarketingActionHandler` to call `Reschedule` instead of `UpdateDetails`, verified against the existing handler test suite.

---

### task: add-reschedule-domain-method

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:246` (add new method immediately before `UpdateDetails`, which starts at line 246)
- Create: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRescheduleTests.cs`

This task implements FR-1 and FR-3 of `spec.r1.md`.

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRescheduleTests.cs` with the following content. It mirrors the structure and style of the sibling `MarketingActionRestoreTests.cs` and `MarketingActionUpdateDetailsTests.cs` in the same directory, and uses the existing `MarketingActionTestBuilder`:

```csharp
using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionRescheduleTests
    {
        private static readonly DateTime FixedUtcNow =
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        private static MarketingAction NewAction() =>
            new MarketingActionTestBuilder()
                .WithTitle("Original Title")
                .WithDescription("Original Description")
                .WithActionType(MarketingActionType.Blog)
                .WithStartDate(FixedUtcNow)
                .WithCreatedAt(FixedUtcNow)
                .WithModifiedAt(FixedUtcNow)
                .WithCreatedBy("user-1")
                .Build();

        [Fact]
        public void Reschedule_UpdatesStartAndEndDateToPassedInValues()
        {
            var action = NewAction();
            var newStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var newEnd = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);

            action.Reschedule(
                startDate: newStart,
                endDate: newEnd,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.StartDate.Should().Be(newStart);
            action.EndDate.Should().Be(newEnd);
        }

        [Fact]
        public void Reschedule_AllowsNullEndDate()
        {
            var action = NewAction();
            var newStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

            action.Reschedule(
                startDate: newStart,
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.StartDate.Should().Be(newStart);
            action.EndDate.Should().BeNull();
        }

        [Fact]
        public void Reschedule_LeavesTitleDescriptionAndActionTypeUnchanged()
        {
            var action = NewAction();

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.Title.Should().Be("Original Title");
            action.Description.Should().Be("Original Description");
            action.ActionType.Should().Be(MarketingActionType.Blog);
        }

        [Fact]
        public void Reschedule_SetsModifiedAtToProvidedUtcNow()
        {
            var action = NewAction();
            var moment = new DateTime(2026, 7, 4, 9, 30, 0, DateTimeKind.Utc);

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: moment);

            action.ModifiedAt.Should().Be(moment);
        }

        [Fact]
        public void Reschedule_SetsModifiedByUserIdAndUsernameFromArguments()
        {
            var action = NewAction();

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-42",
                modifiedByUsername: "bob",
                utcNow: FixedUtcNow);

            action.ModifiedByUserId.Should().Be("user-42");
            action.ModifiedByUsername.Should().Be("bob");
        }

        [Fact]
        public void Reschedule_DefaultsModifiedByUsernameToUnknownUserWhenNull()
        {
            var action = NewAction();

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-42",
                modifiedByUsername: null,
                utcNow: FixedUtcNow);

            action.ModifiedByUsername.Should().Be("Unknown User");
        }

        [Fact]
        public void Reschedule_DoesNotModifyCreatedAuditFields()
        {
            var action = NewAction();
            var originalCreatedAt = action.CreatedAt;
            var originalCreatedByUserId = action.CreatedByUserId;
            var originalCreatedByUsername = action.CreatedByUsername;

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.CreatedAt.Should().Be(originalCreatedAt);
            action.CreatedByUserId.Should().Be(originalCreatedByUserId);
            action.CreatedByUsername.Should().Be(originalCreatedByUsername);
        }

        [Fact]
        public void Reschedule_DoesNotModifyDeletionOrOutlookFields()
        {
            var action = NewAction();

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.IsDeleted.Should().BeFalse();
            action.DeletedAt.Should().BeNull();
            action.OutlookEventId.Should().BeNull();
            action.OutlookSyncStatus.Should().Be(MarketingSyncStatus.NotSynced);
        }

        [Fact]
        public void Reschedule_DoesNotModifyProductAssociationsOrFolderLinks()
        {
            var action = NewAction();
            action.AssociateWithProduct("PROD-1", FixedUtcNow);
            action.LinkToFolder("folder-key", MarketingFolderType.General, FixedUtcNow);

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.ProductAssociations.Should().ContainSingle(p => p.ProductCodePrefix == "PROD-1");
            action.FolderLinks.Should().ContainSingle(f => f.FolderKey == "folder-key");
        }
    }
}
```

- [ ] **Step 2: Run the new test file to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionRescheduleTests"`

Expected: Build FAILS — `'MarketingAction' does not contain a definition for 'Reschedule'` (the method doesn't exist yet).

- [ ] **Step 3: Implement `Reschedule` on `MarketingAction`**

Open `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`. Immediately before the existing `UpdateDetails` method (which currently starts at line 246, right after `ClearOutlookLink`'s closing brace on line 244), insert:

```csharp
        public void Reschedule(
            DateTime startDate,
            DateTime? endDate,
            string modifiedByUserId,
            string? modifiedByUsername,
            DateTime utcNow)
        {
            StartDate = startDate;
            EndDate = endDate;
            ModifiedAt = utcNow;
            ModifiedByUserId = modifiedByUserId;
            ModifiedByUsername = modifiedByUsername ?? "Unknown User";
        }

```

The file's "Domain methods" section (starting at the `// Domain methods` comment on line 93) will then read, in order: `AssociateWithProduct`, `LinkToFolder`, `ReplaceProductAssociations`, `ReplaceFolderLinks`, `SoftDelete`, `Restore`, `MarkOutlookSynced`, `ClearOutlookLink`, `Reschedule`, `UpdateDetails`. Do not modify `UpdateDetails` itself or any other existing method — this is a pure addition.

- [ ] **Step 4: Run the new test file to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionRescheduleTests"`

Expected: PASS — all 9 facts in `MarketingActionRescheduleTests` succeed.

- [ ] **Step 5: Run the full domain test project to check for regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Domain.Marketing"`

Expected: PASS — all existing domain tests in `Anela.Heblo.Tests.Domain.Marketing` (including `MarketingActionUpdateDetailsTests`, `MarketingActionSoftDeleteTests`, `MarketingActionRestoreTests`, and the rest) still pass unmodified, alongside the new `MarketingActionRescheduleTests`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRescheduleTests.cs
git commit -m "feat(marketing): add Reschedule domain method to MarketingAction"
```

---

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
