# Extract MarketingAction → MarketingActionDto Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `MarketingAction → MarketingActionDto` projection out of `GetMarketingActionsHandler.MapToDto` into a static factory `MarketingActionDto.FromEntity`, so both single-item and list use cases consume it without handler-to-handler coupling. Pure structural refactor — byte-identical at the API boundary.

**Architecture:** A new `public static MarketingActionDto FromEntity(MarketingAction action)` factory method on the existing DTO class (`Anela.Heblo.Application.Features.Marketing.Contracts.MarketingActionDto`). The factory body is a verbatim copy of the current `GetMarketingActionsHandler.MapToDto` projection. The two handlers call it directly; the old `internal static MapToDto` is deleted. The DTO already lives in the same assembly as the Domain aggregate it now references, and the `Contracts/` folder already imports `Anela.Heblo.Domain.Features.Marketing` in sibling files — so no new project reference or architectural seam is introduced.

**Tech Stack:** .NET 8, MediatR, xUnit, FluentAssertions, Moq. No new dependencies.

---

## File Structure

Three production files modified; one new test file:

- **Modify** `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingActionDto.cs`
  - Adds `using System.Linq;` and `using Anela.Heblo.Domain.Features.Marketing;`
  - Adds `public static MarketingActionDto FromEntity(MarketingAction action)` method
  - All existing properties unchanged

- **Modify** `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs`
  - Removes `using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions;` (line 5)
  - Replaces `GetMarketingActionsHandler.MapToDto(action)` (line 36) with `MarketingActionDto.FromEntity(action)`

- **Modify** `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs`
  - Replaces `Select(MapToDto)` (line 42) with `Select(MarketingActionDto.FromEntity)`
  - Deletes `internal static MarketingActionDto MapToDto(MarketingAction action)` (lines 52–80)

- **Create** `backend/test/Anela.Heblo.Tests/Application/Marketing/MarketingActionDtoTests.cs`
  - Single focused unit test that asserts byte-identical field-by-field projection for a fully-populated `MarketingAction` (covers the HIGH-severity behavioural-drift risk explicitly called out in `arch-review.r1.md`).

---

## Task 1: Add the failing parity test for `MarketingActionDto.FromEntity`

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/Marketing/MarketingActionDtoTests.cs`

This test pins the exact projection contract before we move the code, so any field drift (rename, missed property, reordered LINQ that changes ordering) fails the test. It exercises every field on `MarketingActionDto`, including the `Distinct()` de-duplication on `AssociatedProducts` and the enum-to-string projection on `ActionType` / `OutlookSyncStatus` / `FolderType`.

- [ ] **Step 1.1: Create the test file with the failing parity test**

Create `backend/test/Anela.Heblo.Tests/Application/Marketing/MarketingActionDtoTests.cs` with the following content (the test will fail to compile until Task 2 adds `FromEntity`):

```csharp
using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Application.Marketing;

