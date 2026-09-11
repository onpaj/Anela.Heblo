### task: add-pagination-calculator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalPaginationCalculatorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Journal/JournalPaginationCalculatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Journal.Pagination;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class JournalPaginationCalculatorTests
{
    [Theory]
    [InlineData(0, 1, 10, 0, false, false)]      // no rows at all
    [InlineData(5, 1, 10, 1, false, false)]       // single partial page
    [InlineData(10, 1, 10, 1, false, false)]      // exact multiple, on last page
    [InlineData(11, 1, 10, 2, true, false)]       // exact multiple + 1, more pages follow
    [InlineData(11, 2, 10, 2, false, true)]       // second (last) page of the above
    [InlineData(25, 2, 10, 3, true, true)]        // middle page
    [InlineData(25, 3, 10, 3, false, true)]       // last page, remainder row
    public void Calculate_ReturnsExpectedPaginationMetadata(
        int totalCount, int pageNumber, int pageSize,
        int expectedTotalPages, bool expectedHasNextPage, bool expectedHasPreviousPage)
    {
        // Act
        var result = JournalPaginationCalculator.Calculate(totalCount, pageNumber, pageSize);

        // Assert
        result.TotalPages.Should().Be(expectedTotalPages);
        result.HasNextPage.Should().Be(expectedHasNextPage);
        result.HasPreviousPage.Should().Be(expectedHasPreviousPage);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JournalPaginationCalculatorTests`
Expected: build FAILS (CS0234 or similar) — `Anela.Heblo.Application.Features.Journal.Pagination` namespace / `JournalPaginationCalculator` type does not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs`:

```csharp
using System;

namespace Anela.Heblo.Application.Features.Journal.Pagination
{
    /// <summary>
    /// Computes the pagination metadata (TotalPages, HasNextPage, HasPreviousPage) shared by
    /// GetJournalEntriesResponse and SearchJournalEntriesResponse. Extracted so the formula
    /// exists in exactly one place for both Journal list use cases.
    /// </summary>
    internal static class JournalPaginationCalculator
    {
        public static (int TotalPages, bool HasNextPage, bool HasPreviousPage) Calculate(
            int totalCount, int pageNumber, int pageSize)
        {
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            return (
                TotalPages: totalPages,
                HasNextPage: pageNumber * pageSize < totalCount,
                HasPreviousPage: pageNumber > 1);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JournalPaginationCalculatorTests`
Expected: PASS — all 7 theory cases green. (`internal` visibility resolves because `Anela.Heblo.Application/AssemblyInfo.cs` already declares `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]`.)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs backend/test/Anela.Heblo.Tests/Features/Journal/JournalPaginationCalculatorTests.cs
git commit -m "feat(journal): add shared pagination metadata calculator"
```

---
