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

