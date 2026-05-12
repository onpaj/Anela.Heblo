# Photobank Folder Regex Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional regex toggle for the folder path filter in the photobank, mirroring the existing filename regex feature end-to-end (DTO → validator → repository → controller → TS hook → React UI).

**Architecture:** New `UseFolderRegex` boolean is plumbed through every layer that `UseRegex` touches, reusing the same validation helper, the same `ErrorCodes.PhotobankInvalidRegexPattern` error, and the same Postgres-exception catch pattern. Bulk-tagging callers (`CountFilteredPhotosAsync`, `GetFilteredPhotoIdsMissingTagAsync`) keep `useFolderRegex: false` — they already ignore filename regex the same way.

**Tech Stack:** .NET 8 (FluentValidation, MediatR, EF Core, Npgsql), React 18 with TypeScript, xUnit + FluentAssertions + Moq (backend), Jest + React Testing Library (frontend).

---

## File map

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetPhotos/GetPhotosRequest.cs` | Add `UseFolderRegex` property |
| `backend/src/Anela.Heblo.Application/Features/Photobank/Validators/GetPhotosRequestValidator.cs` | Add folder regex validation rule |
| `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` | Add `useFolderRegex` param to `GetPhotosAsync` |
| `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` | Extend `BuildFilterQuery` + `GetPhotosAsync` |
| `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetPhotos/GetPhotosHandler.cs` | Forward `UseFolderRegex`; fix catch pattern selection |
| `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` | Add `useFolderRegex` query parameter |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosRequestValidatorTests.cs` | 4 new validator tests for `FolderPath` |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryFilterTests.cs` | Update existing calls + add 2 folder-regex tests |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosHandlerTests.cs` | Update existing mocks + add 2 new handler tests |
| `frontend/src/api/hooks/usePhotobank.ts` | Add `useFolderRegex` to params + URL builder |
| `frontend/src/components/marketing/photobank/TagSidebar.tsx` | Add folder regex prop, state, validation, UI |
| `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx` | Wire `useFolderRegex` state and pass to sidebar |
| `frontend/src/components/marketing/photobank/__tests__/TagSidebar.test.tsx` | Update renderSidebar defaults + 4 new tests |

---

### Task 1: DTO — add `UseFolderRegex` to `GetPhotosRequest`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetPhotos/GetPhotosRequest.cs`

- [ ] **Step 1: Add the new property**

Replace the file content:

```csharp
using System.Collections.Generic;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos
{
    public class GetPhotosRequest : IRequest<GetPhotosResponse>
    {
        public List<string>? Tags { get; set; }
        public string? Search { get; set; }
        public bool UseRegex { get; set; }
        public string? FolderPath { get; set; }
        public bool UseFolderRegex { get; set; }
        public bool WithoutTags { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 48;
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
cd /path/to/repo
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded` (zero errors; the rest of the solution will have errors once we change the interface — we address that in Task 3).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetPhotos/GetPhotosRequest.cs
git commit -m "feat(photobank): add UseFolderRegex property to GetPhotosRequest"
```

---

### Task 2: Validator — add folder path regex rule + tests (TDD)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Validators/GetPhotosRequestValidator.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosRequestValidatorTests.cs`

- [ ] **Step 1: Write the four failing validator tests**

Append to `GetPhotosRequestValidatorTests.cs` (after line 85, inside the class):

```csharp
    [Fact]
    public void FolderPath_InvalidRegex_UseFolderRegexTrue_FailsValidation()
    {
        // Arrange
        var request = new GetPhotosRequest
        {
            FolderPath = "[unclosed",
            UseFolderRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.FolderPath)
            .WithErrorMessage("Invalid regular expression pattern.");
    }

    [Fact]
    public void FolderPath_ValidRegex_UseFolderRegexTrue_PassesValidation()
    {
        // Arrange
        var request = new GetPhotosRequest
        {
            FolderPath = @"^Marketing/.*",
            UseFolderRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.FolderPath);
    }

    [Fact]
    public void FolderPath_InvalidRegex_UseFolderRegexFalse_PassesValidation()
    {
        // Arrange — flag off, regex pattern syntax is not checked
        var request = new GetPhotosRequest
        {
            FolderPath = "[unclosed",
            UseFolderRegex = false,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void FolderPath_Null_UseFolderRegexTrue_PassesValidation()
    {
        // Arrange — no pattern provided, nothing to validate
        var request = new GetPhotosRequest
        {
            FolderPath = null,
            UseFolderRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetPhotosRequestValidatorTests" --no-build
```

