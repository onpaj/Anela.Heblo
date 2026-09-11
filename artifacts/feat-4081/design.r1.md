# Design: Deduplicate pagination metadata calculation in Journal handlers

## Component Design

### `JournalPaginationCalculator` (new)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs`
- **Visibility:** `internal static class` — an Application-layer implementation detail of the Journal module, not a public contract; both call sites are in the same assembly.
- **Responsibility:** Given the total number of matching rows and the requested page number/size, compute the three derived pagination fields that every Journal list response exposes. No I/O, no dependencies, pure function.
- **Public surface:**
  ```csharp
  internal static class JournalPaginationCalculator
  {
      public static (int TotalPages, bool HasNextPage, bool HasPreviousPage) Calculate(
          int totalCount, int pageNumber, int pageSize);
  }
  ```
- **Behavior contract (must match current inline behavior exactly — no behavior change):**
  - `TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)`
  - `HasNextPage = pageNumber * pageSize < totalCount`
  - `HasPreviousPage = pageNumber > 1`
  - No guarding against `pageSize <= 0` or negative `pageNumber` — today's inline code has no such guard either; adding one would be a behavior change outside this issue's scope.

### `GetJournalEntriesHandler` (modified)
- **Responsibility:** unchanged — fetch a page of journal entries via `IJournalRepository.GetEntriesAsync`, map to DTOs, build `GetJournalEntriesResponse`.
- **Change:** the three lines currently computing `TotalPages`/`HasNextPage`/`HasPreviousPage` inline are replaced by one call to `JournalPaginationCalculator.Calculate(result.TotalCount, request.PageNumber, request.PageSize)`, and the returned tuple values are assigned to the response object.

### `SearchJournalEntriesHandler` (modified)
- Same change as above, using `SearchJournalEntriesResponse` and the values already in scope from `result` / `request` in that handler's `Handle` method.

## Data Schemas

No schema changes. For reference, both existing response shapes (unchanged by this design):

```csharp
public class GetJournalEntriesResponse : BaseResponse
{
    public List<JournalEntryDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }       // now sourced from JournalPaginationCalculator.Calculate(...)
    public bool HasNextPage { get; set; }     // now sourced from JournalPaginationCalculator.Calculate(...)
    public bool HasPreviousPage { get; set; } // now sourced from JournalPaginationCalculator.Calculate(...)
}

public class SearchJournalEntriesResponse : BaseResponse
{
    public List<JournalEntryDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }       // now sourced from JournalPaginationCalculator.Calculate(...)
    public bool HasNextPage { get; set; }     // now sourced from JournalPaginationCalculator.Calculate(...)
    public bool HasPreviousPage { get; set; } // now sourced from JournalPaginationCalculator.Calculate(...)
}
```

No API request/response wire shape changes; the generated OpenAPI/TypeScript client is unaffected.
