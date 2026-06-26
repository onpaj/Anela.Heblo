# Article Feedback Repository Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the entity-materialising query in `ArticleRepository.GetFeedbackPagedAsync` with a column-restricted projection so EF Core no longer pulls the multi-kilobyte `HtmlContent` column on every feedback list request.

**Architecture:** Add `ArticleFeedbackProjection` (a `sealed record`) to the Domain layer next to `IArticleRepository` and `ArticleFeedbackStats` (the established pattern for non-entity repository return types). Update `IArticleRepository.GetFeedbackPagedAsync` to return `IReadOnlyList<ArticleFeedbackProjection>`. The Persistence implementation continues to build its filter/sort logic on `IQueryable<Article>` but appends a `.Select(...)` immediately before `ToListAsync`. The Application handler's mapping body is byte-identical because the projection's positional record properties (`Id`, `Title`, `Topic`, ...) match the existing entity property accesses. A SQL-shape regression test using `Testcontainers.PostgreSql` + `DbCommandInterceptor` (mirroring `PhotobankRepositoryGetTagsSqlShapeTests`) guards against future regression.

**Tech Stack:** .NET 8, EF Core 8, PostgreSQL, xUnit, FluentAssertions, Moq, Testcontainers.PostgreSql.

---

## File Structure

### Created

| File | Responsibility |
|------|---------------|
| `backend/src/Anela.Heblo.Domain/Features/Article/ArticleFeedbackProjection.cs` | Immutable Domain-layer record carrying the eight columns the feedback list path consumes. |
| `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackProjectionSqlTests.cs` | Integration test that captures the SQL emitted by `GetFeedbackPagedAsync` against real Postgres and asserts `HtmlContent` is absent and the projected columns are present. |

### Modified

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs` | Return type of `GetFeedbackPagedAsync` changes from `IReadOnlyList<Article>` to `IReadOnlyList<ArticleFeedbackProjection>`. Other methods untouched. |
| `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs` | `GetFeedbackPagedAsync` body adds `.Select(a => new ArticleFeedbackProjection(...))` immediately before `ToListAsync`. Filter and sort logic unchanged. |
| `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs` | Three Moq setups updated to return `IReadOnlyList<ArticleFeedbackProjection>` instead of `IReadOnlyList<Article>`. Assertions unchanged. |

### Unmodified (verified)

`backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs` requires no code edits. The handler accesses `a.Id`, `a.Title`, `a.Topic`, `a.RequestedBy`, `a.CreatedAt`, `a.PrecisionScore`, `a.StyleScore`, `a.FeedbackComment` — all of which are positional record properties on `ArticleFeedbackProjection` with identical names and matching types. The file is only verified to compile after the interface change, not edited.

---

## Task 1: Add `ArticleFeedbackProjection` Domain record

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Article/ArticleFeedbackProjection.cs`

**Rationale:** Adding the type first lets the rest of the refactor proceed with a stable name to bind against. The Domain layer is the only legal home — `IArticleRepository` lives in `Anela.Heblo.Domain` and the Domain `csproj` does not (and must not) reference `Anela.Heblo.Application`. The existing `ArticleFeedbackStats` (`sealed record` in the same folder) is the precedent.

- [ ] **Step 1: Create `ArticleFeedbackProjection.cs`**

Write the file at `backend/src/Anela.Heblo.Domain/Features/Article/ArticleFeedbackProjection.cs` with this exact content:

```csharp
namespace Anela.Heblo.Domain.Features.Article;

public sealed record ArticleFeedbackProjection(
    Guid Id,
    string? Title,
    string Topic,
    string? RequestedBy,
    DateTimeOffset CreatedAt,
    int? PrecisionScore,
    int? StyleScore,
    string? FeedbackComment);
```