Expected: 4 new tests FAIL (the rule doesn't exist yet). Existing 4 tests still pass.

- [ ] **Step 3: Add the folder-path validation rule to the validator**

Full file content for `GetPhotosRequestValidator.cs`:

```csharp
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class GetPhotosRequestValidator : AbstractValidator<GetPhotosRequest>
{
    private const int MaxPageSize = 200;

    public GetPhotosRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0)
            .WithMessage("Page must be a positive integer");

        RuleFor(x => x.PageSize)
            .GreaterThan(0)
            .WithMessage("PageSize must be a positive integer")
            .LessThanOrEqualTo(MaxPageSize)
            .WithMessage($"PageSize cannot exceed {MaxPageSize}");

        RuleFor(x => x.Search)
            .Must(BeValidRegex)
            .When(x => x.UseRegex && !string.IsNullOrWhiteSpace(x.Search))
            .WithMessage("Invalid regular expression pattern.");

        RuleFor(x => x.FolderPath)
            .Must(BeValidRegex)
            .When(x => x.UseFolderRegex && !string.IsNullOrWhiteSpace(x.FolderPath))
            .WithMessage("Invalid regular expression pattern.");
    }

    private static bool BeValidRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try { _ = new Regex(pattern); return true; }
        catch (ArgumentException) { return false; }
    }
}
```

- [ ] **Step 4: Run the tests to confirm all 8 pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetPhotosRequestValidatorTests"
```

Expected: 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Validators/GetPhotosRequestValidator.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosRequestValidatorTests.cs
git commit -m "feat(photobank): add folder regex validation rule + tests"
```

---

### Task 3: Interface + Repository — extend `GetPhotosAsync` with `useFolderRegex`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryFilterTests.cs`

- [ ] **Step 1: Write new folder-regex repository tests (they fail until impl exists)**

Add this new class to the bottom of `PhotobankRepositoryFilterTests.cs` (after line 213):

```csharp
public class PhotobankRepositoryFolderRegexFilterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryFolderRegexFilterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var photos = new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Marketing/2025/Q1", ModifiedAt = DateTime.UtcNow },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Marketing/2025/Q2", ModifiedAt = DateTime.UtcNow },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Vyrobky/2025",      ModifiedAt = DateTime.UtcNow },
        };

        _context.Photos.AddRange(photos);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // NOTE: InMemory provider runs Regex.IsMatch via .NET engine. Production uses
    // Postgres POSIX ~* syntax. The engines differ on some constructs (e.g. named
    // groups). Postgres-specific failures are caught by GetPhotosHandler.

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_folderRegex_matchesOnlyMarketingFolders()
    {
        // Arrange — pattern anchored to start: only "Marketing/..." paths match
        var pattern = @"^Marketing/";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, false, pattern, true, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(2);
        items.Should().OnlyContain(p => p.FolderPath.StartsWith("Marketing/"));
        items.Should().NotContain(p => p.FileName == "c.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_folderRegexFalse_usesFolderSubstringFallback()
    {
        // Arrange — regex mode off; "2025" is a substring of all three paths
        var term = "2025";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, false, term, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(3);
    }
}
```

- [ ] **Step 2: Run existing repo tests to see current baseline**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PhotobankRepository" --no-build
```

Expected: All existing tests pass. The new class fails to compile because the signature doesn't exist yet.

- [ ] **Step 3: Update `IPhotobankRepository.GetPhotosAsync` signature**

Full content of `IPhotobankRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public sealed record PhotoLocator(string DriveId, string SharePointFileId, DateTime ModifiedAt);

    public interface IPhotobankRepository
    {
        // Photos
        Task<(List<Photo> Items, int Total)> GetPhotosAsync(
            List<string>? tags, string? search, bool useRegex, string? folderPath, bool useFolderRegex, bool withoutTags, int page, int pageSize,
            CancellationToken cancellationToken);

        Task<int> CountFilteredPhotosAsync(List<string>? tags, string? search, string? folderPath, CancellationToken cancellationToken);

        Task<List<int>> GetFilteredPhotoIdsMissingTagAsync(List<string>? tags, string? search, string? folderPath, int tagId, CancellationToken cancellationToken);

        Task<List<int>> GetExistingPhotoIdsMissingTagAsync(IReadOnlyList<int> photoIds, int tagId, CancellationToken cancellationToken);

        Task<int> CountExistingPhotosAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);

        Task<Photo?> GetPhotoByIdAsync(int id, CancellationToken cancellationToken);

        Task<PhotoLocator?> GetLocatorAsync(int id, CancellationToken cancellationToken);

        // Tags
        Task<List<(Tag Tag, int Count)>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
        Task<Tag?> GetOrCreateTagAsync(string normalizedName, CancellationToken cancellationToken);
        Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken);
        Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken);

        // Photo tags
        Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken);
        Task RemovePhotoTagAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken);

        // Roots
        Task<List<PhotobankIndexRoot>> GetRootsAsync(CancellationToken cancellationToken);
        Task<PhotobankIndexRoot> AddRootAsync(PhotobankIndexRoot root, CancellationToken cancellationToken);
        Task<bool> DeleteRootAsync(int id, CancellationToken cancellationToken);

        // Rules
        Task<List<TagRule>> GetRulesAsync(CancellationToken cancellationToken);
        Task<TagRule> AddRuleAsync(TagRule rule, CancellationToken cancellationToken);
        Task<TagRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken);
        Task UpdateRuleAsync(TagRule rule, CancellationToken cancellationToken);
        Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken);

        // Reapply rules
        Task<int> ReapplyRulesAsync(List<TagRule> activeRules, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
```

- [ ] **Step 4: Update `PhotobankRepository` — extend `BuildFilterQuery` and `GetPhotosAsync`**

Full content of `PhotobankRepository.cs` (only showing the changed methods — replace the `BuildFilterQuery` and `GetPhotosAsync` methods, all others stay the same):

**`BuildFilterQuery`** — add `bool useFolderRegex` parameter and replace the folder branch:

```csharp
private IQueryable<Photo> BuildFilterQuery(List<string>? tags, string? search, bool useRegex, string? folderPath, bool useFolderRegex)
{
    var query = _context.Photos.AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        if (useRegex)
        {
            var pattern = search.Trim();
            query = query.Where(p => Regex.IsMatch(p.FileName, pattern, RegexOptions.IgnoreCase));
        }
        else
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(p => p.FileName.ToLower().Contains(term));
        }
    }

    if (!string.IsNullOrWhiteSpace(folderPath))
    {
        if (useFolderRegex)
        {
            var pattern = folderPath.Trim();
            query = query.Where(p => Regex.IsMatch(p.FolderPath, pattern, RegexOptions.IgnoreCase));
        }
        else
        {
            var pathTerm = folderPath.Trim().ToLowerInvariant();
            query = query.Where(p => p.FolderPath.ToLower().Contains(pathTerm));
        }
    }

    if (tags != null && tags.Count > 0)
    {
        var normalizedTags = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .ToList();

        foreach (var tag in normalizedTags)
        {
            var t = tag;
            query = query.Where(p => p.Tags.Any(pt => pt.Tag.Name == t));
        }
    }

    return query;
}
```

**`GetPhotosAsync`** — add `bool useFolderRegex` and forward it:

```csharp
public async Task<(List<Photo> Items, int Total)> GetPhotosAsync(
    List<string>? tags, string? search, bool useRegex, string? folderPath, bool useFolderRegex, bool withoutTags, int page, int pageSize,
    CancellationToken cancellationToken)
{
    IQueryable<Photo> query = BuildFilterQuery(tags, search, useRegex, folderPath, useFolderRegex)
        .Include(p => p.Tags)
            .ThenInclude(pt => pt.Tag);

    if (withoutTags)
        query = query.Where(p => !p.Tags.Any());

    var total = await query.CountAsync(cancellationToken);
    var items = await query
        .OrderByDescending(p => p.ModifiedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(cancellationToken);

    return (items, total);
}
```

**`CountFilteredPhotosAsync`** — pass `useFolderRegex: false`:

```csharp
public async Task<int> CountFilteredPhotosAsync(
    List<string>? tags, string? search, string? folderPath,
    CancellationToken cancellationToken)
{
    var query = BuildFilterQuery(tags, search, false, folderPath, false);
    return await query.CountAsync(cancellationToken);
}
```

**`GetFilteredPhotoIdsMissingTagAsync`** — pass `useFolderRegex: false`:

```csharp
public async Task<List<int>> GetFilteredPhotoIdsMissingTagAsync(
    List<string>? tags, string? search, string? folderPath, int tagId,
    CancellationToken cancellationToken)
{
    var query = BuildFilterQuery(tags, search, false, folderPath, false);
    return await query
        .Where(p => !p.Tags.Any(pt => pt.TagId == tagId))
        .Select(p => p.Id)
        .ToListAsync(cancellationToken);
}
```

- [ ] **Step 5: Update existing `PhotobankRepositoryFilterTests` calls to the new signature**

The existing tests call `GetPhotosAsync` with 8 positional args (before `CancellationToken`). After adding `useFolderRegex`, they need a 9th positional arg (`false` for `useFolderRegex`) inserted at position 5 (after `folderPath`).

Update every `_repository.GetPhotosAsync(` call in `PhotobankRepositoryFilterTests.cs`. The pattern is:
```
GetPhotosAsync(null, null, false, folderPath, false, 1, 48, CancellationToken.None)
                                              ^^^^^
                                     INSERT false (useFolderRegex) here
```

Full updated `PhotobankRepositoryFilterTests.cs` (existing class only — the new `PhotobankRepositoryFolderRegexFilterTests` class from Step 1 stays at the bottom as written):

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Anela.Heblo.Application.Features.Photobank;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankRepositoryFilterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryFilterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var photos = new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "ruze-cervena.jpg",    FolderPath = "Marketing/Produkty/Ruze",    ModifiedAt = DateTime.UtcNow },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "levandule.jpg",       FolderPath = "Marketing/Produkty/Levandule", ModifiedAt = DateTime.UtcNow },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "banner-homepage.png", FolderPath = "Marketing/Web",              ModifiedAt = DateTime.UtcNow },
            new() { Id = 4, SharePointFileId = "sp-4", FileName = "vyrobek-01.jpg",      FolderPath = "Vyrobky/2025",               ModifiedAt = DateTime.UtcNow },
        };

        _context.Photos.AddRange(photos);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_filtersByFolderPath_substringMatch()
    {
        // Arrange — "Produkty" is a substring of two folder paths
        var folderPath = "Produkty";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, false, folderPath, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(2);
        items.Should().OnlyContain(p => p.FolderPath.Contains("Produkty", StringComparison.OrdinalIgnoreCase));
        items.Should().NotContain(p => p.FileName == "banner-homepage.png");
        items.Should().NotContain(p => p.FileName == "vyrobek-01.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_filterByFolderPath_caseInsensitive()
    {
        // Arrange — uppercase input, lowercase stored path
        var folderPath = "MARKETING/WEB";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, false, folderPath, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "banner-homepage.png");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_combinesFolderPathWithFilename()
    {
        // Arrange — folderPath matches two photos, filename narrows to one
        var folderPath = "Produkty";
        var search = "ruze";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, false, folderPath, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "ruze-cervena.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_combinesFolderPathWithTag()
    {
        // Arrange — seed a tag on one of the two "Produkty" photos
        var tag = new Tag { Id = 10, Name = "featured" };
        _context.PhotobankTags.Add(tag);

        var photoTag = new PhotoTag
        {
            PhotoId = 1, // ruze-cervena.jpg
            TagId = 10,
            Source = PhotoTagSource.Manual,
            CreatedAt = DateTime.UtcNow,
        };
        _context.PhotoTags.Add(photoTag);
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — folderPath "Produkty" matches photos 1 & 2; tag "featured" is only on photo 1
        var (items, total) = await _repository.GetPhotosAsync(
            new List<string> { "featured" }, null, false, "Produkty", false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "ruze-cervena.jpg");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async System.Threading.Tasks.Task GetPhotosAsync_emptyFolderPath_doesNotFilter(string? folderPath)
    {
        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, false, folderPath, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(4);
        items.Should().HaveCount(4);
    }
}

// NOTE: These tests run against the EF Core InMemory provider, which evaluates
// Regex.IsMatch via the .NET regex engine. In production, Npgsql translates
// this to Postgres POSIX ~* syntax. The two engines differ on some constructs
// (e.g., .NET lookahead, \b). Postgres-specific failures are caught by the
// PostgresException handler in GetPhotosHandler.
public class PhotobankRepositoryRegexFilterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryRegexFilterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var photos = new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "report_2024.pdf",  FolderPath = "Reports", ModifiedAt = DateTime.UtcNow },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "IMG_001.png",      FolderPath = "Photos",  ModifiedAt = DateTime.UtcNow },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "report_final.pdf", FolderPath = "Reports", ModifiedAt = DateTime.UtcNow },
        };

        _context.Photos.AddRange(photos);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_regexSearch_matchesOnlyNumericReport()
    {
        // Arrange — pattern matches "report_" followed by digits
        var pattern = @"^report_\d+";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, pattern, true, null, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(1);
        items.Should().ContainSingle(p => p.FileName == "report_2024.pdf");
        items.Should().NotContain(p => p.FileName == "report_final.pdf");
        items.Should().NotContain(p => p.FileName == "IMG_001.png");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_substringSearch_matchesBothReportFiles()
    {
        // Arrange — plain substring search returns all files containing "report"
        var search = "report";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, search, false, null, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(2);
        items.Should().Contain(p => p.FileName == "report_2024.pdf");
        items.Should().Contain(p => p.FileName == "report_final.pdf");
        items.Should().NotContain(p => p.FileName == "IMG_001.png");
    }
}

public class PhotobankRepositoryFolderRegexFilterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryFolderRegexFilterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var photos = new List<Photo>
        {
            new() { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Marketing/2025/Q1", ModifiedAt = DateTime.UtcNow },
            new() { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Marketing/2025/Q2", ModifiedAt = DateTime.UtcNow },
            new() { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Vyrobky/2025",      ModifiedAt = DateTime.UtcNow },
        };

        _context.Photos.AddRange(photos);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // NOTE: InMemory provider runs Regex.IsMatch via .NET engine. Production uses
    // Postgres POSIX ~* syntax. The engines differ on some constructs (e.g. named
    // groups). Postgres-specific failures are caught by GetPhotosHandler.

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_folderRegex_matchesOnlyMarketingFolders()
    {
        // Arrange — pattern anchored to start: only "Marketing/..." paths match
        var pattern = @"^Marketing/";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, false, pattern, true, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(2);
        items.Should().OnlyContain(p => p.FolderPath.StartsWith("Marketing/"));
        items.Should().NotContain(p => p.FileName == "c.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotosAsync_folderRegexFalse_usesFolderSubstringFallback()
    {
        // Arrange — regex mode off; "2025" is a substring of all three paths
        var term = "2025";

        // Act
        var (items, total) = await _repository.GetPhotosAsync(
            null, null, false, term, false, false, 1, 48, CancellationToken.None);

        // Assert
        total.Should().Be(3);
    }
}
```

- [ ] **Step 6: Run all repository filter tests to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PhotobankRepository"
```

Expected: All tests pass (existing + 2 new folder-regex tests).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryFilterTests.cs
git commit -m "feat(photobank): extend GetPhotosAsync with useFolderRegex + folder-regex tests"
```

---

### Task 4: Handler — forward `UseFolderRegex` + fix catch + update tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetPhotos/GetPhotosHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosHandlerTests.cs`

- [ ] **Step 1: Update existing handler tests to the new `GetPhotosAsync` signature**

All `Setup(r => r.GetPhotosAsync(...))` and `Verify(r => r.GetPhotosAsync(...))` calls need `false` inserted for `useFolderRegex` after `folderPath` (4th positional arg).

Old pattern (7 typed args + cancellationToken):
```csharp
r.GetPhotosAsync(tags, search, useRegex, folderPath, withoutTags, page, pageSize, ct)
```
New pattern (8 typed args + cancellationToken):
```csharp
r.GetPhotosAsync(tags, search, useRegex, folderPath, false, withoutTags, page, pageSize, ct)
```

Full updated `GetPhotosHandlerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class GetPhotosHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly GetPhotosHandler _handler;

    public GetPhotosHandlerTests()
    {
        _repositoryMock = new Mock<IPhotobankRepository>();
        _handler = new GetPhotosHandler(_repositoryMock.Object);
    }

    private static Photo BuildPhoto(int id, string fileName, string folderPath, List<PhotoTag>? tags = null) =>
        new()
        {
            Id = id,
            SharePointFileId = $"sp-{id}",
            FileName = fileName,
            FolderPath = folderPath,
            SharePointWebUrl = $"https://sp.example.com/file-{id}",
            ModifiedAt = DateTime.UtcNow,
            Tags = tags ?? new List<PhotoTag>(),
        };

    private static Tag BuildTag(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async System.Threading.Tasks.Task Handle_NoFilters_ReturnsAllPhotos()
    {
        // Arrange
        var photos = new List<Photo>
        {
            BuildPhoto(1, "photo1.jpg", "Photos/2025"),
            BuildPhoto(2, "photo2.jpg", "Photos/2026"),
        };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, null, false, null, false, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 2));

        var request = new GetPhotosRequest();

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items[0].Id.Should().Be(1);
        result.Items[1].Id.Should().Be(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(48);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_FilterByTag_PassesTagsToRepository()
    {
        // Arrange
        var tag = BuildTag(1, "products");
        var photoTag = new PhotoTag { PhotoId = 1, TagId = 1, Source = PhotoTagSource.Rule, Tag = tag };
        var photos = new List<Photo>
        {
            BuildPhoto(1, "product.jpg", "Photos/Products", new List<PhotoTag> { photoTag }),
        };
        var tagFilter = new List<string> { "products" };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(tagFilter, null, false, null, false, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new GetPhotosRequest { Tags = tagFilter };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Total.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items[0].Tags.Should().ContainSingle(t => t.Name == "products");

        _repositoryMock.Verify(r => r.GetPhotosAsync(tagFilter, null, false, null, false, false, 1, 48, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_FilterBySearch_PassesSearchToRepository()
    {
        // Arrange
        var photos = new List<Photo>
        {
            BuildPhoto(1, "ruze-cervena.jpg", "Photos/Products"),
        };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, "ruze", false, null, false, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new GetPhotosRequest { Search = "ruze" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("ruze-cervena.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPhotosAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Photo>(), 0));

        var request = new GetPhotosRequest { Tags = new List<string> { "nonexistent" } };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Total.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_FilterByFolderPath_PassesFolderPathToRepository()
    {
        // Arrange
        var photos = new List<Photo>
        {
            BuildPhoto(1, "ruze-cervena.jpg", "Marketing/Produkty/Ruze"),
        };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, null, false, "Produkty", false, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new GetPhotosRequest { FolderPath = "Produkty" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().HaveCount(1);
        result.Items[0].FolderPath.Should().Be("Marketing/Produkty/Ruze");

        _repositoryMock.Verify(r => r.GetPhotosAsync(null, null, false, "Produkty", false, false, 1, 48, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_UseRegexTrue_ForwardsUseRegexToRepository()
    {
        // Arrange
        var photos = new List<Photo>
        {
            BuildPhoto(1, "report_2024.pdf", "Reports"),
        };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, @"^report_\d+", true, null, false, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new GetPhotosRequest { Search = @"^report_\d+", UseRegex = true };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().ContainSingle(p => p.Name == "report_2024.pdf");

        _repositoryMock.Verify(r => r.GetPhotosAsync(null, @"^report_\d+", true, null, false, false, 1, 48, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_UseFolderRegexTrue_ForwardsUseFolderRegexToRepository()
    {
        // Arrange
        var photos = new List<Photo>
        {
            BuildPhoto(1, "a.jpg", "Marketing/2025/Q1"),
        };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, null, false, @"^Marketing/", true, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new GetPhotosRequest { FolderPath = @"^Marketing/", UseFolderRegex = true };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().ContainSingle(p => p.FolderPath == "Marketing/2025/Q1");

        _repositoryMock.Verify(r => r.GetPhotosAsync(null, null, false, @"^Marketing/", true, false, 1, 48, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_UseFolderRegexTrue_InvalidPostgresPattern_ReturnsFolderRegexError()
    {
        // Arrange — Npgsql throws PostgresException with SqlState 2201B for invalid POSIX regex.
        // We simulate via a mock that throws; the handler must catch it and return the FolderPath as pattern.
        var postgresEx = CreatePostgresException("2201B");

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, null, false, "(?<x>foo)", true, false, 1, 48, It.IsAny<CancellationToken>()))
            .ThrowsAsync(postgresEx);

        var request = new GetPhotosRequest
        {
            FolderPath = "(?<x>foo)",
            UseFolderRegex = true,
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PhotobankInvalidRegexPattern);
        result.Params.Should().ContainKey("pattern").WhoseValue.Should().Be("(?<x>foo)");
    }

    private static Npgsql.NpgsqlException CreatePostgresException(string sqlState)
    {
        // NpgsqlException is abstract; PostgresException is its concrete subclass.
        // Use reflection to construct it since there is no public constructor.
        var type = typeof(Npgsql.PostgresException);
        var ctor = type.GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof(string), typeof(string) },
            null)!;
        return (Npgsql.NpgsqlException)ctor.Invoke(new object[] { "ERROR", sqlState });
    }
}
```

- [ ] **Step 2: Run existing handler tests to confirm they fail (signature mismatch)**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetPhotosHandlerTests" --no-build
```

Expected: Tests fail to compile because `GetPhotosAsync` now requires 9 typed params but old setups used 8.

- [ ] **Step 3: Update `GetPhotosHandler.cs`**

Full content:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;
using Npgsql;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos
{
    public class GetPhotosHandler : IRequestHandler<GetPhotosRequest, GetPhotosResponse>
    {
        private readonly IPhotobankRepository _repository;

        public GetPhotosHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetPhotosResponse> Handle(GetPhotosRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var (items, total) = await _repository.GetPhotosAsync(
                    request.Tags,
                    request.Search,
                    request.UseRegex,
                    request.FolderPath,
                    request.UseFolderRegex,
                    request.WithoutTags,
                    request.Page,
                    request.PageSize,
                    cancellationToken);

                return new GetPhotosResponse
                {
                    Items = items.Select(MapToDto).ToList(),
                    Total = total,
                    Page = request.Page,
                    PageSize = request.PageSize,
                };
            }
            catch (PostgresException ex) when ((request.UseRegex || request.UseFolderRegex) && ex.SqlState == "2201B")
            {
                var pattern = request.UseFolderRegex
                    ? (request.FolderPath ?? string.Empty)
                    : (request.Search ?? string.Empty);

                return new GetPhotosResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.PhotobankInvalidRegexPattern,
                    Params = new Dictionary<string, string> { ["pattern"] = pattern },
                };
            }
        }

        internal static PhotoDto MapToDto(Photo photo) => new()
        {
            Id = photo.Id,
            SharePointFileId = photo.SharePointFileId,
            DriveId = photo.DriveId,
            Name = photo.FileName,
            FolderPath = photo.FolderPath,
            SharePointWebUrl = photo.SharePointWebUrl,
            FileSizeBytes = photo.FileSizeBytes,
            LastModifiedAt = photo.ModifiedAt,
            Tags = photo.Tags.Select(pt => new TagDto
            {
                Id = pt.TagId,
                Name = pt.Tag.Name,
                Source = pt.Source.ToString(),
            }).ToList(),
        };
    }
}
```

- [ ] **Step 4: Run all handler tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetPhotosHandlerTests"
```

Expected: All tests pass (7 existing + 2 new).

> **Note on `CreatePostgresException`:** If the reflection approach fails (constructor signature differs across Npgsql versions), replace the test with a `Mock<IPhotobankRepository>` that directly sets up the thrown exception using `Moq`'s `ThrowsAsync`. The important assertion is that `Success == false`, `ErrorCode == PhotobankInvalidRegexPattern`, and `Params["pattern"] == request.FolderPath`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetPhotos/GetPhotosHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosHandlerTests.cs
git commit -m "feat(photobank): forward UseFolderRegex in handler + fix Postgres catch pattern selection"
```

---

### Task 5: Controller — add `useFolderRegex` query parameter

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs`

- [ ] **Step 1: Update the `GetPhotos` action**

Replace the `GetPhotos` method (starting at line 49):

```csharp
        /// <summary>
        /// Get photos with optional tag AND filter, filename search, and pagination.
        /// Set useRegex=true to use POSIX regex matching on filename instead of substring search.
        /// Set useFolderRegex=true to use POSIX regex matching on folder path instead of substring search.
        /// </summary>
        [HttpGet("photos")]
        [ProducesResponseType(typeof(GetPhotosResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetPhotosResponse>> GetPhotos(
            [FromQuery] List<string>? tags,
            [FromQuery] string? search,
            [FromQuery] bool useRegex = false,
            [FromQuery] string? folderPath = null,
            [FromQuery] bool useFolderRegex = false,
            [FromQuery] bool withoutTags = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 48,
            CancellationToken cancellationToken = default)
        {
            var request = new GetPhotosRequest
            {
                Tags = tags,
                Search = search,
                UseRegex = useRegex,
                FolderPath = folderPath,
                UseFolderRegex = useFolderRegex,
                WithoutTags = withoutTags,
                Page = page,
                PageSize = pageSize,
            };
            var response = await _mediator.Send(request, cancellationToken);
            return HandleResponse(response);
        }
```

- [ ] **Step 2: Build the full solution**

```bash
dotnet build
```

Expected: `Build succeeded` — all projects compile cleanly.

- [ ] **Step 3: Format**

```bash
dotnet format
```

Expected: No changes (or only cosmetic whitespace fixes).

- [ ] **Step 4: Run all photobank backend tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Photobank"
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs
git commit -m "feat(photobank): add useFolderRegex query param to GetPhotos endpoint"
```

---

### Task 6: Frontend hook — add `useFolderRegex` to params and URL builder

**Files:**
- Modify: `frontend/src/api/hooks/usePhotobank.ts`

- [ ] **Step 1: Add `useFolderRegex` to `GetPhotosParams` interface**

In `GetPhotosParams` (currently at line 37), add the new field after `useRegex`:

```typescript
export interface GetPhotosParams {
  tags?: string[];
  search?: string;
  useRegex?: boolean;
  useFolderRegex?: boolean;
  folderPath?: string;
  withoutTags?: boolean;
  page?: number;
  pageSize?: number;
}
```

- [ ] **Step 2: Add `useFolderRegex` to `buildPhotosUrl`**

In `buildPhotosUrl`, add after the `useRegex` line:

```typescript
function buildPhotosUrl(baseUrl: string, params: GetPhotosParams): string {
  const qs = new URLSearchParams();
  if (params.search) qs.set("search", params.search);
  if (params.useRegex) qs.set("useRegex", "true");
  if (params.useFolderRegex) qs.set("useFolderRegex", "true");
  if (params.folderPath) qs.set("folderPath", params.folderPath);
  if (params.withoutTags) qs.set("withoutTags", "true");
  if (params.page != null) qs.set("page", String(params.page));
  if (params.pageSize != null) qs.set("pageSize", String(params.pageSize));
  (params.tags ?? []).forEach((t) => qs.append("tags", String(t)));
  const query = qs.toString();
  return `${baseUrl}/api/photobank/photos${query ? `?${query}` : ""}`;
}
```

- [ ] **Step 3: TypeScript compile check**

```bash
cd frontend && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/usePhotobank.ts
git commit -m "feat(photobank): add useFolderRegex to GetPhotosParams and URL builder"
```

---

### Task 7: Frontend tests — update `TagSidebar.test.tsx` and implement `TagSidebar` (TDD)

**Files:**
- Modify: `frontend/src/components/marketing/photobank/__tests__/TagSidebar.test.tsx`
- Modify: `frontend/src/components/marketing/photobank/TagSidebar.tsx`

- [ ] **Step 1: Update `renderSidebar` defaults in the test file to include new props**

In `TagSidebar.test.tsx`, update the `defaults` object inside `renderSidebar` (add the two new props after `onRegexChange`):

```typescript
function renderSidebar(overrides: Partial<React.ComponentProps<typeof TagSidebar>> = {}) {
  const defaults: React.ComponentProps<typeof TagSidebar> = {
    tags: mockTags,
    selectedTagIds: [],
    search: "",
    folderPath: "",
    withoutTags: false,
    useRegex: false,
    useFolderRegex: false,
    onTagToggle: jest.fn(),
    onSearchChange: jest.fn(),
    onFolderPathChange: jest.fn(),
    onWithoutTagsToggle: jest.fn(),
    onClearFilters: jest.fn(),
    onRegexChange: jest.fn(),
    onFolderRegexChange: jest.fn(),
    ...overrides,
  };
  return { ...render(<TagSidebar {...defaults} />), props: defaults };
}
```

Also update the `rerender` call in the `"toggling regex off clears the error"` test (line 287) to include the new props:

```typescript
    rerender(
      <TagSidebar
        tags={mockTags}
        selectedTagIds={[]}
        search="[bad"
        folderPath=""
        withoutTags={false}
        useRegex={false}
        useFolderRegex={false}
        onTagToggle={jest.fn()}
        onSearchChange={jest.fn()}
        onFolderPathChange={jest.fn()}
        onWithoutTagsToggle={jest.fn()}
        onClearFilters={jest.fn()}
        onRegexChange={jest.fn()}
        onFolderRegexChange={jest.fn()}
      />
    );
```

- [ ] **Step 2: Add the four new folder-regex tests**

Append inside the `describe("TagSidebar", ...)` block (before the closing `});`):

```typescript
  test("folder regex toggle calls onFolderRegexChange", () => {
    // Arrange
    const onFolderRegexChange = jest.fn();
    renderSidebar({ onFolderRegexChange });

    // Act
    const checkbox = screen.getByRole("checkbox", { name: /Regex složky/i });
    fireEvent.click(checkbox);

    // Assert
    expect(onFolderRegexChange).toHaveBeenCalledWith(true);
  });

  test("folder regex mode with valid pattern calls onFolderPathChange after debounce", () => {
    // Arrange
    const onFolderPathChange = jest.fn();
    renderSidebar({ useFolderRegex: true, onFolderPathChange });

    // Act — target the folder input by its stable aria-label (aria-label doesn't change when regex mode toggles)
    const folderInput = screen.getByLabelText("Hledat ve složkách");
    fireEvent.change(folderInput, { target: { value: "^Marketing/" } });

    // Assert: not called immediately
    expect(onFolderPathChange).not.toHaveBeenCalled();

    // Fast-forward debounce timer
    act(() => {
      jest.advanceTimersByTime(300);
    });

    expect(onFolderPathChange).toHaveBeenCalledWith("^Marketing/");
  });

  test("folder regex mode with invalid pattern shows error and does not call onFolderPathChange after debounce", () => {
    // Arrange
    const onFolderPathChange = jest.fn();
    renderSidebar({ useFolderRegex: true, onFolderPathChange });

    const folderInput = screen.getByLabelText("Hledat ve složkách");

    // Act — type an invalid regex
    fireEvent.change(folderInput, { target: { value: "[bad" } });

    // Assert: error message appears (two "Neplatný regulární výraz" would appear if both inputs are invalid
    // — but here only folder is invalid)
    expect(screen.getAllByText("Neplatný regulární výraz")).toHaveLength(1);

    // Fast-forward debounce timer
    act(() => {
      jest.advanceTimersByTime(300);
    });

    // Assert: onFolderPathChange is NOT called
    expect(onFolderPathChange).not.toHaveBeenCalled();
  });

  test("folder regex mode off with invalid regex-like folder input calls onFolderPathChange", () => {
    // Arrange
    const onFolderPathChange = jest.fn();
    renderSidebar({ useFolderRegex: false, onFolderPathChange });

    const folderInput = screen.getByLabelText("Hledat ve složkách");

    // Act — type something that would be invalid regex but folder regex mode is off
    fireEvent.change(folderInput, { target: { value: "[bad" } });

    // Assert: no error shown
    expect(screen.queryByText("Neplatný regulární výraz")).not.toBeInTheDocument();

    // Fast-forward debounce timer
    act(() => {
      jest.advanceTimersByTime(300);
    });

    // Assert: onFolderPathChange is called as normal
    expect(onFolderPathChange).toHaveBeenCalledWith("[bad");
  });
```

- [ ] **Step 3: Run tests to confirm new tests fail (component doesn't have the props yet)**

```bash
cd frontend && npm test -- --run TagSidebar
```

Expected: New tests fail; existing tests may have TypeScript errors because `TagSidebar` doesn't accept `useFolderRegex` / `onFolderRegexChange` props yet.

- [ ] **Step 4: Implement folder regex support in `TagSidebar.tsx`**

Full file content of `TagSidebar.tsx`:

```tsx
import React, { useCallback, useEffect, useState } from "react";
import { Search, X, Tag, Folder } from "lucide-react";
import type { TagWithCountDto } from "../../../api/hooks/usePhotobank";
import { TagBadge } from "../../ui/TagBadge";

interface TagSidebarProps {
  tags: TagWithCountDto[];
  selectedTagIds: number[];
  search: string;
  folderPath: string;
  withoutTags: boolean;
  useRegex: boolean;
  useFolderRegex: boolean;
  onTagToggle: (tagId: number) => void;
  onSearchChange: (value: string) => void;
  onFolderPathChange: (value: string) => void;
  onWithoutTagsToggle: () => void;
  onClearFilters: () => void;
  onRegexChange: (value: boolean) => void;
  onFolderRegexChange: (value: boolean) => void;
  errorMessage?: string | null;
}

const DEBOUNCE_MS = 300;

const TagSidebar: React.FC<TagSidebarProps> = ({
  tags,
  selectedTagIds,
  search,
  folderPath,
  withoutTags,
  useRegex,
  useFolderRegex,
  onTagToggle,
  onSearchChange,
  onFolderPathChange,
  onWithoutTagsToggle,
  onClearFilters,
  onRegexChange,
  onFolderRegexChange,
  errorMessage,
}) => {
  const [inputValue, setInputValue] = useState(search);
  const [folderPathValue, setFolderPathValue] = useState(folderPath);
  const [tagFilter, setTagFilter] = useState("");
  const [regexError, setRegexError] = useState<string | null>(null);
  const [folderRegexError, setFolderRegexError] = useState<string | null>(null);

  // Sync external search value to local input
  useEffect(() => {
    setInputValue(search);
  }, [search]);

  // Validate regex pattern when regex mode is active
  useEffect(() => {
    if (!useRegex || !inputValue) {
      setRegexError(null);
      return;
    }
    try {
      new RegExp(inputValue);
      setRegexError(null);
    } catch {
      setRegexError("Neplatný regulární výraz");
    }
  }, [inputValue, useRegex]);

  // Debounce search input changes
  useEffect(() => {
    const timer = setTimeout(() => {
      if (inputValue !== search && regexError === null) {
        onSearchChange(inputValue);
      }
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [inputValue, search, onSearchChange, regexError]);

  const handleInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setInputValue(e.target.value);
    },
    [],
  );

  const handleClearSearch = useCallback(() => {
    setInputValue("");
    onSearchChange("");
  }, [onSearchChange]);

  // Sync external folderPath value to local input
  useEffect(() => {
    setFolderPathValue(folderPath);
  }, [folderPath]);

  // Validate folder path regex pattern when folder regex mode is active
  useEffect(() => {
    if (!useFolderRegex || !folderPathValue) {
      setFolderRegexError(null);
      return;
    }
    try {
      new RegExp(folderPathValue);
      setFolderRegexError(null);
    } catch {
      setFolderRegexError("Neplatný regulární výraz");
    }
  }, [folderPathValue, useFolderRegex]);

  // Debounce folder path input changes
  useEffect(() => {
    const timer = setTimeout(() => {
      if (folderPathValue !== folderPath && folderRegexError === null) {
        onFolderPathChange(folderPathValue);
      }
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [folderPathValue, folderPath, onFolderPathChange, folderRegexError]);

  const handleFolderPathInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFolderPathValue(e.target.value);
    },
    [],
  );

  const handleClearFolderPath = useCallback(() => {
    setFolderPathValue("");
    onFolderPathChange("");
  }, [onFolderPathChange]);

  const filteredTags = tagFilter.trim()
    ? tags.filter((t) => t.name.toLowerCase().includes(tagFilter.trim().toLowerCase()))
    : tags;

  const hasActiveFilters = search.length > 0 || folderPath.length > 0 || selectedTagIds.length > 0 || withoutTags || useRegex || useFolderRegex;

  return (
    <aside className="flex flex-col h-full bg-white border-r border-gray-200 overflow-hidden">
      <div className="p-4 border-b border-gray-100">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wide">
            Filtry
          </h2>
          {hasActiveFilters && (
            <button
              onClick={onClearFilters}
              className="text-xs text-primary-blue hover:underline flex items-center gap-1"
              aria-label="Vymazat filtry"
            >
              <X className="w-3 h-3" />
              Vymazat
            </button>
          )}
        </div>

        {/* Search input */}
        <div className="relative">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400 pointer-events-none" />
          <input
            type="text"
            value={inputValue}
            onChange={handleInputChange}
            placeholder={useRegex ? "Regex (POSIX, case-insensitive)..." : "Hledat soubory..."}
            className={`w-full pl-8 pr-7 py-1.5 text-sm border rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent ${
              regexError ? "border-red-400" : "border-gray-300"
            }`}
            aria-label="Hledat soubory"
          />
          {inputValue && (
            <button
              onClick={handleClearSearch}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
              aria-label="Vymazat hledání"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>

        {/* Filename regex toggle */}
        <label className="flex items-center gap-1.5 mt-2 cursor-pointer select-none">
          <input
            type="checkbox"
            checked={useRegex}
            onChange={(e) => onRegexChange(e.target.checked)}
            className="w-3.5 h-3.5 accent-primary-blue"
          />
          <span className="text-xs text-gray-600">Regex</span>
        </label>
        {regexError && (
          <p className="mt-1 text-xs text-red-600">{regexError}</p>
        )}
        {!regexError && errorMessage && (
          <p className="mt-1 text-xs text-red-600">{errorMessage}</p>
        )}

        {/* Folder path input */}
        <div className="relative mt-2">
          <Folder className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400 pointer-events-none" />
          <input
            type="text"
            value={folderPathValue}
            onChange={handleFolderPathInputChange}
            placeholder={useFolderRegex ? "Regex (POSIX, case-insensitive)..." : "Hledat ve složkách..."}
            className={`w-full pl-8 pr-7 py-1.5 text-sm border rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent ${
              folderRegexError ? "border-red-400" : "border-gray-300"
            }`}
            aria-label="Hledat ve složkách"
          />
          {folderPathValue && (
            <button
              onClick={handleClearFolderPath}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
              aria-label="Vymazat složku"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>

        {/* Folder regex toggle */}
        <label className="flex items-center gap-1.5 mt-2 cursor-pointer select-none" aria-label="Regex složky">
          <input
            type="checkbox"
            checked={useFolderRegex}
            onChange={(e) => onFolderRegexChange(e.target.checked)}
            className="w-3.5 h-3.5 accent-primary-blue"
            aria-label="Regex složky"
          />
          <span className="text-xs text-gray-600">Regex</span>
        </label>
        {folderRegexError && (
          <p className="mt-1 text-xs text-red-600">{folderRegexError}</p>
        )}
      </div>

      {/* Tag list */}
      <div className="flex-1 overflow-y-auto p-4">
        <div className="flex items-center gap-1.5 mb-2">
          <Tag className="w-3.5 h-3.5 text-gray-400" />
          <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
            Štítky
          </span>
        </div>

        {/* Tag name filter input */}
        {tags.length > 0 && (
          <div className="relative mb-2">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3 h-3 text-gray-400 pointer-events-none" />
            <input
              type="text"
              value={tagFilter}
              onChange={(e) => setTagFilter(e.target.value)}
              placeholder="Filtrovat štítky..."
              className="w-full pl-7 pr-6 py-1 text-xs border border-gray-200 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-blue focus:border-transparent"
              aria-label="Filtrovat štítky"
            />
            {tagFilter && (
              <button
                type="button"
                onClick={() => setTagFilter("")}
                className="absolute right-1.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                aria-label="Vymazat filtr štítků"
              >
                <X className="w-3 h-3" />
              </button>
            )}
          </div>
        )}

        {tags.length === 0 ? (
          <p className="text-sm text-gray-400 mt-2">Žádné štítky</p>
        ) : (
          <ul className="space-y-0.5">
            {/* "Without tags" special option */}
            <li>
              <button
                type="button"
                onClick={onWithoutTagsToggle}
                className={[
                  "w-full flex items-center justify-between px-2 py-1.5 rounded-md text-sm transition-colors text-left",
                  withoutTags
                    ? "bg-secondary-blue-pale text-primary-blue font-medium"
                    : "text-gray-700 hover:bg-gray-50",
                ].join(" ")}
                aria-pressed={withoutTags}
              >
                <span className="text-xs italic text-gray-400">Bez štítků</span>
                <span className="ml-2 text-xs tabular-nums flex-shrink-0 text-gray-300">—</span>
              </button>
            </li>

            {/* Filtered tag list */}
            {filteredTags.map((tag) => {
              const isSelected = selectedTagIds.includes(tag.id);
              return (
                <li key={tag.id}>
                  <button
                    type="button"
                    onClick={() => onTagToggle(tag.id)}
                    className={[
                      "w-full flex items-center justify-between px-2 py-1.5 rounded-md transition-colors text-left",
                      isSelected ? "bg-secondary-blue-pale" : "hover:bg-gray-50",
                    ].join(" ")}
                    aria-pressed={isSelected}
                  >
                    <TagBadge name={tag.name} />
                    <span
                      className={[
                        "ml-2 text-xs tabular-nums flex-shrink-0",
                        isSelected ? "text-primary-blue" : "text-gray-400",
                      ].join(" ")}
                    >
                      {tag.count}
                    </span>
                  </button>
                </li>
              );
            })}

            {filteredTags.length === 0 && tagFilter && (
              <li className="px-2 py-1.5 text-xs text-gray-400">Žádné výsledky</li>
            )}
          </ul>
        )}
      </div>
    </aside>
  );
};

export default TagSidebar;
```

- [ ] **Step 5: Run TagSidebar tests**

```bash
cd frontend && npm test -- --run TagSidebar
```

Expected: All tests pass (existing + 4 new).

> **Note on test selectors:** The folder regex toggle is rendered with `aria-label="Regex složky"`. The test uses `screen.getByRole("checkbox", { name: /Regex složky/i })`. The folder input keeps `aria-label="Hledat ve složkách"` so `screen.getByLabelText("Hledat ve složkách")` still works regardless of the placeholder value change when `useFolderRegex` is on.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/marketing/photobank/TagSidebar.tsx \
        frontend/src/components/marketing/photobank/__tests__/TagSidebar.test.tsx
git commit -m "feat(photobank): add folder regex toggle and validation to TagSidebar"
```

---

### Task 8: Page — wire `useFolderRegex` state in `PhotobankPage`

**Files:**
- Modify: `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx`

- [ ] **Step 1: Add state, handler, query param, and prop wiring**

Make these changes to `PhotobankPage.tsx`:

**a) Add state** (after line 58 — `const [useRegex, setUseRegex] = useState(false);`):

```typescript
  const [useFolderRegex, setUseFolderRegex] = useState(false);
```

**b) Add `useFolderRegex` to `usePhotos` params** (after the `useRegex` line, around line 96):

```typescript
  const { data: photosData, isLoading: photosLoading, isError: photosError } = usePhotos({
    tags: selectedTagNames.length > 0 ? selectedTagNames : undefined,
    search: search || undefined,
    useRegex: useRegex || undefined,
    useFolderRegex: useFolderRegex || undefined,
    folderPath: folderPath || undefined,
    withoutTags: withoutTags || undefined,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });
```

**c) Add `handleFolderRegexChange`** (after the existing `handleRegexChange`):

```typescript
  const handleFolderRegexChange = useCallback((value: boolean) => {
    setUseFolderRegex(value);
    setPage(1);
  }, []);
```

**d) Add `useFolderRegex` reset in `handleClearFilters`** (after `setUseRegex(false);`):

```typescript
  const handleClearFilters = useCallback(() => {
    setSelectedTagIds([]);
    setSearch("");
    setFolderPath("");
    setWithoutTags(false);
    setUseRegex(false);
    setUseFolderRegex(false);
    setPage(1);
    setSelectedIds(new Set());
    setSelectionAnchorId(null);
  }, []);
```

**e) Pass props to `<TagSidebar />`** (add `useFolderRegex` and `onFolderRegexChange` after the existing `onRegexChange` prop):

```tsx
          <TagSidebar
            tags={tagsData ?? []}
            selectedTagIds={selectedTagIds}
            search={search}
            folderPath={folderPath}
            withoutTags={withoutTags}
            useRegex={useRegex}
            useFolderRegex={useFolderRegex}
            onTagToggle={handleTagToggle}
            onSearchChange={handleSearchChange}
            onFolderPathChange={handleFolderPathChange}
            onWithoutTagsToggle={() => { setWithoutTags((v) => !v); setPage(1); }}
            onClearFilters={handleClearFilters}
            onRegexChange={handleRegexChange}
            onFolderRegexChange={handleFolderRegexChange}
            errorMessage={searchErrorMessage}
          />
```

- [ ] **Step 2: TypeScript compile check**

```bash
cd frontend && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 3: Frontend build**

```bash
cd frontend && npm run build
```

Expected: Build succeeds.

- [ ] **Step 4: Frontend lint**

```bash
cd frontend && npm run lint
```

Expected: No lint errors.

- [ ] **Step 5: Run all frontend tests**

```bash
cd frontend && npm test -- --run
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx
git commit -m "feat(photobank): wire useFolderRegex state in PhotobankPage"
```

---

### Task 9: Final verification

- [ ] **Step 1: Run all backend photobank tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Photobank"
```

Expected: All pass.

- [ ] **Step 2: Full backend build + format**

```bash
dotnet build && dotnet format --verify-no-changes
```

Expected: Succeeds, no formatting changes needed.

- [ ] **Step 3: Run all frontend tests**

```bash
cd frontend && npm test -- --run
```

Expected: All pass.

- [ ] **Step 4: Full frontend build + lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected: Succeeds.

---

## Manual smoke test checklist (after dev server is running)

1. Open photobank page → type a literal folder substring (e.g. `Marketing`) in the folder field → confirm photos filter correctly (substring, case-insensitive).
2. Check the new "Regex" checkbox below the folder input → type `^Marketing/` → confirm only photos whose `FolderPath` starts with `Marketing/` appear.
3. In folder regex mode, type `[unclosed` → expect red border on the folder input, inline error `"Neplatný regulární výraz"`, and no network call (debounce blocked).
4. Enter a .NET-valid but Postgres-invalid pattern (e.g. `(?<name>foo)` named groups are POSIX-unsupported) → expect a `400` with `PhotobankInvalidRegexPattern` error and the **folder path** value appearing in the error (not the filename value).
5. Click "Vymazat" → confirm the folder regex toggle resets to off.
6. Verify that the filename regex toggle still works independently (it should be unaffected by this change).
