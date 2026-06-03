# Bank Import Tab Filter Inputs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the four filter controls in the Bank module's Import tab (Transfer ID, Account, statement date range, errors-only) end-to-end so clicking "Filtrovat" actually constrains the bank-statement list returned by `GET /api/bank-statements`.

**Architecture:** Backend gets five new optional `[FromQuery]` parameters that flow through `GetBankStatementListRequest` → FluentValidation → `GetBankStatementListHandler` → `IBankStatementImportRepository.GetFilteredAsync`, where they are translated to PostgreSQL `WHERE` clauses (`EF.Functions.ILike` for the two string filters, half-open date range, `ImportResult != "OK"` for errors-only). The validator and `ValidationBehavior` are registered for the Bank slice (currently neither is wired). The frontend's hand-rolled `useBankStatementsList` hook forwards the new fields, and `ImportTab.tsx` collapses its per-input "committed-filter" state into a single object that becomes the React Query key.

**Tech Stack:** .NET 8, MediatR, FluentValidation, AutoMapper, EF Core 8 + Npgsql, xUnit + FluentAssertions + Testcontainers (Postgres 16) for `ILike` tests; React 18, React Query, hand-rolled fetch hook, Jest + React Testing Library.

---

## Pre-flight context

Before any step, the implementer should have read:
- `spec.r2.md` — the source spec.
- `arch-review.r1.md` — the architecture review (it overrides parts of the spec, see §"Spec amendments" below).
- `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` — canonical filter+sort+paginate template.
- `frontend/src/components/customer/tabs/ImportTab.tsx` — current state, especially the half-baked "committed filter" pattern at lines 25-94.

