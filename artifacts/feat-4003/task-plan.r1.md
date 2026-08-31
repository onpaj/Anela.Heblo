# Remove duplicate SearchJournalEntryDto Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the duplicate `SearchJournalEntryDto` type and its mirror mapper method, consolidating the Journal search endpoint on the existing `JournalEntryDto`, with zero behavior change to the API or UI.

**Architecture:** `SearchJournalEntryDto` and `JournalEntryMapper.ToSearchDto()` are pure duplicates of `JournalEntryDto` and `JournalEntryMapper.ToDto()`. Backend consolidation happens first (delete type, retype response, delete mapper method, repoint handler), then the generated OpenAPI TypeScript client is regenerated, then the five frontend files that reference `SearchJournalEntryDto` are repointed to `JournalEntryDto`.

**Tech Stack:** .NET 8 / C# (MediatR handler, xUnit tests), NSwag-generated TypeScript client, React/TypeScript frontend, Jest.

---

### task: remove-backend-duplicate-dto

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` (no code change expected — used to verify no regression)

- [ ] **Step 1: Run the existing test suite for this handler to capture the current passing baseline**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SearchJournalEntriesHandlerTests`
Expected: All tests in `SearchJournalEntriesHandlerTests.cs` PASS (this is a refactor — capture the green baseline before changing anything).

- [ ] **Step 2: Delete `SearchJournalEntryDto.cs`**

```bash
rm backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs
```

- [ ] **Step 3: Retype `SearchJournalEntriesResponse.Entries` to `List<JournalEntryDto>`**

In `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs`, change:

```csharp
public class SearchJournalEntriesResponse : BaseResponse
{
    public List<SearchJournalEntryDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
```

to:

```csharp
public class SearchJournalEntriesResponse : BaseResponse
{
    public List<JournalEntryDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
```

(No `using` change needed — `JournalEntryDto` is already in the same `Anela.Heblo.Application.Features.Journal.Contracts` namespace as this file.)

- [ ] **Step 4: Remove `JournalEntryMapper.ToSearchDto()`**

In `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs`, delete the entire `ToSearchDto` method (currently lines 38-66):

```csharp
        public static SearchJournalEntryDto ToSearchDto(JournalEntry entry)
        {
            return new SearchJournalEntryDto
            {
                Id = entry.Id,
                Title = entry.Title,
                Content = entry.Content,
                EntryDate = entry.EntryDate,
                CreatedAt = entry.CreatedAt,
                ModifiedAt = entry.ModifiedAt,
                CreatedByUserId = entry.CreatedByUserId,
                CreatedByUsername = entry.CreatedByUsername,
                ModifiedByUserId = entry.ModifiedByUserId,
                ModifiedByUsername = entry.ModifiedByUsername,
                AssociatedProducts = entry.ProductAssociations
                    .Select(pa => pa.ProductCodePrefix)
                    .Distinct()
                    .ToList(),
                Tags = entry.TagAssignments
                    .Where(ta => ta.Tag != null)
                    .Select(ta => new JournalEntryTagDto
                    {
                        Id = ta.Tag.Id,
                        Name = ta.Tag.Name,
                        Color = ta.Tag.Color
                    })
                    .ToList()
            };
        }
```

Leave `ToDto(JournalEntry entry)` (lines 8-36) exactly as-is — it is the sole surviving mapping method. After deletion, the file should contain only the `internal static class JournalEntryMapper` with the single `ToDto` method inside it.

- [ ] **Step 5: Repoint the handler to call `ToDto`**

In `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`, change:

```csharp
            var entryDtos = result.Items.Select(JournalEntryMapper.ToSearchDto).ToList();
```

to:

```csharp
            var entryDtos = result.Items.Select(JournalEntryMapper.ToDto).ToList();
```

- [ ] **Step 6: Build the backend to confirm no remaining references to the removed type/method**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeds with 0 errors. (If any file still references `SearchJournalEntryDto` or `ToSearchDto`, the build will fail here — search with `grep -rn "SearchJournalEntryDto\|ToSearchDto" backend/` and fix any remaining reference before proceeding.)

- [ ] **Step 7: Re-run the handler test suite to confirm no behavior change**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SearchJournalEntriesHandlerTests`
Expected: All tests PASS, same count as the Step 1 baseline — the DTO values are computed identically via `ToDto`, so no assertion should need to change.

- [ ] **Step 8: Run `dotnet format` to match project formatting conventions**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: Exits 0; no unexpected formatting diffs beyond whitespace in the files touched above.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs \
        backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs \
        backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs
git rm backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs
git commit -m "refactor(journal): remove duplicate SearchJournalEntryDto, consolidate on JournalEntryDto"
```

---

### task: regenerate-openapi-client

**Files:**
- Modify (generated, do not hand-edit): `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Regenerate the TypeScript client from the updated backend contracts**

Run (from repository root):
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```
Expected: Command exits 0 and `frontend/src/api/generated/api-client.ts` is rewritten.

- [ ] **Step 2: Confirm the duplicate type is gone from the generated client**

Run: `grep -n "SearchJournalEntryDto" frontend/src/api/generated/api-client.ts`
Expected: No output (no matches) — the generator no longer emits a `SearchJournalEntryDto` interface/class.

- [ ] **Step 3: Confirm the search response now types entries as `JournalEntryDto[]`**

Run: `grep -n "entries" frontend/src/api/generated/api-client.ts | grep -i journalentrydto`
Expected: At least one match showing `SearchJournalEntriesResponse`'s `entries` property (or its generated equivalent) typed with `JournalEntryDto`.