public class MarketingActionDtoTests
{
    [Fact]
    public void FromEntity_ProjectsAllFields_ForFullyPopulatedAction()
    {
        // Arrange
        var createdAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var modifiedAt = new DateTime(2026, 2, 1, 11, 0, 0, DateTimeKind.Utc);
        var startDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var action = new MarketingAction
        {
            Id = 42,
            Title = "Spring Campaign",
            Description = "Spring product launch",
            ActionType = MarketingActionType.Blog,
            StartDate = startDate,
            EndDate = endDate,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            CreatedByUserId = "user-1",
            CreatedByUsername = "alice",
            ModifiedByUserId = "user-2",
            ModifiedByUsername = "bob",
            OutlookSyncStatus = MarketingSyncStatus.Synced,
            OutlookEventId = "outlook-event-99",
            ProductAssociations = new List<MarketingActionProduct>
            {
                new() { ProductCodePrefix = "PROD-A" },
                new() { ProductCodePrefix = "PROD-B" },
                new() { ProductCodePrefix = "PROD-A" }, // duplicate — must be de-duplicated
            },
            FolderLinks = new List<MarketingActionFolderLink>
            {
                new() { FolderKey = "folder-1", FolderType = MarketingFolderType.SharePoint },
                new() { FolderKey = "folder-2", FolderType = MarketingFolderType.OneDrive },
            },
        };

        // Act
        var dto = MarketingActionDto.FromEntity(action);

        // Assert
        dto.Id.Should().Be(42);
        dto.Title.Should().Be("Spring Campaign");
        dto.Description.Should().Be("Spring product launch");
        dto.ActionType.Should().Be(MarketingActionType.Blog.ToString());
        dto.StartDate.Should().Be(startDate);
        dto.EndDate.Should().Be(endDate);
        dto.CreatedAt.Should().Be(createdAt);
        dto.ModifiedAt.Should().Be(modifiedAt);
        dto.CreatedByUserId.Should().Be("user-1");
        dto.CreatedByUsername.Should().Be("alice");
        dto.ModifiedByUserId.Should().Be("user-2");
        dto.ModifiedByUsername.Should().Be("bob");
        dto.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced.ToString());
        dto.OutlookEventId.Should().Be("outlook-event-99");

        dto.AssociatedProducts.Should().Equal("PROD-A", "PROD-B");

        dto.FolderLinks.Should().HaveCount(2);
        dto.FolderLinks[0].FolderKey.Should().Be("folder-1");
        dto.FolderLinks[0].FolderType.Should().Be(MarketingFolderType.SharePoint.ToString());
        dto.FolderLinks[1].FolderKey.Should().Be("folder-2");
        dto.FolderLinks[1].FolderType.Should().Be(MarketingFolderType.OneDrive.ToString());
    }
}
```

> Note: The test uses object-initializer construction directly against `MarketingAction`, `MarketingActionProduct`, and `MarketingActionFolderLink`. All three are plain classes with a default constructor and settable properties (verified in `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`). The test does NOT call `action.AssociateWithProduct(...)` because that method normalizes input and skips duplicates — we must seed a raw duplicate in the collection to verify the projection's defensive `Distinct()`.

- [ ] **Step 1.2: Run the new test to verify it fails to compile**

Run from repo root:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: **build FAILS** with error similar to `CS0117: 'MarketingActionDto' does not contain a definition for 'FromEntity'`. This confirms the test correctly drives the addition of `FromEntity` in Task 2.

- [ ] **Step 1.3: Do NOT commit yet**

The test file is staged in the working tree but not committed. We commit it together with the production change in Task 2 so the repo is never in a broken state between commits.

---

## Task 2: Add `MarketingActionDto.FromEntity` (verbatim move of the projection)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingActionDto.cs`

The new factory body is a **byte-for-byte copy** of the current `GetMarketingActionsHandler.MapToDto` (lines 52–80 of `GetMarketingActionsHandler.cs`). Do not retype it — copy/paste. This neutralizes the HIGH-severity drift risk from `arch-review.r1.md`.

- [ ] **Step 2.1: Replace `MarketingActionDto.cs` contents**