Field types and nullability are taken directly from `Article.cs`:
- `Id` → `Guid` (non-null)
- `Title` → `string?`
- `Topic` → `string` (non-null; entity defaults it to `""`)
- `RequestedBy` → `string?`
- `CreatedAt` → `DateTimeOffset` (note: the spec text said `DateTime`; entity is `DateTimeOffset` — use `DateTimeOffset`)
- `PrecisionScore` → `int?`
- `StyleScore` → `int?`
- `FeedbackComment` → `string?`

- [ ] **Step 2: Build the Domain project**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Article/ArticleFeedbackProjection.cs
git commit -m "feat: add ArticleFeedbackProjection domain record"
```

---

## Task 2: Write SQL-shape regression test (RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackProjectionSqlTests.cs`

**Rationale:** Write the test before the implementation change. The test calls `ArticleRepository.GetFeedbackPagedAsync` against a real Postgres container, captures the SQL via `DbCommandInterceptor`, and asserts the projected columns appear in the SELECT while `HtmlContent` does not. Before Task 4 it MUST fail (current impl SELECTs `HtmlContent`). After Task 4 it MUST pass.

The test mirrors `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsSqlShapeTests.cs` — the established pattern for SQL-shape verification in this codebase. No `InternalsVisibleTo` or test seam is required.

- [ ] **Step 1: Create the test directory and file**

Write the file at `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackProjectionSqlTests.cs` with this exact content:

```csharp
using System.Data.Common;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Article;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Anela.Heblo.Tests.Article.Persistence;

[Trait("Category", "Integration")]
public class ArticleRepositoryFeedbackProjectionSqlTests : IAsyncLifetime
{
    static ArticleRepositoryFeedbackProjectionSqlTests()
    {
        // Required on macOS with Podman: the Ryuk ResourceReaper container
        // cannot bind to the Docker socket and throws a NullReferenceException.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private ArticleRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Minimal schema — only the columns the projection query touches plus the
        // required NOT NULL columns from ArticleConfiguration. HtmlContent is
        // included so the test would actually fail if the production query still
        // pulled it.
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE public."Articles" (
                    "Id"              uuid                     NOT NULL PRIMARY KEY,
                    "Topic"           varchar(2000)            NOT NULL,
                    "Scope"           varchar(50)              NOT NULL,
                    "Audience"        varchar(500),
                    "Angle"           varchar(500),
                    "Length"          varchar(50)              NOT NULL,
                    "LanguageNote"    varchar(500),
                    "UsedKnowledgeBase" boolean                NOT NULL DEFAULT false,
                    "UsedWebSearch"   boolean                  NOT NULL DEFAULT false,
                    "StyleGuideDriveId" varchar(500),
                    "StyleGuideItemPath" varchar(500),
                    "Title"           text,
                    "HtmlContent"     text,
                    "Status"          integer                  NOT NULL,
                    "ErrorMessage"    varchar(2000),
                    "RequestedBy"     varchar(200),
                    "PrecisionScore"  integer,
                    "StyleScore"      integer,
                    "FeedbackComment" text,
                    "CreatedAt"       timestamptz              NOT NULL,
                    "GeneratedAt"     timestamptz
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ArticleRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetFeedbackPagedAsync_DoesNotSelectHtmlContent()
    {
        _interceptor.Reset();

        await _repository.GetFeedbackPagedAsync(
            hasFeedback: null,
            requestedBy: null,
            sortBy: "CreatedAt",
            descending: true,
            page: 1,
            pageSize: 20,
            ct: CancellationToken.None);

        // The COUNT(*) and the SELECT-with-Skip/Take both fire; inspect the row-returning
        // SELECT (the one that contains a column list, not just COUNT).
        var rowSelect = _interceptor.Commands
            .FirstOrDefault(c => c.Contains("FROM \"Articles\"", StringComparison.OrdinalIgnoreCase)
                && !c.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase));

        rowSelect.Should().NotBeNull("the repository must issue a row-returning SELECT against Articles");
        rowSelect!.Should().NotContain("\"HtmlContent\"",
            "HtmlContent is multi-KB per row and is never read by the feedback list handler");
    }

    [Fact]
    public async Task GetFeedbackPagedAsync_SelectsExactlyTheProjectedColumns()
    {
        _interceptor.Reset();

        await _repository.GetFeedbackPagedAsync(
            hasFeedback: null,
            requestedBy: null,
            sortBy: "CreatedAt",
            descending: true,
            page: 1,
            pageSize: 20,
            ct: CancellationToken.None);

        var rowSelect = _interceptor.Commands
            .First(c => c.Contains("FROM \"Articles\"", StringComparison.OrdinalIgnoreCase)
                && !c.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase));

        // Every projected column appears in the SELECT.
        foreach (var column in new[]
                 {
                     "\"Id\"", "\"Title\"", "\"Topic\"", "\"RequestedBy\"",
                     "\"CreatedAt\"", "\"PrecisionScore\"", "\"StyleScore\"", "\"FeedbackComment\""
                 })
        {
            rowSelect.Should().Contain(column,
                $"projected column {column} must appear in the SELECT");
        }
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public void Reset() => Commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
```