**Spec amendments from arch review (decided, not open questions):**
1. `TransferId` uses `EF.Functions.ILike(...)` with LIKE-escape (NOT plain `.Contains`).
2. `Account` uses the same primitive with LIKE-escape (`%`, `_`, `\` must be escaped).
3. Date range uses **half-open** form: `StatementDate >= from.Date && StatementDate < to.Date.AddDays(1)`. Both bounds are constructed with `DateTime.SpecifyKind(value.Date, DateTimeKind.Utc)` to match how `BankStatementImport` writes `StatementDate` (the ctor forces UTC kind).
4. `ILike` tests run against real Postgres via Testcontainers, NOT the InMemory provider — InMemory does not translate `EF.Functions.ILike`.
5. Validation lives in `GetBankStatementListRequestValidator` (FluentValidation), not in the controller. The validator is **currently not registered** anywhere — registration is a task in this plan.
6. The controller's generic `catch (Exception) → 500` must be split so `ValidationException` returns 400.

---

## File Structure

### New files

- `backend/src/Anela.Heblo.Persistence/Shared/LikeEscape.cs` — shared LIKE-escape helper (extracted from `PackageRepository`'s private helper so the Bank repository can reuse it).
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryFilterIntegrationTests.cs` — Postgres Testcontainers tests for `ILike`-based filters (transferId, account).
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` — handler-level tests with a mocked repository.
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListRequestValidatorTests.cs` — validator rule tests.
- `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx` — component tests for filter wiring.

### Modified files

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — add 5 new optional fields.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — parse new dates, trim Account, forward to repo, add `CancellationToken`.
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — add length, parseability, range-ordering rules.
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` — register validator and `ValidationBehavior`.
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — extend `GetFilteredAsync` signature.
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — implement new predicates.
- `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` — switch its private `EscapeLike` to the new shared helper (small refactor to keep one source of truth).
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — add `[FromQuery]` params, split exception handling so `ValidationException` → 400.
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` — add tests for date-range and `errorsOnly` filters (InMemory-compatible).
- `frontend/src/api/hooks/useBankStatements.ts` — extend `GetBankStatementListRequest` and the URL builder.
- `frontend/src/components/customer/tabs/ImportTab.tsx` — collapse to single committed-filters object, pass to hook, add date-range guard.

---

## Phase 1 — Shared LIKE-escape helper

Extract `PackageRepository`'s private `EscapeLike` into a reusable static class. This keeps the Bank repository from duplicating the same three `.Replace` calls and gives us a single audit point for LIKE-escape rules.

### Task 1: Create `LikeEscape` helper

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Shared/LikeEscape.cs`

- [ ] **Step 1: Create the helper class**

Write `backend/src/Anela.Heblo.Persistence/Shared/LikeEscape.cs`:

```csharp
namespace Anela.Heblo.Persistence.Shared;

/// <summary>
/// Escapes user input so it can be safely interpolated into the pattern argument of
/// EF.Functions.Like / EF.Functions.ILike with a "\\" escape character.
/// Without escaping, characters like '%' or '_' typed by the user would behave as
/// wildcards and silently broaden the match.
/// </summary>
public static class LikeEscape
{
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
```

- [ ] **Step 2: Switch `PackageRepository` to the shared helper**

In `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs`:

Add the using at the top of the file (next to the other usings):

```csharp
using Anela.Heblo.Persistence.Shared;
```

Replace the three `EscapeLike(...)` call-sites and delete the private method. The three lines currently at `PackageRepository.cs:28-32`:

```csharp
q = q.Where(p => EF.Functions.ILike(p.OrderCode, $"%{EscapeLike(orderCode)}%", "\\"));
// ...
q = q.Where(p => EF.Functions.ILike(p.CustomerName, $"%{EscapeLike(customerName)}%", "\\"));
// ...
q = q.Where(p => EF.Functions.ILike(p.PackageNumber, $"%{EscapeLike(packageNumber)}%", "\\"));
```

become:

```csharp
q = q.Where(p => EF.Functions.ILike(p.OrderCode, $"%{LikeEscape.Escape(orderCode)}%", "\\"));
// ...
q = q.Where(p => EF.Functions.ILike(p.CustomerName, $"%{LikeEscape.Escape(customerName)}%", "\\"));
// ...
q = q.Where(p => EF.Functions.ILike(p.PackageNumber, $"%{LikeEscape.Escape(packageNumber)}%", "\\"));
```

Delete the private `EscapeLike` method (the last method in the file, at `PackageRepository.cs:75-76`):

```csharp
private static string EscapeLike(string value) =>
    value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
```

- [ ] **Step 3: Build and run existing repository tests to verify no regression**

Run from repo root:

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackageRepository"
```

Expected: build succeeds; any existing `PackageRepository` tests pass (the helper is behaviour-preserving).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Shared/LikeEscape.cs \
        backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs
git commit -m "refactor: extract LikeEscape helper for ILike pattern escaping"
```

---

## Phase 2 — Backend contract: request DTO and repository interface

Lock in the new shape of `GetBankStatementListRequest` and `IBankStatementImportRepository.GetFilteredAsync` before touching implementation. Both changes are additive and default-valued so existing call-sites (`BankStatementsController.GetBankStatement(int id)`) keep compiling.

### Task 2: Extend `GetBankStatementListRequest` DTO

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs`

- [ ] **Step 1: Add five optional properties to the DTO**

Replace the entire body of `GetBankStatementListRequest.cs` with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListRequest : IRequest<GetBankStatementListResponse>
{
    public int? Id { get; set; }
    public string? StatementDate { get; set; }
    public string? ImportDate { get; set; }

    public string? TransferId { get; set; }
    public string? Account { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public bool? ErrorsOnly { get; set; }

    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 10;
    public string? OrderBy { get; set; } = "ImportDate";
    public bool Ascending { get; set; } = false;
}
```

The DTO stays a `class` (project rule: never `record` for OpenAPI-exposed types).

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: builds clean. (No callers reference the new properties yet, so this is purely additive.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs
git commit -m "feat: add filter properties to GetBankStatementListRequest"
```

### Task 3: Extend `IBankStatementImportRepository.GetFilteredAsync` interface

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`

- [ ] **Step 1: Replace the interface with the extended signature**

Overwrite `IBankStatementImportRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankStatementImportRepository
{
    Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
        int? id = null,
        DateTime? statementDate = null,
        DateTime? importDate = null,
        string? transferId = null,
        string? account = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        bool? errorsOnly = null,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false,
        CancellationToken cancellationToken = default);

    Task<BankStatementImport?> GetByIdAsync(int id);
    Task<BankStatementImport> AddAsync(BankStatementImport bankStatement);
}
```

All new parameters default to `null`/`false`, so existing callers (handler, controller's by-id call) remain source-compatible. `CancellationToken` is added because every other repo in the codebase takes one; passing `CancellationToken.None` from existing callers is fine.

- [ ] **Step 2: Verify build (will show one warning until we update the implementation in the next phase)**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: builds — the existing implementation still matches the interface as long as `BankStatementImportRepository.GetFilteredAsync` accepts the new parameters in the next task. (If you encounter a CS0535 error here saying the implementation doesn't satisfy the interface, that's expected; it will be fixed in Task 4. Do NOT commit until Task 4 passes.)

- [ ] **Step 3 (defer commit until after Task 4)**

Skip this step; the next task makes the implementation match, then we commit them together.

---

## Phase 3 — Backend repository implementation + tests

Add the predicates to `BankStatementImportRepository`. The five new predicates mirror `PackageRepository` exactly: ILike + LIKE-escape for the two string filters, half-open range for dates, plain `!=` for the `ImportResult` predicate.

### Task 4: Implement new filter predicates in `BankStatementImportRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`

- [ ] **Step 1: Replace the file body**

Overwrite `BankStatementImportRepository.cs` with:

```csharp
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Persistence.Shared;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Bank;

public class BankStatementImportRepository : IBankStatementImportRepository
{
    private readonly ApplicationDbContext _context;

    public BankStatementImportRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
        int? id = null,
        DateTime? statementDate = null,
        DateTime? importDate = null,
        string? transferId = null,
        string? account = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        bool? errorsOnly = null,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BankStatements.AsNoTracking().AsQueryable();

        if (id.HasValue)
            query = query.Where(bs => bs.Id == id.Value);

        if (statementDate.HasValue)
            query = query.Where(bs => bs.StatementDate.Date == statementDate.Value.Date);

        if (importDate.HasValue)
            query = query.Where(bs => bs.ImportDate.Date == importDate.Value.Date);

        if (!string.IsNullOrWhiteSpace(transferId))
        {
            var pattern = $"%{LikeEscape.Escape(transferId)}%";
            query = query.Where(bs => EF.Functions.ILike(bs.TransferId, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(account))
        {
            var pattern = $"%{LikeEscape.Escape(account)}%";
            query = query.Where(bs => EF.Functions.ILike(bs.Account, pattern, "\\"));
        }

        if (dateFrom.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Utc);
            query = query.Where(bs => bs.StatementDate >= fromUtc);
        }

        if (dateTo.HasValue)
        {
            var toExclusiveUtc = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(bs => bs.StatementDate < toExclusiveUtc);
        }

        if (errorsOnly == true)
            query = query.Where(bs => bs.ImportResult != "OK");

        var totalCount = await query.CountAsync(cancellationToken);

        query = orderBy.ToLowerInvariant() switch
        {
            "id" => ascending ? query.OrderBy(x => x.Id) : query.OrderByDescending(x => x.Id),
            "statementdate" => ascending
                ? query.OrderBy(x => x.StatementDate).ThenBy(x => x.Id)
                : query.OrderByDescending(x => x.StatementDate).ThenBy(x => x.Id),
            "importdate" => ascending
                ? query.OrderBy(x => x.ImportDate).ThenBy(x => x.Id)
                : query.OrderByDescending(x => x.ImportDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.ImportDate).ThenBy(x => x.Id)
        };

        var items = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<BankStatementImport?> GetByIdAsync(int id)
    {
        return await _context.BankStatements.FindAsync(id);
    }

    public async Task<BankStatementImport> AddAsync(BankStatementImport bankStatement)
    {
        _context.BankStatements.Add(bankStatement);
        await _context.SaveChangesAsync();
        return bankStatement;
    }
}
```

Notes carried over from the arch review:
- `dateFrom`/`dateTo` use `DateTime.SpecifyKind(..., Utc)` because `BankStatementImport`'s constructor forces `StatementDate.Kind = Utc`, and Npgsql with `timestamp without time zone` rejects mixed kinds on some configurations.
- `errorsOnly == true` is intentional (treats `null` and `false` identically as "no constraint").
- The existing `statementDate.Date == ...` and `importDate.Date == ...` predicates are preserved for backward compat; they coexist with the new half-open `StatementDate >=/<` range filters.

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: builds clean.

- [ ] **Step 3: Run existing Bank repository tests (InMemory) — must still pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~BankStatementImportRepositoryTests"
```

Expected: all 11 existing tests pass. None of them exercise the new ILike paths, so the InMemory provider does not blow up here.

- [ ] **Step 4: Commit (covers Tasks 3 + 4)**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs \
        backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs
git commit -m "feat: add filter predicates to BankStatementImportRepository"
```

### Task 5: InMemory unit tests for date-range and errors-only filters

These three predicates use plain `>=`/`<`/`!=` which the InMemory provider translates correctly. The two `ILike`-based ones (transferId, account) need real Postgres and live in the integration-test class created in Task 6.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs`

- [ ] **Step 1: Add three new test methods**

Append the following to the test class **immediately before** the `CreateTestImport` helper method (around line 320 of `BankStatementImportRepositoryTests.cs`). Keep the existing `CreateTestImport` and `Dispose` methods unchanged.

```csharp
    [Fact]
    public async Task GetFilteredAsync_WithDateFromOnly_ReturnsStatementsOnOrAfterDate()
    {
        // Arrange
        var cutoff = DateTime.UtcNow.Date.AddDays(-2);
        var before = CreateTestImport("T1", DateTime.UtcNow.Date.AddDays(-5), "A", CurrencyCode.CZK);
        var onCutoff = CreateTestImport("T2", cutoff, "A", CurrencyCode.CZK);
        var after = CreateTestImport("T3", DateTime.UtcNow.Date, "A", CurrencyCode.CZK);

        await _repository.AddAsync(before);
        await _repository.AddAsync(onCutoff);
        await _repository.AddAsync(after);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(dateFrom: cutoff);

        // Assert
        Assert.Equal(2, totalCount);
        var transferIds = items.Select(i => i.TransferId).ToList();
        Assert.Contains("T2", transferIds);
        Assert.Contains("T3", transferIds);
        Assert.DoesNotContain("T1", transferIds);
    }

    [Fact]
    public async Task GetFilteredAsync_WithDateToOnly_ReturnsStatementsOnOrBeforeDate()
    {
        // Arrange
        var cutoff = DateTime.UtcNow.Date.AddDays(-2);
        var before = CreateTestImport("T1", DateTime.UtcNow.Date.AddDays(-5), "A", CurrencyCode.CZK);
        var onCutoff = CreateTestImport("T2", cutoff, "A", CurrencyCode.CZK);
        var after = CreateTestImport("T3", DateTime.UtcNow.Date, "A", CurrencyCode.CZK);

        await _repository.AddAsync(before);
        await _repository.AddAsync(onCutoff);
        await _repository.AddAsync(after);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(dateTo: cutoff);

        // Assert
        Assert.Equal(2, totalCount);
        var transferIds = items.Select(i => i.TransferId).ToList();
        Assert.Contains("T1", transferIds);
        Assert.Contains("T2", transferIds);
        Assert.DoesNotContain("T3", transferIds);
    }

    [Fact]
    public async Task GetFilteredAsync_WithDateFromAndDateTo_ReturnsStatementsInRangeInclusive()
    {
        // Arrange
        var rangeStart = DateTime.UtcNow.Date.AddDays(-3);
        var rangeEnd = DateTime.UtcNow.Date.AddDays(-1);
        var before = CreateTestImport("T1", DateTime.UtcNow.Date.AddDays(-5), "A", CurrencyCode.CZK);
        var onStart = CreateTestImport("T2", rangeStart, "A", CurrencyCode.CZK);
        var inside = CreateTestImport("T3", DateTime.UtcNow.Date.AddDays(-2), "A", CurrencyCode.CZK);
        var onEnd = CreateTestImport("T4", rangeEnd, "A", CurrencyCode.CZK);
        var after = CreateTestImport("T5", DateTime.UtcNow.Date, "A", CurrencyCode.CZK);

        await _repository.AddAsync(before);
        await _repository.AddAsync(onStart);
        await _repository.AddAsync(inside);
        await _repository.AddAsync(onEnd);
        await _repository.AddAsync(after);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(dateFrom: rangeStart, dateTo: rangeEnd);

        // Assert
        Assert.Equal(3, totalCount);
        var transferIds = items.Select(i => i.TransferId).ToList();
        Assert.Contains("T2", transferIds);
        Assert.Contains("T3", transferIds);
        Assert.Contains("T4", transferIds);
    }

    [Fact]
    public async Task GetFilteredAsync_WithErrorsOnlyTrue_ReturnsOnlyNonOkStatements()
    {
        // Arrange
        var ok = CreateTestImport("T1", DateTime.UtcNow.Date, "A", CurrencyCode.CZK);
        ok.ImportResult = "OK";
        var processingError = CreateTestImport("T2", DateTime.UtcNow.Date, "A", CurrencyCode.CZK);
        processingError.ImportResult = "PROCESSING_ERROR: connection refused";
        var unknown = CreateTestImport("T3", DateTime.UtcNow.Date, "A", CurrencyCode.CZK);
        unknown.ImportResult = "UNKNOWN_ERROR";

        await _repository.AddAsync(ok);
        await _repository.AddAsync(processingError);
        await _repository.AddAsync(unknown);

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(errorsOnly: true);

        // Assert
        Assert.Equal(2, totalCount);
        var transferIds = items.Select(i => i.TransferId).ToList();
        Assert.Contains("T2", transferIds);
        Assert.Contains("T3", transferIds);
        Assert.DoesNotContain("T1", transferIds);
    }

    [Fact]
    public async Task GetFilteredAsync_WithErrorsOnlyFalseOrNull_DoesNotRestrictByImportResult()
    {
        // Arrange
        var ok = CreateTestImport("T1", DateTime.UtcNow.Date, "A", CurrencyCode.CZK);
        ok.ImportResult = "OK";
        var err = CreateTestImport("T2", DateTime.UtcNow.Date, "A", CurrencyCode.CZK);
        err.ImportResult = "PROCESSING_ERROR";

        await _repository.AddAsync(ok);
        await _repository.AddAsync(err);

        // Act
        var (itemsNull, totalNull) = await _repository.GetFilteredAsync(errorsOnly: null);
        var (itemsFalse, totalFalse) = await _repository.GetFilteredAsync(errorsOnly: false);

        // Assert
        Assert.Equal(2, totalNull);
        Assert.Equal(2, totalFalse);
        Assert.Equal(2, itemsNull.Count());
        Assert.Equal(2, itemsFalse.Count());
    }
```

- [ ] **Step 2: Run new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~BankStatementImportRepositoryTests"
```

Expected: all 16 tests pass (11 existing + 5 new). If any of the new range tests fail with a kind-mismatch exception under InMemory, that's a sign Step 1 of Task 4 was not followed exactly — `DateTime.SpecifyKind(..., Utc)` must be used; remove that and InMemory will still pass but the production Postgres path will break.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs
git commit -m "test: cover date-range and errors-only filters in BankStatementImportRepository"
```

### Task 6: Postgres Testcontainers integration tests for ILike filters

The InMemory provider does not translate `EF.Functions.ILike` — it throws at runtime. Use the same pattern as `MeetingTranscriptRepositorySearchIntegrationTests` (Testcontainers + manually created table).

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryFilterIntegrationTests.cs`

- [ ] **Step 1: Create the integration test class**

Write `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryFilterIntegrationTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Bank;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

[Trait("Category", "Integration")]
public class BankStatementImportRepositoryFilterIntegrationTests : IAsyncLifetime
{
    static BankStatementImportRepositoryFilterIntegrationTests()
    {
        // Required on macOS with Podman; mirrors MeetingTranscriptRepositorySearchIntegrationTests.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private ApplicationDbContext _context = null!;
    private BankStatementImportRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new ApplicationDbContext(options);

        // EnsureCreatedAsync would try to install extensions not present in the plain
        // postgres:16 image (e.g. vector for KB). Create only the BankStatements table.
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE SCHEMA IF NOT EXISTS public;

            CREATE TABLE IF NOT EXISTS public."BankStatements" (
                "Id"            serial       PRIMARY KEY,
                "TransferId"    varchar(100) NOT NULL,
                "StatementDate" timestamp    NOT NULL,
                "ImportDate"    timestamp    NOT NULL,
                "Account"       text         NOT NULL,
                "Currency"      integer      NOT NULL,
                "ItemCount"     integer      NOT NULL,
                "ImportResult"  text         NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BankStatements_TransferId"
                ON public."BankStatements" ("TransferId");
            CREATE INDEX IF NOT EXISTS "IX_BankStatements_Account"
                ON public."BankStatements" ("Account");
            CREATE INDEX IF NOT EXISTS "IX_BankStatements_StatementDate"
                ON public."BankStatements" ("StatementDate");
            CREATE INDEX IF NOT EXISTS "IX_BankStatements_ImportDate"
                ON public."BankStatements" ("ImportDate");
            """;
        await cmd.ExecuteNonQueryAsync();

        _repository = new BankStatementImportRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task SeedAsync(params (string TransferId, string Account, string ImportResult)[] rows)
    {
        var statementDate = DateTime.UtcNow.Date.AddDays(-1);
        foreach (var (transferId, account, importResult) in rows)
        {
            var entity = new BankStatementImport(transferId, statementDate)
            {
                Account = account,
                Currency = CurrencyCode.CZK,
                ItemCount = 1,
                ImportResult = importResult,
            };
            await _repository.AddAsync(entity);
        }
    }

    [Fact]
    public async Task GetFilteredAsync_TransferIdFilter_PerformsCaseInsensitiveSubstringMatch()
    {
        // Arrange
        await SeedAsync(
            ("TRX-ALPHA-001", "ShoptetPay-CZK", "OK"),
            ("TRX-beta-002",  "ShoptetPay-CZK", "OK"),
            ("OTHER-XYZ",     "ShoptetPay-CZK", "OK"));

        // Act — lowercase query should still match uppercase stored value
        var (items, totalCount) = await _repository.GetFilteredAsync(transferId: "alpha");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "TRX-ALPHA-001");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountFilter_PerformsCaseInsensitiveSubstringMatch()
    {
        // Arrange
        await SeedAsync(
            ("T1", "ShoptetPay-CZK", "OK"),
            ("T2", "Comgate-EUR",    "OK"),
            ("T3", "shoptetpay-EUR", "OK"));

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(account: "SHOPTET");

        // Assert
        totalCount.Should().Be(2);
        items.Select(i => i.TransferId).Should().BeEquivalentTo(new[] { "T1", "T3" });
    }

    [Fact]
    public async Task GetFilteredAsync_TransferIdFilter_EscapesPercentWildcard()
    {
        // Arrange — only T1 contains the literal "%" character.
        await SeedAsync(
            ("100%-discount", "A", "OK"),
            ("100A-discount", "A", "OK"),
            ("100B-discount", "A", "OK"));

        // Act — searching for "100%" must match the literal "%" only, not "anything that starts with 100"
        var (items, totalCount) = await _repository.GetFilteredAsync(transferId: "100%");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "100%-discount");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountFilter_EscapesUnderscoreWildcard()
    {
        // Arrange — without escaping, "_" matches any single char, so all three would match "A_B".
        await SeedAsync(
            ("T1", "A_B-1", "OK"),
            ("T2", "AXB-2", "OK"),
            ("T3", "AYB-3", "OK"));

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(account: "A_B");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.Account == "A_B-1");
    }

    [Fact]
    public async Task GetFilteredAsync_CombinedTransferIdAndAccountAndErrorsOnly_AppliesAllFilters()
    {
        // Arrange
        await SeedAsync(
            ("TRX-001", "ShoptetPay-CZK", "OK"),
            ("TRX-002", "ShoptetPay-CZK", "PROCESSING_ERROR"),
            ("TRX-003", "Comgate-CZK",    "PROCESSING_ERROR"),
            ("OTHER",   "ShoptetPay-CZK", "PROCESSING_ERROR"));

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            transferId: "trx",
            account: "shoptetpay",
            errorsOnly: true);

        // Assert — only TRX-002 matches all three conditions
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "TRX-002");
    }
}
```

- [ ] **Step 2: Run the integration tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~BankStatementImportRepositoryFilterIntegrationTests"
```

Expected: all 5 tests pass. Requires Docker (or Podman) running locally — same prerequisite as the existing `MeetingTranscriptRepositorySearchIntegrationTests`. The CI environment already runs Testcontainers suites.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryFilterIntegrationTests.cs
git commit -m "test: Postgres integration tests for ILike-based bank statement filters"
```

---

## Phase 4 — Backend validator + tests

The validator is currently dead code (defined but not registered). We extend it with the new rules; registration happens in Task 11 (`BankModule`).

### Task 7: Extend `GetBankStatementListRequestValidator`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs`

- [ ] **Step 1: Replace the validator with the extended ruleset**

Overwrite `GetBankStatementListRequestValidator.cs`:

```csharp
using System.Globalization;
using FluentValidation;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class GetBankStatementListRequestValidator : AbstractValidator<GetBankStatementListRequest>
{
    private const int MaxStringFilterLength = 100;

    public GetBankStatementListRequestValidator()
    {
        RuleFor(x => x.Take)
            .GreaterThan(0)
            .WithMessage("Take must be greater than 0")
            .LessThanOrEqualTo(100)
            .WithMessage("Take must not exceed 100");

        RuleFor(x => x.Skip)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Skip must be greater than or equal to 0");

        RuleFor(x => x.TransferId)
            .MaximumLength(MaxStringFilterLength)
            .WithMessage($"TransferId must not exceed {MaxStringFilterLength} characters");

        RuleFor(x => x.Account)
            .MaximumLength(MaxStringFilterLength)
            .WithMessage($"Account must not exceed {MaxStringFilterLength} characters");

        RuleFor(x => x.DateFrom)
            .Must(BeParseableDateOrNull)
            .WithMessage("DateFrom is not a valid date");

        RuleFor(x => x.DateTo)
            .Must(BeParseableDateOrNull)
            .WithMessage("DateTo is not a valid date");

        RuleFor(x => x)
            .Must(HaveValidDateRange)
            .WithMessage("DateFrom must be on or before DateTo");
    }

    private static bool BeParseableDateOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static bool HaveValidDateRange(GetBankStatementListRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DateFrom) || string.IsNullOrWhiteSpace(request.DateTo))
            return true;

        if (!DateTime.TryParse(request.DateFrom, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
            return true; // BeParseableDateOrNull will already produce its own failure
        if (!DateTime.TryParse(request.DateTo, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            return true;

        return from.Date <= to.Date;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs
git commit -m "feat: extend GetBankStatementListRequestValidator with filter rules"
```

### Task 8: Validator unit tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListRequestValidatorTests.cs`

- [ ] **Step 1: Write the validator tests**

Write `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListRequestValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Application.Features.Bank.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class GetBankStatementListRequestValidatorTests
{
    private readonly GetBankStatementListRequestValidator _validator = new();

    [Fact]
    public void ValidRequest_WithNoFilters_PassesValidation()
    {
        var request = new GetBankStatementListRequest();

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TransferId_OverMaxLength_FailsValidation()
    {
        var request = new GetBankStatementListRequest { TransferId = new string('x', 101) };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.TransferId);
    }

    [Fact]
    public void Account_OverMaxLength_FailsValidation()
    {
        var request = new GetBankStatementListRequest { Account = new string('y', 101) };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Account);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026/99/99")]
    public void DateFrom_Unparseable_FailsValidation(string raw)
    {
        var request = new GetBankStatementListRequest { DateFrom = raw };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.DateFrom);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026/99/99")]
    public void DateTo_Unparseable_FailsValidation(string raw)
    {
        var request = new GetBankStatementListRequest { DateTo = raw };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.DateTo);
    }

    [Fact]
    public void DateFromAfterDateTo_FailsValidation()
    {
        var request = new GetBankStatementListRequest
        {
            DateFrom = "2026-06-10",
            DateTo = "2026-06-01",
        };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void DateFromEqualsDateTo_PassesValidation()
    {
        var request = new GetBankStatementListRequest
        {
            DateFrom = "2026-06-01",
            DateTo = "2026-06-01",
        };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void OnlyDateFromProvided_PassesValidation()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-06-01" };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void OnlyDateToProvided_PassesValidation()
    {
        var request = new GetBankStatementListRequest { DateTo = "2026-06-01" };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
```

- [ ] **Step 2: Run the validator tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetBankStatementListRequestValidatorTests"
```

Expected: all 11 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListRequestValidatorTests.cs
git commit -m "test: cover GetBankStatementListRequestValidator rules"
```

---

## Phase 5 — Backend handler + tests

The handler now parses the two new date strings, trims `Account`, and forwards everything to the repository. Once the validator is registered (Task 11), the handler can assume `DateFrom`/`DateTo` are either null or parseable.

### Task 9: Update `GetBankStatementListHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs`

- [ ] **Step 1: Replace the handler body**

Overwrite `GetBankStatementListHandler.cs`:

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListHandler : IRequestHandler<GetBankStatementListRequest, GetBankStatementListResponse>
{
    private readonly IBankStatementImportRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetBankStatementListHandler> _logger;

    public GetBankStatementListHandler(
        IBankStatementImportRepository repository,
        IMapper mapper,
        ILogger<GetBankStatementListHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetBankStatementListResponse> Handle(GetBankStatementListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting bank statement list with Skip={Skip}, Take={Take}", request.Skip, request.Take);

        DateTime? statementDate = ParseOptionalDate(request.StatementDate);
        DateTime? importDate = ParseOptionalDate(request.ImportDate);
        DateTime? dateFrom = ParseOptionalDate(request.DateFrom);
        DateTime? dateTo = ParseOptionalDate(request.DateTo);

        var trimmedTransferId = string.IsNullOrWhiteSpace(request.TransferId) ? null : request.TransferId.Trim();
        var trimmedAccount = string.IsNullOrWhiteSpace(request.Account) ? null : request.Account.Trim();

        var (items, totalCount) = await _repository.GetFilteredAsync(
            id: request.Id,
            statementDate: statementDate,
            importDate: importDate,
            transferId: trimmedTransferId,
            account: trimmedAccount,
            dateFrom: dateFrom,
            dateTo: dateTo,
            errorsOnly: request.ErrorsOnly,
            skip: request.Skip,
            take: request.Take,
            orderBy: request.OrderBy ?? "ImportDate",
            ascending: request.Ascending,
            cancellationToken: cancellationToken);

        var dtoList = _mapper.Map<List<BankStatementImportDto>>(items);

        _logger.LogInformation("Retrieved {Count} bank statements (total: {TotalCount})", dtoList.Count, totalCount);

        return new GetBankStatementListResponse
        {
            Items = dtoList,
            TotalCount = totalCount
        };
    }

    private static DateTime? ParseOptionalDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }
}
```

Note: `ParseOptionalDate` keeps the silent-fallback-to-null behaviour for `StatementDate`/`ImportDate` (existing semantics, preserved for backward compat) and for `DateFrom`/`DateTo` (defence in depth — the validator already rejects unparseable values with 400 before the handler runs).

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs
git commit -m "feat: forward filter parameters from handler to repository"
```

### Task 10: Handler tests with mocked repository

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs`

- [ ] **Step 1: Write the handler tests**

Write `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class GetBankStatementListHandlerTests
{
    private readonly Mock<IBankStatementImportRepository> _repo = new();
    private readonly IMapper _mapper;

    public GetBankStatementListHandlerTests()
    {
        var cfg = new MapperConfiguration(c => c.AddProfile<BankMappingProfile>());
        _mapper = cfg.CreateMapper();
    }

    private GetBankStatementListHandler CreateHandler()
    {
        _repo
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<bool?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<BankStatementImport>().AsEnumerable(), 0));

        return new GetBankStatementListHandler(_repo.Object, _mapper, NullLogger<GetBankStatementListHandler>.Instance);
    }

    [Fact]
    public async Task Handle_TrimsAccountWhitespace_BeforeRepositoryCall()
    {
        var handler = CreateHandler();
        var request = new GetBankStatementListRequest { Account = "  ShoptetPay-CZK  " };

        await handler.Handle(request, CancellationToken.None);

        _repo.Verify(r => r.GetFilteredAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<string?>(), "ShoptetPay-CZK",
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<bool?>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TrimsTransferIdWhitespace_BeforeRepositoryCall()
    {
        var handler = CreateHandler();
        var request = new GetBankStatementListRequest { TransferId = "  TRX-001  " };

        await handler.Handle(request, CancellationToken.None);

        _repo.Verify(r => r.GetFilteredAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            "TRX-001", It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<bool?>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ParsesDateFromAndDateTo_BeforeRepositoryCall()
    {
        var handler = CreateHandler();
        var request = new GetBankStatementListRequest
        {
            DateFrom = "2026-05-01",
            DateTo = "2026-05-31",
        };

        await handler.Handle(request, CancellationToken.None);

        _repo.Verify(r => r.GetFilteredAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 31),
            It.IsAny<bool?>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ForwardsErrorsOnly_ToRepository()
    {
        var handler = CreateHandler();
        var request = new GetBankStatementListRequest { ErrorsOnly = true };

        await handler.Handle(request, CancellationToken.None);

        _repo.Verify(r => r.GetFilteredAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            true,
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyStringFilters_PassedAsNullToRepository()
    {
        var handler = CreateHandler();
        var request = new GetBankStatementListRequest { TransferId = "", Account = "   " };

        await handler.Handle(request, CancellationToken.None);

        _repo.Verify(r => r.GetFilteredAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            null, null,
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<bool?>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsItemsAndTotalCount_FromRepository()
    {
        var statement = new BankStatementImport("TRX-1", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))
        {
            Account = "A",
            Currency = Anela.Heblo.Domain.Shared.CurrencyCode.CZK,
            ItemCount = 1,
            ImportResult = "OK",
        };

        _repo
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<bool?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { statement }.AsEnumerable(), 1));

        var handler = new GetBankStatementListHandler(_repo.Object, _mapper, NullLogger<GetBankStatementListHandler>.Instance);

        var response = await handler.Handle(new GetBankStatementListRequest(), CancellationToken.None);

        response.TotalCount.Should().Be(1);
        response.Items.Should().ContainSingle(i => i.TransferId == "TRX-1");
    }
}
```

- [ ] **Step 2: Run the handler tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetBankStatementListHandlerTests"
```

Expected: all 6 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs
git commit -m "test: cover handler filter forwarding and trimming"
```

---

## Phase 6 — Backend wiring: module + controller

Register the validator and pipeline behaviour for the Bank slice, then wire the controller's `[FromQuery]` parameters and 400-vs-500 exception split.

### Task 11: Register validator and `ValidationBehavior` in `BankModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs`

- [ ] **Step 1: Add validator + pipeline registration**

Overwrite `BankModule.cs`:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Application.Features.Bank.Validators;
using Anela.Heblo.Domain.Features.Bank;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Bank;

public static class BankModule
{
    public static IServiceCollection AddBankModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<IBankClientFactory, BankClientFactory>();
        services.Configure<BankAccountSettings>(configuration.GetSection(BankAccountSettings.ConfigurationKey));

        services.AddScoped<IValidator<GetBankStatementListRequest>, GetBankStatementListRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetBankStatementListRequest, GetBankStatementListResponse>,
            ValidationBehavior<GetBankStatementListRequest, GetBankStatementListResponse>>();

        return services;
    }
}
```

This mirrors `PackagingModule.AddPackagingModule` (the established codebase pattern for per-slice validator wiring).

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs
git commit -m "feat: register GetBankStatementListRequest validator and pipeline behavior"
```

### Task 12: Update `BankStatementsController.GetBankStatements`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`

- [ ] **Step 1: Add new `[FromQuery]` parameters and split 400-vs-500 exception handling**

Locate `GetBankStatements` (around line 94) and replace it with the version below. Leave `GetAccounts`, `ImportStatements`, and `GetBankStatement(int id)` unchanged.

```csharp
    /// <summary>
    /// Get list of bank statement imports with optional filtering and pagination
    /// </summary>
    /// <param name="id">Filter by import ID</param>
    /// <param name="statementDate">Filter by statement date (exact match)</param>
    /// <param name="importDate">Filter by import date (exact match)</param>
    /// <param name="transferId">Case-insensitive substring match on Transfer ID</param>
    /// <param name="account">Case-insensitive substring match on bank account</param>
    /// <param name="dateFrom">Inclusive lower bound on statement date (ISO date)</param>
    /// <param name="dateTo">Inclusive upper bound on statement date (ISO date)</param>
    /// <param name="errorsOnly">When true, restrict to imports whose result is not "OK"</param>
    /// <param name="skip">Number of records to skip (default: 0)</param>
    /// <param name="take">Number of records to take (default: 10, max: 100)</param>
    /// <param name="orderBy">Order by field (default: ImportDate)</param>
    /// <param name="ascending">Sort direction (default: false)</param>
    [HttpGet]
    public async Task<ActionResult<GetBankStatementListResponse>> GetBankStatements(
        [FromQuery] int? id = null,
        [FromQuery] string? statementDate = null,
        [FromQuery] string? importDate = null,
        [FromQuery] string? transferId = null,
        [FromQuery] string? account = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] bool? errorsOnly = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        [FromQuery] string? orderBy = "ImportDate",
        [FromQuery] bool ascending = false)
    {
        try
        {
            _logger.LogInformation("Getting bank statements with Skip={Skip}, Take={Take}", skip, take);

            var request = new GetBankStatementListRequest
            {
                Id = id,
                StatementDate = statementDate,
                ImportDate = importDate,
                TransferId = transferId,
                Account = account,
                DateFrom = dateFrom,
                DateTo = dateTo,
                ErrorsOnly = errorsOnly,
                Skip = skip,
                Take = take,
                OrderBy = orderBy,
                Ascending = ascending
            };

            var response = await _mediator.Send(request);
            return Ok(response);
        }
        catch (FluentValidation.ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for bank statement list request");
            return BadRequest(new
            {
                message = "Invalid request",
                errors = ex.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving bank statements");
            return StatusCode(500, new { message = "An error occurred while retrieving bank statements" });
        }
    }
```

The `FluentValidation.ValidationException` catch must come **before** the catch-all `Exception` handler — order matters for `try`/`catch`.

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: builds clean.

- [ ] **Step 3: Run all Bank backend tests to confirm no regression**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Features.Bank"
```

Expected: all Bank tests pass (existing repository tests, new filter tests, validator tests, handler tests, and integration tests).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
git commit -m "feat: accept filter query params in GetBankStatements; 400 on validation failure"
```

---

## Phase 7 — Backend final validation

### Task 13: `dotnet format` and full test sweep

- [ ] **Step 1: Apply formatting**

```bash
dotnet format backend/Anela.Heblo.sln
```

- [ ] **Step 2: Run the full backend test suite (Bank slice + dependents)**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Features.Bank|FullyQualifiedName~Packaging"
```

Expected: every test passes. The `Packaging` filter catches accidental regressions from the `LikeEscape` extraction in Task 1.

- [ ] **Step 3: Commit any format-only changes (if `dotnet format` modified files)**

```bash
git status
# If files were modified:
git add -u
git commit -m "chore: dotnet format"
```

---

## Phase 8 — Frontend: hook contract

The hand-rolled `useBankStatementsList` is the wire-contract surface; we extend the request interface and the URL builder. The generated OpenAPI client picks up the new fields automatically next build, but this hook does NOT consume those types — it constructs the URL by hand, so the manual extension is load-bearing.

### Task 14: Extend `GetBankStatementListRequest` and `useBankStatementsList`

**Files:**
- Modify: `frontend/src/api/hooks/useBankStatements.ts`

- [ ] **Step 1: Extend the request interface**

In `frontend/src/api/hooks/useBankStatements.ts`, replace the `GetBankStatementListRequest` interface (currently at lines 40-48) with:

```typescript
export interface GetBankStatementListRequest {
  id?: number;
  statementDate?: string;
  importDate?: string;
  transferId?: string;
  account?: string;
  dateFrom?: string;
  dateTo?: string;
  errorsOnly?: boolean;
  skip?: number;
  take?: number;
  orderBy?: string;
  ascending?: boolean;
}
```

- [ ] **Step 2: Extend the URL builder inside `useBankStatementsList`**

In the same file, inside the `useBankStatementsList` `queryFn`, locate the `URLSearchParams` block (currently lines 97-118). Add five new conditional `params.append` calls immediately after the existing `importDate` block (after line 106) so the new fields are forwarded:

```typescript
      if (request.transferId) {
        params.append('transferId', request.transferId);
      }
      if (request.account) {
        params.append('account', request.account);
      }
      if (request.dateFrom) {
        params.append('dateFrom', request.dateFrom);
      }
      if (request.dateTo) {
        params.append('dateTo', request.dateTo);
      }
      if (request.errorsOnly) {
        params.append('errorsOnly', 'true');
      }
```

Place them between the existing `if (request.importDate) { ... }` block and the `if (request.skip !== undefined) { ... }` block. This preserves the existing parameter order for the wire so curl/log diffs of the request URL stay readable.

Note on `errorsOnly`: appending only when truthy keeps the unfiltered request identical to today's (no `errorsOnly=false` noise) — matches the spec's "checkbox unchecked → no constraint".

- [ ] **Step 3: Verify type-check and lint**

```bash
cd frontend && npm run lint && npx tsc --noEmit
```

Expected: no errors. The file's other consumers (`ImportTab`, `BankStatementImportJobTracker`, etc.) still type-check because every new field is optional.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useBankStatements.ts
git commit -m "feat: forward filter params from useBankStatementsList"
```

---

## Phase 9 — Frontend: `ImportTab.tsx`

Collapse the half-implemented committed-filter state into a single object, pass it to the hook, and add the `dateFrom > dateTo` guard. Update the pagination "(filtrováno)" indicator to consider all five filters.

### Task 15: Refactor `ImportTab.tsx` to commit filters as a single object

**Files:**
- Modify: `frontend/src/components/customer/tabs/ImportTab.tsx`

- [ ] **Step 1: Replace the filter/pagination/hook-wiring section at the top of the component**

In `ImportTab.tsx`, replace lines 24-94 (everything from `// Filter states` through the closing `};` of `handleClearFilters`) with the following block. Do NOT touch the JSX below `// Sorting handler` — the input bindings stay the same.

```typescript
  // Filter input state (live, drives the controlled inputs)
  const [transferIdInput, setTransferIdInput] = useState("");
  const [accountInput, setAccountInput] = useState("");
  const [statementDateFromInput, setStatementDateFromInput] = useState("");
  const [statementDateToInput, setStatementDateToInput] = useState("");
  const [showOnlyErrorsInput, setShowOnlyErrorsInput] = useState(false);

  // Committed filter state — only mutated by Filtrovat / Vyčistit, drives the query
  type CommittedFilters = {
    transferId: string;
    account: string;
    dateFrom: string;
    dateTo: string;
    errorsOnly: boolean;
  };

  const emptyFilters: CommittedFilters = {
    transferId: "",
    account: "",
    dateFrom: "",
    dateTo: "",
    errorsOnly: false,
  };

  const [committedFilters, setCommittedFilters] = useState<CommittedFilters>(emptyFilters);

  // Date-range validation error (cleared whenever inputs change)
  const [dateRangeError, setDateRangeError] = useState<string | null>(null);

  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Sorting states - default to ImportDate descending
  const [sortBy, setSortBy] = useState("ImportDate");
  const [sortDescending, setSortDescending] = useState(true);

  // Import modal states
  const [showImportModal, setShowImportModal] = useState(false);
  const [selectedAccount, setSelectedAccount] = useState("");
  const [importDate, setImportDate] = useState("");
  const [isImporting, setIsImporting] = useState(false);

  // Use the actual API call for data
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useBankStatementsList({
    skip: (pageNumber - 1) * pageSize,
    take: pageSize,
    orderBy: sortBy,
    ascending: !sortDescending,
    transferId: committedFilters.transferId || undefined,
    account: committedFilters.account || undefined,
    dateFrom: committedFilters.dateFrom || undefined,
    dateTo: committedFilters.dateTo || undefined,
    errorsOnly: committedFilters.errorsOnly || undefined,
  });

  // Import related hooks
  const importMutation = useBankStatementImport();
  const { data: accounts, isLoading: accountsLoading } = useBankStatementAccounts();

  const filteredItems = data?.items || [];
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / pageSize);

  const hasActiveFilters =
    committedFilters.transferId !== "" ||
    committedFilters.account !== "" ||
    committedFilters.dateFrom !== "" ||
    committedFilters.dateTo !== "" ||
    committedFilters.errorsOnly;

  // Handler for applying filters
  const handleApplyFilters = () => {
    if (
      statementDateFromInput !== "" &&
      statementDateToInput !== "" &&
      statementDateFromInput > statementDateToInput
    ) {
      setDateRangeError('"Od" musí být dříve nebo stejně jako "Do".');
      return;
    }

    setDateRangeError(null);
    setCommittedFilters({
      transferId: transferIdInput.trim(),
      account: accountInput.trim(),
      dateFrom: statementDateFromInput,
      dateTo: statementDateToInput,
      errorsOnly: showOnlyErrorsInput,
    });
    setPageNumber(1);
  };

  // Handler for Enter key press
  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  // Handler for clearing all filters
  const handleClearFilters = () => {
    setTransferIdInput("");
    setAccountInput("");
    setStatementDateFromInput("");
    setStatementDateToInput("");
    setShowOnlyErrorsInput(false);
    setDateRangeError(null);
    setCommittedFilters(emptyFilters);
    setPageNumber(1);
  };
```

- [ ] **Step 2: Update the date input bindings to use the new state names**

Still in `ImportTab.tsx`, locate the two `<input type="date">` elements (currently around lines 332-345). They reference `statementDateFrom` / `statementDateTo`. Update them to use the new state names AND insert a `dateRangeError` display block immediately below the closing `</div>` of the date inputs.

Replace the existing block:

```tsx
            <div className="flex gap-2">
              <input
                type="date"
                value={statementDateFrom}
                onChange={(e) => setStatementDateFrom(e.target.value)}
                className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md"
                placeholder="Od"
              />
              <input
                type="date"
                value={statementDateTo}
                onChange={(e) => setStatementDateTo(e.target.value)}
                className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md"
                placeholder="Do"
              />
            </div>
```

with:

```tsx
            <div className="flex gap-2">
              <input
                type="date"
                value={statementDateFromInput}
                onChange={(e) => {
                  setStatementDateFromInput(e.target.value);
                  setDateRangeError(null);
                }}
                className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md"
                placeholder="Od"
              />
              <input
                type="date"
                value={statementDateToInput}
                onChange={(e) => {
                  setStatementDateToInput(e.target.value);
                  setDateRangeError(null);
                }}
                className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md"
                placeholder="Do"
              />
            </div>
```

Then update the "Jen chyby" checkbox (currently around lines 348-358):

```tsx
            <div className="flex gap-2">
              <label className="flex items-center text-sm">
                <input
                  type="checkbox"
                  checked={showOnlyErrors}
                  onChange={(e) => setShowOnlyErrors(e.target.checked)}
                  className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
                />
                <span className="ml-1 text-gray-700">Jen chyby</span>
              </label>
            </div>
```

to:

```tsx
            <div className="flex gap-2">
              <label className="flex items-center text-sm">
                <input
                  type="checkbox"
                  checked={showOnlyErrorsInput}
                  onChange={(e) => setShowOnlyErrorsInput(e.target.checked)}
                  className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
                />
                <span className="ml-1 text-gray-700">Jen chyby</span>
              </label>
            </div>
```

Add the inline date-range error display **immediately after** the closing `</div>` of the entire filters row (the outer flex container around line 359). Since the existing filter row uses `flex-wrap`, add the error as a full-width row below by inserting this block right before `<div className="flex items-center gap-2">` (the Filtrovat/Vyčistit buttons block):

```tsx
            {dateRangeError && (
              <div className="w-full text-sm text-red-600" role="alert">
                {dateRangeError}
              </div>
            )}
```

- [ ] **Step 3: Update the pagination "(filtrováno)" indicator to consider all five filters**

Locate (currently around line 484):

```tsx
                  {transferIdFilter || accountFilter ? (
                    <span className="text-gray-500"> (filtrováno)</span>
                  ) : (
                    ""
                  )}
```

Replace with:

```tsx
                  {hasActiveFilters ? (
                    <span className="text-gray-500"> (filtrováno)</span>
                  ) : (
                    ""
                  )}
```

- [ ] **Step 4: Verify type-check and lint**

```bash
cd frontend && npm run lint && npx tsc --noEmit
```

Expected: no errors. If the linter complains about unused `refetch`, ignore — it's still used by the import modal flow (`await refetch();` inside `handleImportSubmit`, around line 137).

- [ ] **Step 5: Verify build**

```bash
cd frontend && npm run build
```

Expected: clean build.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/customer/tabs/ImportTab.tsx
git commit -m "feat: wire bank import filters end-to-end with date-range guard"
```

---

## Phase 10 — Frontend tests

### Task 16: Component tests for `ImportTab` filter wiring

**Files:**
- Create: `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx`

- [ ] **Step 1: Write the component tests**

Write `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import ImportTab from "../ImportTab";

const mockUseBankStatementsList = jest.fn();
const mockUseBankStatementImport = jest.fn();
const mockUseBankStatementAccounts = jest.fn();

jest.mock("../../../../api/hooks/useBankStatements", () => ({
  useBankStatementsList: (req: any) => mockUseBankStatementsList(req),
  useBankStatementImport: () => mockUseBankStatementImport(),
  useBankStatementAccounts: () => mockUseBankStatementAccounts(),
}));

describe("ImportTab filter wiring", () => {
  beforeEach(() => {
    mockUseBankStatementsList.mockReset();
    mockUseBankStatementImport.mockReset();
    mockUseBankStatementAccounts.mockReset();

    mockUseBankStatementsList.mockReturnValue({
      data: { items: [], totalCount: 0 },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    });
    mockUseBankStatementImport.mockReturnValue({ mutateAsync: jest.fn(), isLoading: false });
    mockUseBankStatementAccounts.mockReturnValue({ data: [], isLoading: false });
  });

  function getLatestRequest() {
    const calls = mockUseBankStatementsList.mock.calls;
    return calls[calls.length - 1][0];
  }

  it("initial render does not include any filter params in the hook request", () => {
    render(<ImportTab />);

    const req = getLatestRequest();
    expect(req.transferId).toBeUndefined();
    expect(req.account).toBeUndefined();
    expect(req.dateFrom).toBeUndefined();
    expect(req.dateTo).toBeUndefined();
    expect(req.errorsOnly).toBeUndefined();
  });

  it("typing into inputs does NOT trigger filtered request (no auto-search)", () => {
    render(<ImportTab />);

    fireEvent.change(screen.getByPlaceholderText("Transfer ID..."), {
      target: { value: "TRX-1" },
    });

    const req = getLatestRequest();
    expect(req.transferId).toBeUndefined();
  });

  it("clicking Filtrovat sends trimmed transferId/account, date range, and errorsOnly", async () => {
    render(<ImportTab />);

    fireEvent.change(screen.getByPlaceholderText("Transfer ID..."), {
      target: { value: "  TRX-1  " },
    });
    fireEvent.change(screen.getByPlaceholderText("Účet..."), {
      target: { value: "  ShoptetPay-CZK  " },
    });
    const [fromInput, toInput] = screen.getAllByDisplayValue("");
    fireEvent.change(fromInput, { target: { value: "2026-05-01" } });
    fireEvent.change(toInput, { target: { value: "2026-05-31" } });
    fireEvent.click(screen.getByLabelText(/Jen chyby/));

    fireEvent.click(screen.getByRole("button", { name: "Filtrovat" }));

    await waitFor(() => {
      const req = getLatestRequest();
      expect(req.transferId).toBe("TRX-1");
      expect(req.account).toBe("ShoptetPay-CZK");
      expect(req.dateFrom).toBe("2026-05-01");
      expect(req.dateTo).toBe("2026-05-31");
      expect(req.errorsOnly).toBe(true);
    });
  });

  it("clicking Filtrovat resets pagination to page 1 (skip = 0)", async () => {
    render(<ImportTab />);

    fireEvent.change(screen.getByPlaceholderText("Transfer ID..."), {
      target: { value: "TRX-1" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Filtrovat" }));

    await waitFor(() => {
      const req = getLatestRequest();
      expect(req.skip).toBe(0);
    });
  });

  it("dateFrom > dateTo blocks submission and shows inline error", () => {
    render(<ImportTab />);

    const [fromInput, toInput] = screen.getAllByDisplayValue("");
    fireEvent.change(fromInput, { target: { value: "2026-05-31" } });
    fireEvent.change(toInput, { target: { value: "2026-05-01" } });

    fireEvent.click(screen.getByRole("button", { name: "Filtrovat" }));

    expect(screen.getByRole("alert")).toHaveTextContent(/musí být dříve/i);
    const req = getLatestRequest();
    expect(req.dateFrom).toBeUndefined();
    expect(req.dateTo).toBeUndefined();
  });

  it("clicking Vymazat resets all filter inputs and committed filters", async () => {
    render(<ImportTab />);

    fireEvent.change(screen.getByPlaceholderText("Transfer ID..."), {
      target: { value: "TRX-1" },
    });
    fireEvent.click(screen.getByLabelText(/Jen chyby/));
    fireEvent.click(screen.getByRole("button", { name: "Filtrovat" }));

    await waitFor(() => {
      expect(getLatestRequest().transferId).toBe("TRX-1");
    });

    fireEvent.click(screen.getByRole("button", { name: "Vymazat" }));

    await waitFor(() => {
      const req = getLatestRequest();
      expect(req.transferId).toBeUndefined();
      expect(req.errorsOnly).toBeUndefined();
      expect(req.skip).toBe(0);
    });
    expect((screen.getByPlaceholderText("Transfer ID...") as HTMLInputElement).value).toBe("");
    expect((screen.getByLabelText(/Jen chyby/) as HTMLInputElement).checked).toBe(false);
  });
});
```

Note: the existing component uses the Czech label "Vymazat" for the clear button (`ImportTab.tsx:372`), not "Vyčistit" as the spec implies — tests match the actual UI string. If implementing this change includes also renaming the button to match the spec, update both `ImportTab.tsx` and the test in the same commit.

- [ ] **Step 2: Run the new component tests**

```bash
cd frontend && npx jest --testPathPattern="ImportTab.test"
```

Expected: all 6 tests pass.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx
git commit -m "test: cover ImportTab filter submission and date-range guard"
```

---

## Phase 11 — Manual smoke + final validation

### Task 17: Manual UI smoke test

- [ ] **Step 1: Start backend**

```bash
cd backend/src/Anela.Heblo.API && dotnet run
```

- [ ] **Step 2: Start frontend (separate terminal)**

```bash
cd frontend && npm start
```

- [ ] **Step 3: Smoke through each filter in the browser**

Navigate to the Bank module → Import tab. Verify each scenario:

| Action | Expected result |
|---|---|
| Type `TRX` into Transfer ID, click Filtrovat | Only statements whose Transfer ID contains "TRX" (case-insensitive) appear; "(filtrováno)" shows in the pagination footer. |
| Clear, type lowercase `shoptet` into Účet, click Filtrovat | Statements whose account contains "shoptet" case-insensitively appear. |
| Clear, set Od = today-30, Do = today-1, click Filtrovat | Only statements with `statementDate` in that inclusive window appear. |
| Set Od = today, Do = today-30 (invalid order), click Filtrovat | Inline red error appears; no request goes out (verify via Network tab — no `/api/bank-statements?...dateFrom=...` request fires). |
| Clear, check "Jen chyby", click Filtrovat | Only rows where the "Stav importu" badge is red appear. |
| Combine three filters (e.g. transferId + account + errorsOnly), click Filtrovat | AND-combined result set. |
| Click Vymazat | All inputs blank, "(filtrováno)" gone, full unfiltered list returns at page 1. |

- [ ] **Step 4: Verify the Network tab shows correct query params**

Open browser DevTools → Network. After clicking Filtrovat, inspect the `/api/bank-statements?...` request and confirm the query string contains the expected params (and only them — no `errorsOnly=false`, no empty values).

- [ ] **Step 5: Stop both processes**

### Task 18: Final repo-wide validation

- [ ] **Step 1: Full backend build and test**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: full suite passes.

- [ ] **Step 2: Full frontend build and lint**

```bash
cd frontend && npm run lint && npm run build && npm test -- --watchAll=false
```

Expected: lint clean; build succeeds; jest suite passes.

- [ ] **Step 3: Confirm OpenAPI client regenerates (optional sanity check)**

Per `docs/development/api-client-generation.md`, the TypeScript client is regenerated on build. Confirm the generated client picked up the five new query parameters (they're optional, so this is non-breaking):

```bash
cd frontend && grep -n "transferId\|errorsOnly" src/api/generated/*.ts | head
```

Expected: at least one match referencing the new params on the `bankStatementsGet` (or analogous) signature. If the generated file already exists and the build refreshed it, you should see the new fields. The hand-rolled hook does not consume these types directly, so divergence does not block — but a match here is the cheapest way to confirm the wire contract is in sync.

- [ ] **Step 4: No commit needed (validation only)**

---

## Out of scope (do not implement)

- New filter dimensions beyond the four already in the UI (no user/amount/error-subtype filters).
- Persisting filter selections (no URL query string, no localStorage, no saved presets).
- Empty-state copy that distinguishes "no imports yet" vs "no matches" (the spec defers this).
- Index tuning on `TransferId` / `StatementDate` for `%substring%` (only revisit if production p95 regresses).
- E2E coverage — relies on BE + FE unit/integration tests per spec NFR-4.
- Deduplicating `ErrorType` between `BankStatementImportDto` and `BankMappingProfile` (flagged in arch review as a separate cleanup).
- Introducing `DateOnly` anywhere in the Bank module.

---

## Risk reminders

- **Order matters in `try`/`catch` (Task 12):** `FluentValidation.ValidationException` catch must precede the catch-all `Exception` catch, or every 400 becomes a 500.
- **`DateTime.SpecifyKind(..., Utc)` (Task 4):** Removing this will cause Npgsql to reject the date bounds at runtime against the production `timestamp without time zone` column with mismatched kinds; InMemory tests will still pass and hide the regression.
- **Testcontainers requires Docker/Podman (Task 6):** locally and in CI. The existing `MeetingTranscriptRepositorySearchIntegrationTests` proves CI is configured; locally, ensure Docker Desktop / Podman Desktop is running before running the integration suite.
- **Validator registration is mandatory (Task 11):** Without the `IValidator<>` and `IPipelineBehavior<,>` registrations, the validator is dead code, the new 400 paths become 200s, and the negative tests in Task 18 will silently pass against a no-op pipeline.