Open `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingActionDto.cs` and replace its entire contents with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class MarketingActionDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string ActionType { get; set; } = null!;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string CreatedByUserId { get; set; } = null!;
        public string? CreatedByUsername { get; set; }
        public string? ModifiedByUserId { get; set; }
        public string? ModifiedByUsername { get; set; }
        public List<string> AssociatedProducts { get; set; } = new();
        public List<MarketingActionFolderLinkDto> FolderLinks { get; set; } = new();
        public string OutlookSyncStatus { get; set; } = "NotSynced";
        public string? OutlookEventId { get; set; }

        public static MarketingActionDto FromEntity(MarketingAction action) =>
            new()
            {
                Id = action.Id,
                Title = action.Title,
                Description = action.Description,
                ActionType = action.ActionType.ToString(),
                StartDate = action.StartDate,
                EndDate = action.EndDate,
                CreatedAt = action.CreatedAt,
                ModifiedAt = action.ModifiedAt,
                CreatedByUserId = action.CreatedByUserId,
                CreatedByUsername = action.CreatedByUsername,
                ModifiedByUserId = action.ModifiedByUserId,
                ModifiedByUsername = action.ModifiedByUsername,
                AssociatedProducts = action.ProductAssociations
                    .Select(pa => pa.ProductCodePrefix)
                    .Distinct()
                    .ToList(),
                FolderLinks = action.FolderLinks
                    .Select(fl => new MarketingActionFolderLinkDto
                    {
                        FolderKey = fl.FolderKey,
                        FolderType = fl.FolderType.ToString(),
                    })
                    .ToList(),
                OutlookSyncStatus = action.OutlookSyncStatus.ToString(),
                OutlookEventId = action.OutlookEventId,
            };
    }
}
```

Self-check before moving on: every property assignment above appears verbatim in `GetMarketingActionsHandler.MapToDto` at lines 55–79 of the current file. The order of property assignments matches the original; the `Distinct()` and `.ToList()` calls are preserved unchanged.

- [ ] **Step 2.2: Run the parity test to verify it passes**

Run from repo root:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionDtoTests"
```
Expected: **PASS** — `MarketingActionDtoTests.FromEntity_ProjectsAllFields_ForFullyPopulatedAction` passes.

- [ ] **Step 2.3: Run full backend build to confirm nothing else broke**

Run from repo root:
```bash
dotnet build
```
Expected: **build succeeds with no new warnings**. The call site `GetMarketingActionsHandler.MapToDto` still exists and still works — Tasks 3 and 4 remove it.

- [ ] **Step 2.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingActionDto.cs \
        backend/test/Anela.Heblo.Tests/Application/Marketing/MarketingActionDtoTests.cs
git commit -m "refactor(marketing): add MarketingActionDto.FromEntity factory

Adds a static factory method to the DTO that performs the exact same
projection currently in GetMarketingActionsHandler.MapToDto. Includes
a parity unit test pinning the field-by-field contract. Call sites
are switched in follow-up commits."
```

---

## Task 3: Switch `GetMarketingActionHandler` (single-item) to the new factory

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs`

This removes the cross-handler dependency — `GetMarketingActionHandler` will no longer reference `GetMarketingActionsHandler` at all.

- [ ] **Step 3.1: Replace `GetMarketingActionHandler.cs` contents**

Open `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs` and replace its entire contents with:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingAction
{
    public class GetMarketingActionHandler : IRequestHandler<GetMarketingActionRequest, GetMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;

        public GetMarketingActionHandler(IMarketingActionRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetMarketingActionResponse> Handle(
            GetMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var action = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (action == null)
            {
                return new GetMarketingActionResponse(ErrorCodes.MarketingActionNotFound, new Dictionary<string, string>
                {
                    { "actionId", request.Id.ToString() },
                });
            }

            return new GetMarketingActionResponse
            {
                Action = MarketingActionDto.FromEntity(action),
            };
        }
    }
}
```

Two specific changes versus the current file:
1. The `using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions;` directive (originally line 5) is **removed** — no other reference to that namespace exists in this file.
2. `GetMarketingActionsHandler.MapToDto(action)` (originally line 36) becomes `MarketingActionDto.FromEntity(action)`.

The 404 path (`ErrorCodes.MarketingActionNotFound`) is untouched, the response shape is unchanged, and the `_repository.GetByIdAsync` call is unchanged.

- [ ] **Step 3.2: Verify zero references to `GetMarketingActionsHandler` remain in this file**

Run from repo root:
```bash
grep -n "GetMarketingActionsHandler" backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs || echo "OK: no references"
```
Expected: `OK: no references`.

- [ ] **Step 3.3: Build**

Run from repo root:
```bash
dotnet build
```
Expected: **build succeeds**. (The old `GetMarketingActionsHandler.MapToDto` still exists and is still referenced from inside `GetMarketingActionsHandler` itself — that's removed in Task 4.)

- [ ] **Step 3.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs
git commit -m "refactor(marketing): GetMarketingActionHandler uses MarketingActionDto.FromEntity

Breaks the cross-handler dependency on GetMarketingActionsHandler.MapToDto
by switching the single-item handler to the new DTO factory. No behavior
or contract change."
```