- [ ] **Step 2: Run the new test — confirm it fails (RED)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ArticleRepositoryFeedbackProjectionSqlTests"`

Expected: `GetFeedbackPagedAsync_DoesNotSelectHtmlContent` FAILS with an assertion that the SELECT contains `"HtmlContent"`. This proves the test correctly detects the current bad behaviour. **Do not commit yet** — failing tests do not land in main.

If Docker/Podman is unavailable in the environment, document that the test will be validated in Task 6 after the production change lands; proceed to Task 3.

---

## Task 3: Update `IArticleRepository.GetFeedbackPagedAsync` signature

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs`

- [ ] **Step 1: Change the return type**

Edit `backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs`. Replace the lines:

```csharp
    Task<(IReadOnlyList<Article> Items, int TotalCount)> GetFeedbackPagedAsync(
        bool? hasFeedback,
        string? requestedBy,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken ct = default);
```

with:

```csharp
    Task<(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)> GetFeedbackPagedAsync(
        bool? hasFeedback,
        string? requestedBy,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken ct = default);
```

No other methods on the interface change. Other methods (`GetByIdAsync`, `GetForUpdateAsync`, `GetPagedAsync`, `GetWithStepsAsync`) legitimately return the full `Article` aggregate and are out of scope.

- [ ] **Step 2: Build the Domain project**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: build succeeds (interface change in isolation compiles fine).

- [ ] **Step 3: Build the whole solution to observe expected break**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: Build FAILS. Errors will appear in:
- `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs` — implementation no longer satisfies the interface.
- `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs` — Moq setups return the wrong tuple type.

`GetArticleFeedbackListHandler.cs` should still compile because it accesses members that exist on both `Article` and `ArticleFeedbackProjection`.

**Do not commit yet** — broken build does not land in main.

---

## Task 4: Update `ArticleRepository.GetFeedbackPagedAsync` to project at the database

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs`

- [ ] **Step 1: Replace the method body**

Edit `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs`. Replace the entire `GetFeedbackPagedAsync` method (currently lines 59–98) with:

```csharp
    public async Task<(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)> GetFeedbackPagedAsync(
        bool? hasFeedback,
        string? requestedBy,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.Articles.AsNoTracking();

        if (hasFeedback == true)
            query = query.Where(a => a.PrecisionScore != null || a.StyleScore != null);
        else if (hasFeedback == false)
            query = query.Where(a => a.PrecisionScore == null && a.StyleScore == null);

        if (!string.IsNullOrWhiteSpace(requestedBy))
            query = query.Where(a => a.RequestedBy == requestedBy);

        query = sortBy switch
        {
            "PrecisionScore" => descending
                ? query.OrderByDescending(a => a.PrecisionScore)
                : query.OrderBy(a => a.PrecisionScore),
            "StyleScore" => descending
                ? query.OrderByDescending(a => a.StyleScore)
                : query.OrderBy(a => a.StyleScore),
            _ => descending
                ? query.OrderByDescending(a => a.CreatedAt)
                : query.OrderBy(a => a.CreatedAt)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ArticleFeedbackProjection(
                a.Id,
                a.Title,
                a.Topic,
                a.RequestedBy,
                a.CreatedAt,
                a.PrecisionScore,
                a.StyleScore,
                a.FeedbackComment))
            .ToListAsync(ct);

        return (items, total);
    }
```