- [ ] **Step 4: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(journal): regenerate OpenAPI client after SearchJournalEntryDto removal"
```

(This will intentionally fail to build against the frontend files still referencing the old type until the next task updates them — that is expected at this point in the plan.)

---

### task: update-frontend-consumers

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx`
- Modify: `frontend/src/components/pages/CatalogDetail.tsx`
- Modify: `frontend/src/components/catalog/detail/CatalogDetailModals.tsx`
- Modify: `frontend/src/components/catalog/detail/tabs/JournalTab.tsx`
- Modify: `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx`

- [ ] **Step 1: Update `JournalList.tsx`**

Change the import (currently):
```typescript
import type {
  JournalEntryDto,
  SearchJournalEntryDto,
} from "../../../api/generated/api-client";
```
to:
```typescript
import type {
  JournalEntryDto,
} from "../../../api/generated/api-client";
```

Then change the cast (currently):
```typescript
                {isSearchMode
                  ? (entries as SearchJournalEntryDto[]).map((entry) => (
```
to:
```typescript
                {isSearchMode
                  ? (entries as JournalEntryDto[]).map((entry) => (
```

- [ ] **Step 2: Update `CatalogDetail.tsx`**

Change:
```typescript
import { SearchJournalEntryDto } from "../../api/generated/api-client";
```
to:
```typescript
import { JournalEntryDto } from "../../api/generated/api-client";
```

Change:
```typescript
  const [selectedJournalEntry, setSelectedJournalEntry] = useState<
    SearchJournalEntryDto | undefined
  >(undefined);
```
to:
```typescript
  const [selectedJournalEntry, setSelectedJournalEntry] = useState<
    JournalEntryDto | undefined
  >(undefined);
```

- [ ] **Step 3: Update `CatalogDetailModals.tsx`**

Change:
```typescript
import { SearchJournalEntryDto } from "../../../api/generated/api-client";
```
to:
```typescript
import { JournalEntryDto } from "../../../api/generated/api-client";
```

Change:
```typescript
  selectedJournalEntry?: SearchJournalEntryDto;
```
to:
```typescript
  selectedJournalEntry?: JournalEntryDto;
```

- [ ] **Step 4: Update `JournalTab.tsx`**

Change:
```typescript
import type { SearchJournalEntryDto } from "../../../../api/generated/api-client";
```
to:
```typescript
import type { JournalEntryDto } from "../../../../api/generated/api-client";
```

Change:
```typescript
  onEditEntry: (entry: SearchJournalEntryDto) => void;
```
to:
```typescript
  onEditEntry: (entry: JournalEntryDto) => void;
```

- [ ] **Step 5: Update `CatalogDetailTabs.tsx`**

Change:
```typescript
import { SearchJournalEntryDto } from "../../../api/generated/api-client";
```
to:
```typescript
import { JournalEntryDto } from "../../../api/generated/api-client";
```

Change:
```typescript
  journalEntries: SearchJournalEntryDto[];
```
to:
```typescript
  journalEntries: JournalEntryDto[];
```

Change:
```typescript
  onEditJournalEntry: (entry: SearchJournalEntryDto) => void;
```
to:
```typescript
  onEditJournalEntry: (entry: JournalEntryDto) => void;
```

- [ ] **Step 6: Confirm no remaining references anywhere in the frontend**

Run: `grep -rn "SearchJournalEntryDto" frontend/src`
Expected: No output (no matches).

- [ ] **Step 7: Build the frontend**

Run: `cd frontend && npm run build`
Expected: Build succeeds with 0 TypeScript errors.

- [ ] **Step 8: Lint the frontend**

Run: `cd frontend && npm run lint`
Expected: Exits 0, no new lint errors introduced.

- [ ] **Step 9: Run frontend tests touching Journal/CatalogDetail**

Run: `cd frontend && npx jest src/components/pages/Journal src/components/pages/CatalogDetail.tsx src/components/catalog/detail --watchAll=false`
Expected: All tests PASS — `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` already types its mock data as `JournalEntryDto[]`, so no test-file changes are anticipated, but this step verifies that.

- [ ] **Step 10: Commit**

```bash
git add frontend/src/components/pages/Journal/JournalList.tsx \
        frontend/src/components/pages/CatalogDetail.tsx \
        frontend/src/components/catalog/detail/CatalogDetailModals.tsx \
        frontend/src/components/catalog/detail/tabs/JournalTab.tsx \
        frontend/src/components/catalog/detail/CatalogDetailTabs.tsx
git commit -m "refactor(journal): repoint frontend consumers from SearchJournalEntryDto to JournalEntryDto"
```

---

### task: final-verification

**Files:** none (verification only — no new file changes)

- [ ] **Step 1: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors, 0 warnings introduced.

- [ ] **Step 2: Full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: All tests PASS (no regressions anywhere in the solution, not just the Journal module).

- [ ] **Step 3: Full frontend build**

Run: `cd frontend && npm run build`
Expected: 0 errors.

- [ ] **Step 4: Full frontend lint**

Run: `cd frontend && npm run lint`
Expected: 0 errors.

- [ ] **Step 5: Repository-wide grep confirms zero remaining references**

Run: `grep -rn "SearchJournalEntryDto\|ToSearchDto" backend frontend`
Expected: No output (no matches) anywhere in the repository.

- [ ] **Step 6: `dotnet format` verify (no diffs)**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: Exits 0.