---

## Task 4: Switch `GetMarketingActionsHandler` (list) to the new factory and delete `MapToDto`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs`

After this step, the old `internal static MapToDto` is gone and the list handler also goes through `MarketingActionDto.FromEntity`. The list handler's `using Anela.Heblo.Application.Features.Marketing.Contracts;` directive must **remain** because the call site `MarketingActionDto.FromEntity` is in that namespace.

- [ ] **Step 4.1: Replace `GetMarketingActionsHandler.cs` contents**

Open `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs` and replace its entire contents with:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Domain.Features.Marketing;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions
{
    public class GetMarketingActionsHandler : IRequestHandler<GetMarketingActionsRequest, GetMarketingActionsResponse>
    {
        private readonly IMarketingActionRepository _repository;

        public GetMarketingActionsHandler(IMarketingActionRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetMarketingActionsResponse> Handle(
            GetMarketingActionsRequest request,
            CancellationToken cancellationToken)
        {
            var criteria = new MarketingActionQueryCriteria
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                SearchTerm = request.SearchTerm,
                ActionType = request.ActionType,
                ProductCodePrefix = request.ProductCodePrefix,
                StartDateFrom = request.StartDateFrom,
                StartDateTo = request.StartDateTo,
                EndDateFrom = request.EndDateFrom,
                EndDateTo = request.EndDateTo,
                IncludeDeleted = request.IncludeDeleted,
            };

            var result = await _repository.GetPagedAsync(criteria, cancellationToken);

            return new GetMarketingActionsResponse
            {
                Actions = result.Items.Select(MarketingActionDto.FromEntity).ToList(),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize,
                TotalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize),
                HasNextPage = result.PageNumber * result.PageSize < result.TotalCount,
                HasPreviousPage = result.PageNumber > 1,
            };
        }
    }
}
```

Three specific changes versus the current file:
1. `result.Items.Select(MapToDto).ToList()` (originally line 42) becomes `result.Items.Select(MarketingActionDto.FromEntity).ToList()` — method group, per FR-3.
2. The `internal static MarketingActionDto MapToDto(MarketingAction action) => …` method (originally lines 52–80) is **deleted in its entirety**.
3. `using` directives unchanged: `System`, `System.Linq`, `System.Threading`, `System.Threading.Tasks`, `Anela.Heblo.Application.Features.Marketing.Contracts`, `Anela.Heblo.Domain.Features.Marketing`, `MediatR` all remain (the namespace is still needed for `MarketingActionDto`, `MarketingActionQueryCriteria`, and the criteria types).

- [ ] **Step 4.2: Verify zero references to the old `MapToDto` remain anywhere in the repo**

Run from repo root:
```bash
grep -RIn "GetMarketingActionsHandler\.MapToDto" backend/ && echo "FAIL: stale references found" || echo "OK: no stale references"
```
Expected: `OK: no stale references`.

Also verify the qualifier-free `MapToDto` is gone from this specific file:
```bash
grep -n "MapToDto" backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs || echo "OK: no MapToDto in handler"
```
Expected: `OK: no MapToDto in handler`.

- [ ] **Step 4.3: Build**

Run from repo root:
```bash
dotnet build
```
Expected: **build succeeds with no new warnings**.

- [ ] **Step 4.4: Run the existing handler test suite to confirm behaviour parity end-to-end**

The existing `GetMarketingActionsHandlerTests` exercises the full handler path including the projection. It is the primary mitigation for the HIGH-severity drift risk.

Run from repo root:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Marketing"
```
Expected: all Marketing tests pass — `MarketingActionDtoTests`, `GetMarketingActionsHandlerTests`, `CreateMarketingActionHandlerTests`, `DeleteMarketingActionHandlerTests`, `UpdateMarketingActionHandlerTests`, `OutlookCalendarSyncServiceTokenTests`.