Note: filters and sort continue to operate on `IQueryable<Article>`; the projection is applied immediately before `ToListAsync` so any future sort column added to `AllowedSortColumns` continues to work without further change (provided it's already present on the entity). The `CountAsync` runs before `Skip/Take/Select` and EF Core translates it to `SELECT COUNT(*)`, so no rows are materialised by the count call.

The `Anela.Heblo.Domain.Features.Article` using directive at the top of the file is already present (line 1), so `ArticleFeedbackProjection` is in scope. No new `using` is needed.

- [ ] **Step 2: Build the Persistence project**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: build succeeds.

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: only `GetArticleFeedbackListHandlerTests.cs` still fails (Moq setups return the wrong tuple). `GetArticleFeedbackListHandler.cs` compiles unchanged because positional record properties (`a.Id`, `a.Title`, ...) match the existing field accesses.

---

## Task 5: Update handler unit-test mock setups

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs`

**Rationale:** The handler under test now consumes `IReadOnlyList<ArticleFeedbackProjection>`. The three test methods each set up `_repository.Setup(r => r.GetFeedbackPagedAsync(...)).ReturnsAsync(((IReadOnlyList<DomainArticle>)..., ...))`. Each setup must change to return `IReadOnlyList<ArticleFeedbackProjection>`. Assertions remain unchanged.

- [ ] **Step 1: Replace `Handle_DefaultParams_RunsPagedAndStatsInParallelAndProjectsResults`**

In `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs`, replace:

```csharp
    [Fact]
    public async Task Handle_DefaultParams_RunsPagedAndStatsInParallelAndProjectsResults()
    {
        var article = new DomainArticle
        {
            Id = Guid.NewGuid(),
            Topic = "Sun care",
            Title = "Sun care title",
            RequestedBy = "alice",
            CreatedAt = DateTimeOffset.UtcNow,
            PrecisionScore = 4,
            StyleScore = 5,
            FeedbackComment = "ok",
        };

        _repository.Setup(r => r.GetFeedbackPagedAsync(
                null, null, "CreatedAt", true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<DomainArticle>)new[] { article }, 1));
```

with:

```csharp
    [Fact]
    public async Task Handle_DefaultParams_RunsPagedAndStatsInParallelAndProjectsResults()
    {
        var article = new ArticleFeedbackProjection(
            Id: Guid.NewGuid(),
            Title: "Sun care title",
            Topic: "Sun care",
            RequestedBy: "alice",
            CreatedAt: DateTimeOffset.UtcNow,
            PrecisionScore: 4,
            StyleScore: 5,
            FeedbackComment: "ok");

        _repository.Setup(r => r.GetFeedbackPagedAsync(
                null, null, "CreatedAt", true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ArticleFeedbackProjection>)new[] { article }, 1));
```

The downstream assertions (`response.Items[0].Id.Should().Be(article.Id)`, etc.) work unchanged because positional record properties expose the named values via `init` properties.

- [ ] **Step 2: Replace `Handle_UnknownSortBy_FallsBackToCreatedAt`**

In the same file, replace:

```csharp
        _repository.Setup(r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), "CreatedAt", true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<DomainArticle>)Array.Empty<DomainArticle>(), 0));
```

with:

```csharp
        _repository.Setup(r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), "CreatedAt", true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ArticleFeedbackProjection>)Array.Empty<ArticleFeedbackProjection>(), 0));
```

- [ ] **Step 3: Replace `Handle_PageSizeOutsideAllowlist_FallsBackTo20`**

In the same file, replace:

```csharp
        _repository.Setup(r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<int>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<DomainArticle>)Array.Empty<DomainArticle>(), 0));