- [ ] **Step 4.5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs
git commit -m "refactor(marketing): GetMarketingActionsHandler uses MarketingActionDto.FromEntity

Switches the list handler to the new DTO factory and deletes the now
unused internal static MapToDto. Wire contract is unchanged; existing
handler tests exercise the projection end-to-end."
```

---

## Task 5: Final validation gates (build, format, tests, frontend no-diff)

This task runs the full validation suite mandated by `CLAUDE.md` and spec NFR-2. Each step is a hard gate — if any fails, stop and fix before continuing.

- [ ] **Step 5.1: Verify the architectural acceptance criteria (FR-4)**

Run from repo root:
```bash
grep -RIn "GetMarketingActionsHandler\.MapToDto" backend/ && echo "FAIL" || echo "OK: FR-4 satisfied (no MapToDto qualified references)"
```
Expected: `OK: FR-4 satisfied (no MapToDto qualified references)`.

```bash
grep -RIn "GetMarketingActionsHandler\." backend/ | grep -v "GetMarketingActionsHandler\.cs" | grep -v "/test/"
```
Expected: **no output** (zero hits outside the handler file itself and tests). The test file only references the type for `IRequestHandler` generic parameters and constructor calls — never a member.

- [ ] **Step 5.2: `dotnet build` (clean, no new warnings)**

Run from repo root:
```bash
dotnet build
```
Expected: **build succeeds**. Compare the warning count against `main` if anything looks unfamiliar.

- [ ] **Step 5.3: `dotnet format --verify-no-changes`**

Run from repo root:
```bash
dotnet format --verify-no-changes
```
Expected: **exits 0 with no diff**. If it reports formatting drift in any of the three touched files, run `dotnet format` (without the flag) to apply fixes, then re-run with `--verify-no-changes` until clean. Amend or add a follow-up commit with the formatting fix.

- [ ] **Step 5.4: Full backend test suite**

Run from repo root:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: **all tests pass**. Any failure here is a behavioural drift signal — investigate before continuing.

- [ ] **Step 5.5: Frontend OpenAPI no-diff check**

The TypeScript client is auto-regenerated by `npm run build`. Spec NFR-2 mandates zero diff in `frontend/src/api-client/`.

Run from repo root:
```bash
cd frontend && npm run build && cd ..
```
Then:
```bash
git status frontend/src/api-client/
```
Expected: `nothing to commit, working tree clean` (or the equivalent — no modified files under `frontend/src/api-client/`). Static methods on DTOs are not emitted to the OpenAPI client; if there is a diff, the refactor has unexpectedly leaked into the contract and must be investigated before merge.

- [ ] **Step 5.6: Frontend lint (sanity check — should be a no-op)**

Run from repo root:
```bash
cd frontend && npm run lint && cd ..
```
Expected: **lint passes**. No frontend files were touched; this is a defensive check.

- [ ] **Step 5.7: If any formatting fix was needed in Step 5.3, commit it**

If `dotnet format` produced changes, commit them now:
```bash
git add backend/
git commit -m "chore: apply dotnet format"
```
Otherwise, skip this step — there is nothing to commit.

---

## Task 6: PR description notes (for human reviewer)

This task is documentation only. When opening the PR, the description must include the items below so the reviewer doesn't ask about them and so the follow-up arch-review candidates aren't lost.

- [ ] **Step 6.1: Draft the PR description**

The PR description should include these sections:

**Summary**
- Moves `MarketingAction → MarketingActionDto` projection out of `GetMarketingActionsHandler.MapToDto` to a new static factory `MarketingActionDto.FromEntity`.
- `GetMarketingActionHandler` (single) no longer depends on `GetMarketingActionsHandler` (list).
- Zero behaviour change. JSON responses on `GET /api/marketing-actions/{id}` and `GET /api/marketing-actions` are byte-identical. OpenAPI/TypeScript client regenerates with no diff.

**Design notes**
- Factory lives on the DTO itself (`FromEntity`), not on the Domain aggregate (`ToDto` would require Domain → Application/Contracts, which is the wrong direction). The brief approved this option; `JournalEntryMapper` (a separate mapper class) was considered and rejected as heavier than needed for a single DTO shape.
- Adds a `using Anela.Heblo.Domain.Features.Marketing;` to `MarketingActionDto.cs`. This direction (Application/Contracts → Domain inside the same assembly) is already established in sibling files in the same folder (`CreateMarketingActionRequest.cs`, `UpdateMarketingActionRequest.cs`, `GetMarketingActionsRequest.cs`, `MarketingFolderLinkRequest.cs`).
- DTO remains a `class` (not a `record`) per the project rule on OpenAPI generator compatibility.

**Future divergence note**
- Today single-item and list views share `MarketingActionDto`. If a future single-item view needs additional fields, the team must consciously choose between (a) extending `MarketingActionDto` (both endpoints expose the field) or (b) introducing a distinct `MarketingActionDetailDto` with its own `FromEntity`. The refactor makes this an explicit design decision rather than an accidental fork via `if/else` inside `MapToDto`.

**Follow-up arch-review candidates (NOT in this PR)**
- `CreateLotHandler.MapToDto` referenced from `UpdateLotHandler` / `GetLotHandler` / `ListLotsHandler` — same structural issue.
- `CreateEansHandler.MapToDto` referenced from `GetEanByCodeHandler` / `ListEansHandler` — same structural issue.

**Test plan**
- [x] `dotnet build` clean, no new warnings
- [x] `dotnet format --verify-no-changes` clean
- [x] `dotnet test` passes including new `MarketingActionDtoTests.FromEntity_ProjectsAllFields_ForFullyPopulatedAction`
- [x] `npm run build` produces no diff under `frontend/src/api-client/`
- [x] `grep -R "GetMarketingActionsHandler.MapToDto" backend/` returns zero matches
- [ ] Manual smoke (optional, staging): `GET /api/marketing-actions/{id}` and `GET /api/marketing-actions` return JSON identical to pre-refactor

- [ ] **Step 6.2: Open the PR via `gh`**

Push the branch and open the PR (substitute the actual branch name):
```bash
git push -u origin HEAD
gh pr create --title "refactor(marketing): extract MarketingAction→MarketingActionDto mapping to DTO factory" --body "$(cat <<'EOF'
## Summary
- Moves `MarketingAction → MarketingActionDto` projection out of `GetMarketingActionsHandler.MapToDto` into a static factory `MarketingActionDto.FromEntity` on the DTO itself.
- `GetMarketingActionHandler` (single-item) no longer depends on `GetMarketingActionsHandler` (list). Both consume the same factory.
- Zero behaviour change. Wire contract is byte-identical on both endpoints; OpenAPI client regenerates clean.

## Design notes
- Factory lives on the DTO (not on the Domain aggregate, which would require Domain → Application/Contracts).
- `using Anela.Heblo.Domain.Features.Marketing;` direction inside the same assembly is already established by sibling files in `Marketing/Contracts/`.
- DTO stays a `class` (not a `record`) per the OpenAPI generator compatibility rule.

## Future divergence
If the single-item view later needs more fields, this refactor forces the decision to be explicit: extend `MarketingActionDto` (both endpoints expose the field) or introduce `MarketingActionDetailDto` with its own `FromEntity`. No more accidental forks via `if/else` inside a shared `MapToDto`.

## Follow-up arch-review candidates (NOT in this PR)
- `CreateLotHandler.MapToDto` referenced from `UpdateLotHandler` / `GetLotHandler` / `ListLotsHandler`
- `CreateEansHandler.MapToDto` referenced from `GetEanByCodeHandler` / `ListEansHandler`

## Test plan
- [x] `dotnet build` clean, no new warnings
- [x] `dotnet format --verify-no-changes` clean
- [x] `dotnet test` passes including new `MarketingActionDtoTests`
- [x] `npm run build` produces no diff under `frontend/src/api-client/`
- [x] `grep -R "GetMarketingActionsHandler.MapToDto" backend/` returns zero matches
- [ ] Optional manual smoke on staging: `GET /api/marketing-actions/{id}` and `GET /api/marketing-actions` JSON identical to pre-refactor
EOF
)"
```
Expected: PR URL printed. Capture it for the agent log.

---

## Self-Review (executed)

**Spec coverage**
- FR-1 (add `FromEntity`) → Task 2.1
- FR-1 field-by-field acceptance criteria → covered by the parity test in Task 1.1 (every field of `MarketingActionDto` asserted, including `Distinct()` and enum-to-string projections)
- FR-2 (update single handler) → Task 3.1 (call site + `using` removal)
- FR-3 (update list handler, delete `MapToDto`) → Task 4.1
- FR-4 (zero `GetMarketingActionsHandler.MapToDto` references repo-wide) → Task 4.2 and Task 5.1
- NFR-1 (behaviour parity) → enforced by Task 1.1 parity test + Task 4.4 existing handler-tests run
- NFR-2 (build / format / test / OpenAPI no-diff) → Task 5 in full
- NFR-3 (architectural fit) → covered by Task 2.1 (DTO stays a class; Application/Contracts → Domain inside the same assembly is the established direction); documented in Task 6.1
- NFR-4 (test coverage) → exceeded — added a focused parity test rather than relying solely on the implicit handler-test coverage
- Arch-review amendment #1 (`using System.Linq;` in `MarketingActionDto.cs`) → included verbatim in Task 2.1
- Arch-review amendment #2 (`Distinct()` is defensive — keep it) → preserved in Task 2.1; parity test in Task 1.1 explicitly seeds a duplicate to lock this behaviour
- Out-of-scope sibling sites (`CreateLotHandler.MapToDto`, `CreateEansHandler.MapToDto`) → explicitly called out as follow-up in Task 6.1

**Placeholder scan**
- No `TBD` / `TODO` / `implement later`.
- No "similar to Task N" — every code block is repeated where needed.
- All commands include expected output.
- Every type / method / property referenced (`FromEntity`, `MarketingActionDto`, `MarketingActionFolderLinkDto`, `MarketingActionProduct`, `MarketingActionFolderLink`, `MarketingSyncStatus`, `MarketingFolderType`, `MarketingActionType`) exists in the codebase as verified before writing the plan.

**Type consistency**
- Method name is `FromEntity` everywhere (Task 1, 2, 3, 4, 5, 6) — no drift to `Map`, `ToDto`, or `Create`.
- DTO property names in the test (Task 1.1) match the DTO definition exactly: `Id`, `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `CreatedAt`, `ModifiedAt`, `CreatedByUserId`, `CreatedByUsername`, `ModifiedByUserId`, `ModifiedByUsername`, `AssociatedProducts`, `FolderLinks`, `OutlookSyncStatus`, `OutlookEventId`.
- `MarketingActionFolderLinkDto` is referenced in both Task 1.1 (assertions) and Task 2.1 (factory body) with the same field names: `FolderKey`, `FolderType`.
- Enum projections: `ActionType.ToString()`, `OutlookSyncStatus.ToString()`, `FolderType.ToString()` — consistent between Task 1.1 (asserted via `MarketingActionType.Blog.ToString()` etc.) and Task 2.1 (factory body).