```

with:

```csharp
        _repository.Setup(r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<int>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ArticleFeedbackProjection>)Array.Empty<ArticleFeedbackProjection>(), 0));
```

- [ ] **Step 4: Remove the `DomainArticle` alias if it is no longer referenced**

After the three edits, the line `using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;` at the top of the file is unused. Grep the file for `DomainArticle` — if there are no remaining references, delete that `using` line. If any reference remains (e.g. an unrelated test added later), leave the alias.

Run a grep within the file:

```bash
grep -n "DomainArticle" backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs
```

If output is empty, delete `using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;`.

- [ ] **Step 5: Run the handler tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetArticleFeedbackListHandlerTests"`
Expected: all three handler tests PASS.

---

## Task 6: Run the SQL-shape regression test — confirm GREEN

**Files:**
- (No edits — verification only.)

- [ ] **Step 1: Run the SQL-shape test against the production change**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ArticleRepositoryFeedbackProjectionSqlTests"`

Expected: both facts PASS.
- `GetFeedbackPagedAsync_DoesNotSelectHtmlContent` — passes because the `.Select(...)` restricts the SQL to projected columns.
- `GetFeedbackPagedAsync_SelectsExactlyTheProjectedColumns` — passes because EF Core emits each projected column name in the SELECT.

If the Postgres container cannot start in the environment (Docker/Podman unavailable), document the failure as environmental and ensure the test runs in CI before merge.

- [ ] **Step 2: Build the whole solution and run the entire test suite**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, no warnings introduced.

Run: `dotnet test backend/Anela.Heblo.sln --filter "Category!=Integration"`
Expected: all non-integration tests pass.

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: all tests pass (subject to Docker availability for the integration tier).

- [ ] **Step 3: Apply formatting**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no changes (or only whitespace adjustments to files touched in this plan).

- [ ] **Step 4: Commit the refactor**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs \
        backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs \
        backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackProjectionSqlTests.cs
git commit -m "refactor(article): project feedback list at database level

Restrict ArticleRepository.GetFeedbackPagedAsync to the eight columns the
feedback list handler reads, eliminating the multi-KB HtmlContent transfer
on every page request. Return type narrows to IReadOnlyList<ArticleFeedbackProjection>.
Adds a SQL-shape regression test guarding against HtmlContent re-entering
the SELECT."
```

---

## Self-Review Notes

**Spec coverage check:**
- FR-1 (DB-level projection, no `HtmlContent` in SELECT) → Task 4 (impl) + Task 2/6 (SQL-shape test enforces it).
- FR-2 (introduce `ArticleFeedbackProjection`) → Task 1. Location amended per arch review §1 to Domain layer.
- FR-3 (update `IArticleRepository` contract) → Task 3.
- FR-4 (simplify handler mapping) → Verified no edits needed; positional record properties match. Documented in "Unmodified" file table.
- FR-5 (preserve other repository methods) → Task 3 Step 1 explicitly states "No other methods on the interface change."
- NFR-1 (performance, no `HtmlContent`) → Task 4 + Task 6.
- NFR-2 (security — unchanged surface) → No additional task; covered by FR-4 verification.
- NFR-3 (maintainability) → Achieved by Task 1 type design and arch review's Decision 4 documentation pattern.
- NFR-4 (SQL-shape test coverage) → Task 2 + Task 6.

**Type consistency check:**
- `ArticleFeedbackProjection` field types match `Article` entity property types exactly, including nullability and `DateTimeOffset` (not `DateTime`).
- Handler accesses (`a.Id`, `a.Title`, `a.Topic`, `a.RequestedBy`, `a.CreatedAt`, `a.PrecisionScore`, `a.StyleScore`, `a.FeedbackComment`) match the positional record's auto-generated properties.
- Test mocks use the same `IReadOnlyList<ArticleFeedbackProjection>` shape that the interface declares.

**Placeholder scan:** No TBDs, no "implement later" markers, no "similar to Task N" cross-references — every code block in every step contains the literal content the engineer needs to type.
