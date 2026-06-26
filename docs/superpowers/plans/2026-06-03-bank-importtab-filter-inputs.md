# Bank Import Tab Filter Inputs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the four filter controls on the Bank Import tab (Transfer ID, Account, statement-date range, errors-only) end-to-end so clicking "Filtrovat" actually constrains the bank-statement list. Filters are applied server-side (EF `Where` clauses against existing columns); no schema changes.

**Architecture:** Extend the existing `GetBankStatementListRequest` MediatR query with five optional properties (`transferId`, `account`, `dateFrom`, `dateTo`, `errorsOnly`). Introduce a `BankStatementListFilter` domain record so the repository's `GetFilteredAsync` signature stays maintainable as criteria grow. Use `EF.Functions.ILike` with the `EscapeLike` helper pattern already established in `PackageRepository` for case-insensitive substring matching on `TransferId` and `Account`. On the frontend, replace the no-op `refetch()` model with a "committed filters" object that React Query keys on, so "Filtrovat" / "Vyčistit" are the only triggers for a new fetch.

**Tech Stack:** .NET 8, MediatR, EF Core (PostgreSQL via Npgsql), FluentValidation, xUnit + FluentAssertions + Testcontainers (Postgres) for ILike coverage. React + TypeScript, React Query, Jest + React Testing Library.

---

## File Structure

**Backend — new files:**
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs` — domain record packaging all filter criteria.
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryIntegrationTests.cs` — Testcontainers Postgres test covering ILike substring matching + wildcard escaping for `TransferId` / `Account`.
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` — handler-level tests covering `DateTime.TryParse` paths and validator-driven 400 paths.

**Backend — modified files:**
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — replace positional optional filter params with `BankStatementListFilter`; add `CancellationToken`.
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — accept filter record; implement `EscapeLike` helper; add five new `Where` clauses; thread `CancellationToken` through.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — add `TransferId`, `Account`, `DateFrom`, `DateTo`, `ErrorsOnly` properties (DTO, class).
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — parse `DateFrom`/`DateTo` strings, trim `TransferId`/`Account`, build `BankStatementListFilter`, pass through.
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — add length, parseable-date, and `DateFrom <= DateTo` rules.
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` — register `GetBankStatementListRequestValidator` and `ValidationBehavior` pipeline (prerequisite — currently not wired).
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — add five new `[FromQuery]` parameters; catch `FluentValidation.ValidationException` → 400; update XML docs.
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` — update to new filter-record signature; add unit tests for InMemory-safe filters (`DateFrom`/`DateTo`/`ErrorsOnly`/combined).

**Frontend — new files:**
- `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx` — component test asserting `useBankStatementsList` is called with the expected committed-filter payload on apply/clear and that the `dateFrom > dateTo` guard blocks submission.

**Frontend — modified files:**
- `frontend/src/api/hooks/useBankStatements.ts` — extend `GetBankStatementListRequest` with five new optional fields; serialise non-empty/defined values into `URLSearchParams`.
- `frontend/src/components/customer/tabs/ImportTab.tsx` — collapse `transferIdFilter`/`accountFilter` into a single `committedFilters` state object covering all five fields; trim & validate before commit; update "(filtrováno)" indicator condition.

No schema migrations. No new packages. The OpenAPI TypeScript client (`frontend/src/api/generated/api-client.ts`) regenerates automatically on backend Debug build — no manual edit.

---

## Task 1: Wire FluentValidation pipeline for `GetBankStatementListRequest`

The existing `GetBankStatementListRequestValidator` is not registered with DI, and `BankModule.cs` does not install a `ValidationBehavior` for the request. This must be done first so the new validation rules added in Task 10 actually run. The controller does not currently translate `FluentValidation.ValidationException` to HTTP 400, so we also add that mapping.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` (GetBankStatements action only, surgical)

- [ ] **Step 1: Update `BankModule.cs` to register validator and pipeline behavior**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` with:

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

- [ ] **Step 2: Add `catch (ValidationException)` to the `GetBankStatements` action**

In `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`, add the `using FluentValidation;` directive at the top, and modify the `try`/`catch` block in `GetBankStatements` (currently ending around line 121) to add a specific catch for `FluentValidation.ValidationException` before the generic `Exception`:

Before:
```csharp
        try
        {
            _logger.LogInformation("Getting bank statements with Skip={Skip}, Take={Take}", skip, take);
            // ... build request, mediator.Send
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving bank statements");
            return StatusCode(500, new { message = "An error occurred while retrieving bank statements" });
        }
```

After:
```csharp
        try
        {
            _logger.LogInformation("Getting bank statements with Skip={Skip}, Take={Take}", skip, take);
            // ... build request, mediator.Send
            return Ok(response);
        }
        catch (FluentValidation.ValidationException ex)
        {
            _logger.LogWarning(ex, "Invalid request for bank statement list");
            return BadRequest(new { message = "Invalid request", errors = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving bank statements");
            return StatusCode(500, new { message = "An error occurred while retrieving bank statements" });
        }
```

- [ ] **Step 3: Verify the solution still builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED with zero new warnings.

- [ ] **Step 4: Run the existing Bank repository test suite to confirm nothing regressed**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Bank" --no-build`
Expected: all existing Bank-feature tests still pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs \
        backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
git commit -m "chore(bank): wire FluentValidation pipeline for GetBankStatementListRequest"
```

---

## Task 2: Introduce `BankStatementListFilter` domain record

Package all repository filter criteria into one record so the `GetFilteredAsync` signature does not grow to 10+ positional parameters.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs`

- [ ] **Step 1: Create the filter record**

Create `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs` with:

```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public sealed record BankStatementListFilter(
    int? Id = null,
    string? TransferId = null,
    string? Account = null,
    DateTime? StatementDate = null,
    DateTime? ImportDate = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    bool? ErrorsOnly = null);
```

This is a domain-layer record (it does not cross the OpenAPI generator), so the project rule "DTOs are classes, never records" does not apply.

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs
git commit -m "feat(bank): add BankStatementListFilter domain record"
```

---

## Task 3: Change `IBankStatementImportRepository.GetFilteredAsync` signature

Replace the positional optional parameters with a `BankStatementListFilter` argument plus a `CancellationToken`. Update the implementation, the handler, and the existing tests so the behaviour of the *existing* filters is preserved.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs`

- [ ] **Step 1: Update the interface**

Replace the contents of `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` with:

```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankStatementImportRepository
{
    Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
        BankStatementListFilter filter,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false,
        CancellationToken cancellationToken = default);

    Task<BankStatementImport?> GetByIdAsync(int id);
    Task<BankStatementImport> AddAsync(BankStatementImport bankStatement);
}
```

- [ ] **Step 2: Update the repository implementation**

Replace the contents of `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` with:

```csharp
using Anela.Heblo.Domain.Features.Bank;
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
        BankStatementListFilter filter,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _context.BankStatements.AsNoTracking().AsQueryable();

        if (filter.Id.HasValue)
            query = query.Where(bs => bs.Id == filter.Id.Value);

        if (filter.StatementDate.HasValue)
            query = query.Where(bs => bs.StatementDate.Date == filter.StatementDate.Value.Date);

        if (filter.ImportDate.HasValue)
            query = query.Where(bs => bs.ImportDate.Date == filter.ImportDate.Value.Date);

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

This compiles but does **not** yet implement the new filter clauses — those land in Tasks 4–7.

- [ ] **Step 3: Update the handler to construct a `BankStatementListFilter`**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` with:

```csharp
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

        DateTime? statementDate = null;
        if (!string.IsNullOrEmpty(request.StatementDate) && DateTime.TryParse(request.StatementDate, out var parsedStatementDate))
        {
            statementDate = parsedStatementDate;
        }

        DateTime? importDate = null;
        if (!string.IsNullOrEmpty(request.ImportDate) && DateTime.TryParse(request.ImportDate, out var parsedImportDate))
        {
            importDate = parsedImportDate;
        }

        var filter = new BankStatementListFilter(
            Id: request.Id,
            StatementDate: statementDate,
            ImportDate: importDate);

        var (items, totalCount) = await _repository.GetFilteredAsync(
            filter,
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
}
```

This intentionally still does **not** wire `TransferId`/`Account`/`DateFrom`/`DateTo`/`ErrorsOnly` — those are wired in Task 9 after the request DTO is extended.

- [ ] **Step 4: Update existing repository tests to the new signature**

In `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs`, replace every call to `_repository.GetFilteredAsync(...)` so it now passes a `BankStatementListFilter`. Apply these targeted substitutions:

- `await _repository.GetFilteredAsync();` → `await _repository.GetFilteredAsync(new BankStatementListFilter());`
- `await _repository.GetFilteredAsync(statementDate: targetDate);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(StatementDate: targetDate));`
- `await _repository.GetFilteredAsync(skip: 2, take: 2);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(), skip: 2, take: 2);`
- `await _repository.GetFilteredAsync(orderBy: "ImportDate", ascending: true);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(), orderBy: "ImportDate", ascending: true);`
- `await _repository.GetFilteredAsync(id: saved1.Id);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(Id: saved1.Id));`
- `await _repository.GetFilteredAsync(importDate: targetImportDate);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(ImportDate: targetImportDate));`
- `await _repository.GetFilteredAsync(orderBy: "StatementDate", ascending: true);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(), orderBy: "StatementDate", ascending: true);`
- `await _repository.GetFilteredAsync(orderBy: "Id", ascending: true);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(), orderBy: "Id", ascending: true);`
- `await _repository.GetFilteredAsync(orderBy: "InvalidColumn", ascending: true);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(), orderBy: "InvalidColumn", ascending: true);`
- `await _repository.GetFilteredAsync(id: saved1.Id, statementDate: targetDate);` → `await _repository.GetFilteredAsync(new BankStatementListFilter(Id: saved1.Id, StatementDate: targetDate));`

No new test cases yet — this step preserves behavior only.

- [ ] **Step 5: Run repository tests to confirm green**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BankStatementImportRepositoryTests" --no-restore`
Expected: all existing tests pass.

- [ ] **Step 6: Build the full solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED with zero new warnings.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs \
        backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs
git commit -m "refactor(bank): replace GetFilteredAsync positional params with BankStatementListFilter record"
```

---

## Task 4: Repository — `ErrorsOnly` filter (InMemory test, no ILike)

The cheapest filter to wire first: a single equality `Where` clause using the `ImportStatus.Success` constant. Fully testable against the existing InMemory provider.

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`

- [ ] **Step 1: Write the failing test**

Append to `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` (inside the test class, alongside the existing `[Fact]` methods):

```csharp
[Fact]
public async Task GetFilteredAsync_WithErrorsOnly_ReturnsOnlyNonOkImports()
{
    // Arrange
    var ok = CreateTestImport("T-OK", DateTime.UtcNow.Date, "ACC", CurrencyCode.CZK);
    ok.ImportResult = ImportStatus.Success;
    var failed = CreateTestImport("T-FAIL", DateTime.UtcNow.Date, "ACC", CurrencyCode.CZK);
    failed.ImportResult = ImportStatus.ProcessingError;

    await _repository.AddAsync(ok);
    await _repository.AddAsync(failed);

    // Act
    var (items, totalCount) = await _repository.GetFilteredAsync(
        new BankStatementListFilter(ErrorsOnly: true));

    // Assert
    Assert.Equal(1, totalCount);
    Assert.Single(items);
    Assert.Equal("T-FAIL", items.First().TransferId);
}

[Fact]
public async Task GetFilteredAsync_WithErrorsOnlyFalseOrNull_ReturnsAll()
{
    // Arrange
    var ok = CreateTestImport("T-OK", DateTime.UtcNow.Date, "ACC", CurrencyCode.CZK);
    ok.ImportResult = ImportStatus.Success;
    var failed = CreateTestImport("T-FAIL", DateTime.UtcNow.Date, "ACC", CurrencyCode.CZK);
    failed.ImportResult = ImportStatus.ProcessingError;

    await _repository.AddAsync(ok);
    await _repository.AddAsync(failed);

    // Act — null
    var (itemsNull, totalNull) = await _repository.GetFilteredAsync(
        new BankStatementListFilter(ErrorsOnly: null));
    // Act — false
    var (itemsFalse, totalFalse) = await _repository.GetFilteredAsync(
        new BankStatementListFilter(ErrorsOnly: false));

    // Assert
    Assert.Equal(2, totalNull);
    Assert.Equal(2, totalFalse);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetFilteredAsync_WithErrorsOnly" --no-restore`
Expected: FAIL — the filter clause is not yet implemented; `totalCount` will be 2 instead of 1.

- [ ] **Step 3: Implement the `ErrorsOnly` clause**

In `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`, inside `GetFilteredAsync`, after the existing `if (filter.ImportDate.HasValue)` block and before `var totalCount = await query.CountAsync(...)`, add:

```csharp
        if (filter.ErrorsOnly == true)
            query = query.Where(bs => bs.ImportResult != ImportStatus.Success);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetFilteredAsync_WithErrorsOnly" --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs
git commit -m "feat(bank): repository ErrorsOnly filter via ImportStatus.Success constant"
```

---

## Task 5: Repository — `DateFrom` / `DateTo` range filter (InMemory testable)

Implement the inclusive day-granularity range filter on `StatementDate`. The existing handler convention already parses `string?` query params into `DateTime?` — same convention applies here (Task 9). The repository takes pre-parsed `DateTime?` values.

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`

- [ ] **Step 1: Write the failing tests**

Append to the test class:

```csharp
[Fact]
public async Task GetFilteredAsync_WithDateFromOnly_ReturnsImportsOnOrAfterDate()
{
    // Arrange
    var twoDaysAgo = DateTime.UtcNow.Date.AddDays(-2);
    var yesterday = DateTime.UtcNow.Date.AddDays(-1);
    var today = DateTime.UtcNow.Date;
    await _repository.AddAsync(CreateTestImport("T1", twoDaysAgo, "ACC", CurrencyCode.CZK));
    await _repository.AddAsync(CreateTestImport("T2", yesterday, "ACC", CurrencyCode.CZK));
    await _repository.AddAsync(CreateTestImport("T3", today, "ACC", CurrencyCode.CZK));

    // Act — DateFrom = yesterday should include yesterday & today, exclude two-days-ago
    var (items, totalCount) = await _repository.GetFilteredAsync(
        new BankStatementListFilter(DateFrom: yesterday));

    // Assert
    Assert.Equal(2, totalCount);
    Assert.DoesNotContain(items, i => i.TransferId == "T1");
    Assert.Contains(items, i => i.TransferId == "T2");
    Assert.Contains(items, i => i.TransferId == "T3");
}

[Fact]
public async Task GetFilteredAsync_WithDateToOnly_ReturnsImportsOnOrBeforeDate()
{
    // Arrange
    var twoDaysAgo = DateTime.UtcNow.Date.AddDays(-2);
    var yesterday = DateTime.UtcNow.Date.AddDays(-1);
    var today = DateTime.UtcNow.Date;
    await _repository.AddAsync(CreateTestImport("T1", twoDaysAgo, "ACC", CurrencyCode.CZK));
    await _repository.AddAsync(CreateTestImport("T2", yesterday, "ACC", CurrencyCode.CZK));
    await _repository.AddAsync(CreateTestImport("T3", today, "ACC", CurrencyCode.CZK));

    // Act — DateTo = yesterday should include two-days-ago & yesterday, exclude today
    var (items, totalCount) = await _repository.GetFilteredAsync(
        new BankStatementListFilter(DateTo: yesterday));

    // Assert
    Assert.Equal(2, totalCount);
    Assert.Contains(items, i => i.TransferId == "T1");
    Assert.Contains(items, i => i.TransferId == "T2");
    Assert.DoesNotContain(items, i => i.TransferId == "T3");
}

[Fact]
public async Task GetFilteredAsync_WithDateRange_ReturnsInclusiveRange()
{
    // Arrange
    var threeDaysAgo = DateTime.UtcNow.Date.AddDays(-3);
    var twoDaysAgo = DateTime.UtcNow.Date.AddDays(-2);
    var yesterday = DateTime.UtcNow.Date.AddDays(-1);
    var today = DateTime.UtcNow.Date;
    await _repository.AddAsync(CreateTestImport("T0", threeDaysAgo, "ACC", CurrencyCode.CZK));
    await _repository.AddAsync(CreateTestImport("T1", twoDaysAgo, "ACC", CurrencyCode.CZK));
    await _repository.AddAsync(CreateTestImport("T2", yesterday, "ACC", CurrencyCode.CZK));
    await _repository.AddAsync(CreateTestImport("T3", today, "ACC", CurrencyCode.CZK));

    // Act — inclusive range [twoDaysAgo, yesterday]
    var (items, totalCount) = await _repository.GetFilteredAsync(
        new BankStatementListFilter(DateFrom: twoDaysAgo, DateTo: yesterday));

    // Assert
    Assert.Equal(2, totalCount);
    Assert.Contains(items, i => i.TransferId == "T1");
    Assert.Contains(items, i => i.TransferId == "T2");
    Assert.DoesNotContain(items, i => i.TransferId == "T0");
    Assert.DoesNotContain(items, i => i.TransferId == "T3");
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetFilteredAsync_WithDate" --no-restore`
Expected: 3 new tests FAIL (filter clauses not implemented).

- [ ] **Step 3: Implement the date range clauses**

In `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`, inside `GetFilteredAsync`, after the `ErrorsOnly` clause added in Task 4 and before `var totalCount = …`, add:

```csharp
        if (filter.DateFrom.HasValue)
            query = query.Where(bs => bs.StatementDate.Date >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(bs => bs.StatementDate.Date <= filter.DateTo.Value.Date);
```

- [ ] **Step 4: Run to verify all three tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetFilteredAsync_WithDate" --no-restore`
Expected: PASS for all three new tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs
git commit -m "feat(bank): repository inclusive StatementDate range filter"
```

---

## Task 6: Repository — `EscapeLike` helper + `TransferId` / `Account` ILike filters

`EF.Functions.ILike` is not supported by the InMemory provider, so this task implements the helper and `Where` clauses, but defers behaviour assertions to the Testcontainers integration test added in Task 7.

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`

- [ ] **Step 1: Add `EscapeLike` helper and the two ILike `Where` clauses**

In `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`:

1. Add a private static helper method at the bottom of the class (above the closing brace):

```csharp
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
```

2. Inside `GetFilteredAsync`, between the existing `Id` clause and the `StatementDate` clause, add:

```csharp
        if (!string.IsNullOrWhiteSpace(filter.TransferId))
        {
            var pattern = $"%{EscapeLike(filter.TransferId.Trim())}%";
            query = query.Where(bs => EF.Functions.ILike(bs.TransferId, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Account))
        {
            var pattern = $"%{EscapeLike(filter.Account.Trim())}%";
            query = query.Where(bs => EF.Functions.ILike(bs.Account, pattern, "\\"));
        }
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Run the full Bank repository test suite to confirm no InMemory regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BankStatementImportRepositoryTests" --no-restore`
Expected: all tests added so far still PASS. The existing InMemory tests cannot exercise the ILike paths but must not be broken by their presence.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs
git commit -m "feat(bank): repository TransferId/Account ILike filters with wildcard escaping"
```

---

## Task 7: Testcontainers integration test for ILike filters

InMemory cannot exercise `EF.Functions.ILike`. Add one focused integration test class against a real Postgres container, mirroring `MeetingTranscriptRepositorySearchIntegrationTests`.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryIntegrationTests.cs`

- [ ] **Step 1: Write the failing integration test class**

Create `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryIntegrationTests.cs` with:

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
public class BankStatementImportRepositoryIntegrationTests : IAsyncLifetime
{
    static BankStatementImportRepositoryIntegrationTests()
    {
        // Required on macOS with Podman: the Ryuk ResourceReaper container
        // cannot bind to the Docker socket and throws a NullReferenceException.
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

        // Create only the BankStatements table — EnsureCreatedAsync would attempt
        // to install the "vector" extension (used by other modules) which is not
        // available in the plain postgres:16 image.
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE SCHEMA IF NOT EXISTS public;
                CREATE TABLE IF NOT EXISTS public."BankStatements" (
                    "Id"            serial                       PRIMARY KEY,
                    "TransferId"    character varying(100)       NOT NULL,
                    "StatementDate" timestamp without time zone  NOT NULL,
                    "ImportDate"    timestamp without time zone  NOT NULL,
                    "Account"       text                         NOT NULL,
                    "Currency"      integer                      NOT NULL,
                    "ItemCount"     integer                      NOT NULL,
                    "ImportResult"  text                         NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_BankStatements_TransferId"
                    ON public."BankStatements" ("TransferId");
                CREATE INDEX IF NOT EXISTS "IX_BankStatements_Account"
                    ON public."BankStatements" ("Account");
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        _repository = new BankStatementImportRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task<BankStatementImport> SeedAsync(string transferId, string account, string importResult = "OK")
    {
        var import = new BankStatementImport(transferId, DateTime.UtcNow.Date);
        import.Account = account;
        import.Currency = CurrencyCode.CZK;
        import.ItemCount = 1;
        import.ImportResult = importResult;
        return await _repository.AddAsync(import);
    }

    [Fact]
    public async Task GetFilteredAsync_TransferIdSubstring_MatchesCaseInsensitive()
    {
        // Arrange
        await SeedAsync("ABC-123", "ShoptetPay-CZK");
        await SeedAsync("XYZ-999", "ShoptetPay-CZK");

        // Act — lowercase query should still match uppercase TransferId
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(TransferId: "abc"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "ABC-123");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountSubstring_MatchesCaseInsensitive()
    {
        // Arrange
        await SeedAsync("T1", "ShoptetPay-CZK");
        await SeedAsync("T2", "Comgate-EUR");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(Account: "shoptet"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "T1");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountSubstring_TrimsWhitespace()
    {
        // Arrange
        await SeedAsync("T1", "ShoptetPay-CZK");

        // Act — surrounding whitespace must not break the match
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(Account: "  shoptet  "));

        // Assert
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetFilteredAsync_AccountSubstring_EscapesPercentWildcard()
    {
        // Arrange — exact literal '%' in stored value
        await SeedAsync("T1", "Acct-50%-rate");
        await SeedAsync("T2", "Acct-50-rate");

        // Act — searching for "50%" must match T1 literally, not match T2 as wildcard
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(Account: "50%"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "T1");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountSubstring_EscapesUnderscoreWildcard()
    {
        // Arrange — underscore is a single-char wildcard in LIKE; literal match must be preserved
        await SeedAsync("T1", "Acct_X");
        await SeedAsync("T2", "AcctYX");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(Account: "Acct_X"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "T1");
    }

    [Fact]
    public async Task GetFilteredAsync_TransferIdAndAccount_CombineWithAndSemantics()
    {
        // Arrange
        await SeedAsync("ABC-100", "ShoptetPay-CZK");
        await SeedAsync("ABC-200", "Comgate-EUR");
        await SeedAsync("XYZ-300", "ShoptetPay-CZK");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(TransferId: "abc", Account: "shoptet"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "ABC-100");
    }
}
```

- [ ] **Step 2: Run the integration tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BankStatementImportRepositoryIntegrationTests" --no-restore`
Expected: PASS for all six new tests. Docker must be available locally. If Docker is unavailable the tests will error at container startup — fix the local Docker setup rather than skipping.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryIntegrationTests.cs
git commit -m "test(bank): Testcontainers integration tests for TransferId/Account ILike filters"
```

---

## Task 8: Extend `GetBankStatementListRequest` with the five new properties

Class (not record), per project rule "DTOs are classes, never records". Date fields travel as `string?` and are parsed in the handler — same convention as the existing `StatementDate`/`ImportDate`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs`

- [ ] **Step 1: Replace the file contents**

Replace `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListRequest : IRequest<GetBankStatementListResponse>
{
    public int? Id { get; set; }
    public string? TransferId { get; set; }
    public string? Account { get; set; }
    public string? StatementDate { get; set; }
    public string? ImportDate { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public bool? ErrorsOnly { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 10;
    public string? OrderBy { get; set; } = "ImportDate";
    public bool Ascending { get; set; } = false;
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs
git commit -m "feat(bank): add TransferId/Account/DateFrom/DateTo/ErrorsOnly to GetBankStatementListRequest"
```

---

## Task 9: Update handler to parse new fields and build the full filter

The handler is the only place that converts wire-shape `string?` dates into domain-shape `DateTime?`. It also trims string filters defensively (the frontend should already trim, but defence-in-depth is cheap).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs`

- [ ] **Step 1: Write the failing handler test**

Create `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` with:

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
    private readonly Mock<IBankStatementImportRepository> _repository = new();
    private readonly IMapper _mapper;
    private readonly GetBankStatementListHandler _handler;

    public GetBankStatementListHandlerTests()
    {
        var cfg = new MapperConfiguration(c => c.AddProfile<BankMappingProfile>());
        _mapper = cfg.CreateMapper();
        _handler = new GetBankStatementListHandler(_repository.Object, _mapper, NullLogger<GetBankStatementListHandler>.Instance);

        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));
    }

    [Fact]
    public async Task Handle_PassesAllFilterFieldsToRepository()
    {
        // Arrange
        var request = new GetBankStatementListRequest
        {
            TransferId = "  ABC  ",
            Account = "  shoptet  ",
            DateFrom = "2026-01-01",
            DateTo = "2026-01-31",
            ErrorsOnly = true,
        };
        BankStatementListFilter? captured = null;
        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<BankStatementListFilter, int, int, string, bool, CancellationToken>(
                (f, _, _, _, _, _) => captured = f)
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.TransferId.Should().Be("ABC");                       // trimmed
        captured.Account.Should().Be("shoptet");                       // trimmed
        captured.DateFrom.Should().Be(new DateTime(2026, 1, 1));
        captured.DateTo.Should().Be(new DateTime(2026, 1, 31));
        captured.ErrorsOnly.Should().Be(true);
    }

    [Fact]
    public async Task Handle_IgnoresUnparseableDateStrings()
    {
        // Arrange
        var request = new GetBankStatementListRequest
        {
            DateFrom = "not-a-date",
            DateTo = "still-not-a-date",
        };
        BankStatementListFilter? captured = null;
        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<BankStatementListFilter, int, int, string, bool, CancellationToken>(
                (f, _, _, _, _, _) => captured = f)
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        captured!.DateFrom.Should().BeNull();
        captured.DateTo.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OmitsEmptyOrWhitespaceStringFilters()
    {
        // Arrange
        var request = new GetBankStatementListRequest
        {
            TransferId = "   ",
            Account = "",
        };
        BankStatementListFilter? captured = null;
        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<BankStatementListFilter, int, int, string, bool, CancellationToken>(
                (f, _, _, _, _, _) => captured = f)
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        captured!.TransferId.Should().BeNull();
        captured.Account.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetBankStatementListHandlerTests" --no-restore`
Expected: FAIL — the handler does not yet populate `TransferId`/`Account`/`DateFrom`/`DateTo`/`ErrorsOnly` on the filter object.

- [ ] **Step 3: Update the handler to wire the new fields**

Replace the contents of `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` with:

```csharp
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

        DateTime? statementDate = ParseDateOrNull(request.StatementDate);
        DateTime? importDate = ParseDateOrNull(request.ImportDate);
        DateTime? dateFrom = ParseDateOrNull(request.DateFrom);
        DateTime? dateTo = ParseDateOrNull(request.DateTo);

        var trimmedTransferId = NormalizeNullableString(request.TransferId);
        var trimmedAccount = NormalizeNullableString(request.Account);

        var filter = new BankStatementListFilter(
            Id: request.Id,
            TransferId: trimmedTransferId,
            Account: trimmedAccount,
            StatementDate: statementDate,
            ImportDate: importDate,
            DateFrom: dateFrom,
            DateTo: dateTo,
            ErrorsOnly: request.ErrorsOnly);

        var (items, totalCount) = await _repository.GetFilteredAsync(
            filter,
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

    private static DateTime? ParseDateOrNull(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DateTime.TryParse(value, out var parsed) ? parsed : null;

    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 4: Run the handler tests to verify PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetBankStatementListHandlerTests" --no-restore`
Expected: all three tests PASS.

- [ ] **Step 5: Re-run the broader Bank test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Bank"`
Expected: all Bank tests pass (unit-level + Testcontainers when Docker is available).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs
git commit -m "feat(bank): handler maps new request filters to BankStatementListFilter"
```

---

## Task 10: Extend the validator

Add the three new rule groups: length caps, parseable dates, and `DateFrom <= DateTo`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs`

- [ ] **Step 1: Write the failing test**

Append to `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs`:

```csharp
public class GetBankStatementListRequestValidatorTests
{
    private readonly Anela.Heblo.Application.Features.Bank.Validators.GetBankStatementListRequestValidator _validator = new();

    [Fact]
    public void Validate_RejectsTransferIdLongerThan100Chars()
    {
        var request = new GetBankStatementListRequest { TransferId = new string('a', 101) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.TransferId));
    }

    [Fact]
    public void Validate_RejectsAccountLongerThan100Chars()
    {
        var request = new GetBankStatementListRequest { Account = new string('a', 101) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.Account));
    }

    [Fact]
    public void Validate_RejectsUnparseableDateFrom()
    {
        var request = new GetBankStatementListRequest { DateFrom = "not-a-date" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }

    [Fact]
    public void Validate_RejectsUnparseableDateTo()
    {
        var request = new GetBankStatementListRequest { DateTo = "not-a-date" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateTo));
    }

    [Fact]
    public void Validate_RejectsDateFromLaterThanDateTo()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-02-01", DateTo = "2026-01-01" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }

    [Fact]
    public void Validate_AcceptsAllNullOptionalFields()
    {
        var request = new GetBankStatementListRequest { Take = 10, Skip = 0 };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsValidDateRange()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-01-01", DateTo = "2026-01-31" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify failures**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetBankStatementListRequestValidatorTests" --no-restore`
Expected: 5 of 7 tests FAIL (the two "Accepts…" tests will pass because no rules yet exist).

- [ ] **Step 3: Update the validator**

Replace `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` with:

```csharp
using FluentValidation;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class GetBankStatementListRequestValidator : AbstractValidator<GetBankStatementListRequest>
{
    private const int MaxStringLength = 100;

    public GetBankStatementListRequestValidator()
    {
        RuleFor(x => x.Take)
            .GreaterThan(0).WithMessage("Take must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Take must not exceed 100");

        RuleFor(x => x.Skip)
            .GreaterThanOrEqualTo(0).WithMessage("Skip must be greater than or equal to 0");

        RuleFor(x => x.TransferId!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"TransferId must not exceed {MaxStringLength} characters")
            .When(x => x.TransferId != null);

        RuleFor(x => x.Account!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"Account must not exceed {MaxStringLength} characters")
            .When(x => x.Account != null);

        RuleFor(x => x.DateFrom!)
            .Must(BeParseableDate)
            .WithMessage("DateFrom must be a valid date")
            .When(x => !string.IsNullOrWhiteSpace(x.DateFrom));

        RuleFor(x => x.DateTo!)
            .Must(BeParseableDate)
            .WithMessage("DateTo must be a valid date")
            .When(x => !string.IsNullOrWhiteSpace(x.DateTo));

        RuleFor(x => x.DateFrom!)
            .Must((req, _) => DateFromIsNotLaterThanDateTo(req))
            .WithMessage("DateFrom must not be later than DateTo")
            .When(x => BeParseableDate(x.DateFrom) && BeParseableDate(x.DateTo));
    }

    private static bool BeParseableDate(string? value) =>
        string.IsNullOrWhiteSpace(value) || DateTime.TryParse(value, out _);

    private static bool DateFromIsNotLaterThanDateTo(GetBankStatementListRequest req)
    {
        if (!DateTime.TryParse(req.DateFrom, out var from)) return true;
        if (!DateTime.TryParse(req.DateTo, out var to)) return true;
        return from.Date <= to.Date;
    }
}
```

- [ ] **Step 4: Run the validator tests to verify PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetBankStatementListRequestValidatorTests" --no-restore`
Expected: all 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs
git commit -m "feat(bank): validator length/parseable-date/range rules for list filters"
```

---

## Task 11: Extend the controller action

Add five `[FromQuery]` parameters mirroring the request DTO. XML docs updated for OpenAPI generation. Note: the catch for `FluentValidation.ValidationException` was already added in Task 1.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`

- [ ] **Step 1: Update the `GetBankStatements` action**

In `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`, replace the existing `GetBankStatements` action (signature + body, lines ~82–126) with:

```csharp
    /// <summary>
    /// Get list of bank statement imports with optional filtering and pagination
    /// </summary>
    /// <param name="id">Filter by import ID</param>
    /// <param name="transferId">Case-insensitive substring filter on Transfer ID (max 100 chars)</param>
    /// <param name="account">Case-insensitive substring filter on Account name (max 100 chars)</param>
    /// <param name="statementDate">Filter by statement date (exact match)</param>
    /// <param name="importDate">Filter by import date (exact match)</param>
    /// <param name="dateFrom">Inclusive lower bound on statement date (ISO date)</param>
    /// <param name="dateTo">Inclusive upper bound on statement date (ISO date)</param>
    /// <param name="errorsOnly">When true, restricts to statements with ImportResult != "OK"</param>
    /// <param name="skip">Number of records to skip (default: 0)</param>
    /// <param name="take">Number of records to take (default: 10, max: 100)</param>
    /// <param name="orderBy">Order by field (default: ImportDate)</param>
    /// <param name="ascending">Sort direction (default: false)</param>
    /// <returns>Paginated list of bank statement imports</returns>
    [HttpGet]
    public async Task<ActionResult<GetBankStatementListResponse>> GetBankStatements(
        [FromQuery] int? id = null,
        [FromQuery] string? transferId = null,
        [FromQuery] string? account = null,
        [FromQuery] string? statementDate = null,
        [FromQuery] string? importDate = null,
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
                TransferId = transferId,
                Account = account,
                StatementDate = statementDate,
                ImportDate = importDate,
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
            _logger.LogWarning(ex, "Invalid request for bank statement list");
            return BadRequest(new { message = "Invalid request", errors = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving bank statements");
            return StatusCode(500, new { message = "An error occurred while retrieving bank statements" });
        }
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Run the entire backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: all tests PASS. (Testcontainers-based integration tests require Docker locally; CI provides it.)

- [ ] **Step 4: Run `dotnet format` to satisfy the style checker**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
If this fails, run: `dotnet format backend/Anela.Heblo.sln` and verify changes are formatting-only.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
git commit -m "feat(bank): expose new filter query params on GET /api/bank-statements"
```

---

## Task 12: Extend the frontend hook DTO and request serialization

`useBankStatementsList` is hand-written and uses `URLSearchParams` directly — extending it is local. The auto-generated client (`frontend/src/api/generated/api-client.ts`) regenerates on backend Debug build; no manual edit there.

**Files:**
- Modify: `frontend/src/api/hooks/useBankStatements.ts`

- [ ] **Step 1: Update the `GetBankStatementListRequest` interface and the hook's query-string serialisation**

In `frontend/src/api/hooks/useBankStatements.ts`:

1. Replace the existing `GetBankStatementListRequest` interface (lines 40–48) with:

```typescript
export interface GetBankStatementListRequest {
  id?: number;
  transferId?: string;
  account?: string;
  statementDate?: string;
  importDate?: string;
  dateFrom?: string;   // ISO date 'YYYY-MM-DD'
  dateTo?: string;     // ISO date 'YYYY-MM-DD'
  errorsOnly?: boolean;
  skip?: number;
  take?: number;
  orderBy?: string;
  ascending?: boolean;
}
```

2. Inside the `useBankStatementsList` queryFn, replace the `URLSearchParams` block (lines 97–118) with:

```typescript
      const params = new URLSearchParams();
      if (request.id !== undefined) {
        params.append('id', request.id.toString());
      }
      const transferId = request.transferId?.trim();
      if (transferId) {
        params.append('transferId', transferId);
      }
      const account = request.account?.trim();
      if (account) {
        params.append('account', account);
      }
      if (request.statementDate) {
        params.append('statementDate', request.statementDate);
      }
      if (request.importDate) {
        params.append('importDate', request.importDate);
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
      if (request.skip !== undefined) {
        params.append('skip', request.skip.toString());
      }
      if (request.take !== undefined) {
        params.append('take', request.take.toString());
      }
      if (request.orderBy) {
        params.append('orderBy', request.orderBy);
      }
      if (request.ascending !== undefined) {
        params.append('ascending', request.ascending.toString());
      }
```

The hook trims string filters and omits empty/whitespace values (FR-2, FR-6). `errorsOnly` is only appended when truthy — `false` or `undefined` are equivalent server-side.

- [ ] **Step 2: Verify the frontend compiles**

Run: `cd frontend && npm run build`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useBankStatements.ts
git commit -m "feat(bank): extend useBankStatementsList request shape with five new filter fields"
```

---

## Task 13: Refactor `ImportTab` to use committed filters

Replace the existing pair `(transferIdInput, transferIdFilter)` / `(accountInput, accountFilter)` plus the no-op `refetch()` pattern with a single `committedFilters` object that the hook depends on. React Query keys the cache on the request object, so the cache only refreshes when committed filters change.

**Files:**
- Modify: `frontend/src/components/customer/tabs/ImportTab.tsx`

- [ ] **Step 1: Update state, hook input, apply / clear handlers, validation guard, and the "(filtrováno)" indicator**

The change is large enough that the cleanest path is a full replacement of the affected sections rather than scattered surgical edits. Apply the following edits to `frontend/src/components/customer/tabs/ImportTab.tsx`:

1. Replace the existing filter-state block (lines 24–32) with:

```typescript
  // Local input state (uncommitted)
  const [transferIdInput, setTransferIdInput] = useState("");
  const [accountInput, setAccountInput] = useState("");
  const [statementDateFrom, setStatementDateFrom] = useState("");
  const [statementDateTo, setStatementDateTo] = useState("");
  const [showOnlyErrors, setShowOnlyErrors] = useState(false);
  const [dateRangeError, setDateRangeError] = useState<string | null>(null);

  // Committed filters (drive the hook; updated only on Apply / Clear)
  const [committedFilters, setCommittedFilters] = useState<{
    transferId?: string;
    account?: string;
    dateFrom?: string;
    dateTo?: string;
    errorsOnly?: boolean;
  }>({});

  const hasActiveFilter =
    !!committedFilters.transferId ||
    !!committedFilters.account ||
    !!committedFilters.dateFrom ||
    !!committedFilters.dateTo ||
    !!committedFilters.errorsOnly;
```

2. Replace the `useBankStatementsList` call (lines 48–58) with:

```typescript
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useBankStatementsList({
    transferId: committedFilters.transferId,
    account: committedFilters.account,
    dateFrom: committedFilters.dateFrom,
    dateTo: committedFilters.dateTo,
    errorsOnly: committedFilters.errorsOnly,
    skip: (pageNumber - 1) * pageSize,
    take: pageSize,
    orderBy: sortBy,
    ascending: !sortDescending,
  });
```

3. Replace `handleApplyFilters` (lines 69–74) with:

```typescript
  const handleApplyFilters = () => {
    if (statementDateFrom && statementDateTo && statementDateFrom > statementDateTo) {
      setDateRangeError('"Od" musí být dříve nebo stejně jako "Do".');
      return;
    }
    setDateRangeError(null);

    const trimmedTransferId = transferIdInput.trim();
    const trimmedAccount = accountInput.trim();

    setCommittedFilters({
      transferId: trimmedTransferId || undefined,
      account: trimmedAccount || undefined,
      dateFrom: statementDateFrom || undefined,
      dateTo: statementDateTo || undefined,
      errorsOnly: showOnlyErrors || undefined,
    });
    setPageNumber(1);
  };
```

(`refetch()` removed — committing new filter state changes the React Query key and triggers a fetch automatically.)

4. Replace `handleClearFilters` (lines 84–94) with:

```typescript
  const handleClearFilters = () => {
    setTransferIdInput("");
    setAccountInput("");
    setStatementDateFrom("");
    setStatementDateTo("");
    setShowOnlyErrors(false);
    setDateRangeError(null);
    setCommittedFilters({});
    setPageNumber(1);
  };
```

5. Surface the inline date-range error. Below the existing two `<input type="date" …>` controls (currently in the `<div className="flex gap-2">` ending around line 346), add a render of `dateRangeError` immediately after that div closes:

```tsx
            {dateRangeError && (
              <p className="text-xs text-red-600">{dateRangeError}</p>
            )}
```

6. Update the "(filtrováno)" indicator at line 484. Replace:

```tsx
                  {transferIdFilter || accountFilter ? (
                    <span className="text-gray-500"> (filtrováno)</span>
                  ) : (
                    ""
                  )}
```

with:

```tsx
                  {hasActiveFilter ? (
                    <span className="text-gray-500"> (filtrováno)</span>
                  ) : (
                    ""
                  )}
```

7. Remove the now-dead `transferIdFilter` / `accountFilter` state declarations from the top of the component (they were lines 27–28 in the original).

- [ ] **Step 2: Verify the frontend compiles and lints**

Run: `cd frontend && npm run build && npm run lint`
Expected: both succeed with no new errors. TypeScript will surface any forgotten references to the removed `transferIdFilter` / `accountFilter` state.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/customer/tabs/ImportTab.tsx
git commit -m "feat(bank): wire Import tab filters via committed-filter state to useBankStatementsList"
```

---

## Task 14: Component test for `ImportTab` filter wiring

Verify (a) committed-filter payload is sent on apply, (b) clear sends an empty payload, (c) `dateFrom > dateTo` guard blocks submission. Mock `useBankStatementsList` so the test does not need network or auth.

**Files:**
- Create: `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx` with:

```tsx
import React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom';

const useBankStatementsListMock = jest.fn();
const useBankStatementImportMock = jest.fn();
const useBankStatementAccountsMock = jest.fn();

jest.mock('../../../../api/hooks/useBankStatements', () => ({
  useBankStatementsList: (req: unknown) => useBankStatementsListMock(req),
  useBankStatementImport: () => useBankStatementImportMock(),
  useBankStatementAccounts: () => useBankStatementAccountsMock(),
}));

import ImportTab from '../ImportTab';

const baseHookResponse = {
  data: { items: [], totalCount: 0 },
  isLoading: false,
  error: null,
  refetch: jest.fn(),
};

beforeEach(() => {
  useBankStatementsListMock.mockReset();
  useBankStatementImportMock.mockReset();
  useBankStatementAccountsMock.mockReset();

  useBankStatementsListMock.mockImplementation(() => baseHookResponse);
  useBankStatementImportMock.mockReturnValue({ mutateAsync: jest.fn() });
  useBankStatementAccountsMock.mockReturnValue({ data: [], isLoading: false });
});

function lastHookCallArg() {
  return useBankStatementsListMock.mock.calls[useBankStatementsListMock.mock.calls.length - 1][0];
}

describe('ImportTab filters', () => {
  it('does not send filter values until Filtrovat is clicked', () => {
    render(<ImportTab />);

    fireEvent.change(screen.getByPlaceholderText('Transfer ID...'), { target: { value: 'ABC' } });
    fireEvent.change(screen.getByPlaceholderText('Účet...'), { target: { value: 'Shoptet' } });

    // After only editing inputs (no Filtrovat click), the committed filter remains empty
    const args = lastHookCallArg();
    expect(args.transferId).toBeUndefined();
    expect(args.account).toBeUndefined();
  });

  it('sends trimmed committed filters on Filtrovat click and resets page', () => {
    render(<ImportTab />);

    fireEvent.change(screen.getByPlaceholderText('Transfer ID...'), { target: { value: '  ABC  ' } });
    fireEvent.change(screen.getByPlaceholderText('Účet...'), { target: { value: '  Shoptet  ' } });
    fireEvent.click(screen.getByText('Filtrovat'));

    const args = lastHookCallArg();
    expect(args.transferId).toBe('ABC');
    expect(args.account).toBe('Shoptet');
    expect(args.skip).toBe(0); // page reset to 1 → skip 0
  });

  it('sends errorsOnly=true on Filtrovat click when checkbox is checked', () => {
    render(<ImportTab />);

    fireEvent.click(screen.getByLabelText(/Jen chyby/));
    fireEvent.click(screen.getByText('Filtrovat'));

    const args = lastHookCallArg();
    expect(args.errorsOnly).toBe(true);
  });

  it('sends inclusive date range on Filtrovat click', () => {
    render(<ImportTab />);

    const dateInputs = screen.getAllByDisplayValue('').filter(
      (i) => (i as HTMLInputElement).type === 'date'
    ) as HTMLInputElement[];
    expect(dateInputs.length).toBeGreaterThanOrEqual(2);

    fireEvent.change(dateInputs[0], { target: { value: '2026-01-01' } });
    fireEvent.change(dateInputs[1], { target: { value: '2026-01-31' } });
    fireEvent.click(screen.getByText('Filtrovat'));

    const args = lastHookCallArg();
    expect(args.dateFrom).toBe('2026-01-01');
    expect(args.dateTo).toBe('2026-01-31');
  });

  it('blocks Filtrovat and shows inline error when dateFrom > dateTo', () => {
    render(<ImportTab />);

    const dateInputs = screen.getAllByDisplayValue('').filter(
      (i) => (i as HTMLInputElement).type === 'date'
    ) as HTMLInputElement[];
    fireEvent.change(dateInputs[0], { target: { value: '2026-02-01' } });
    fireEvent.change(dateInputs[1], { target: { value: '2026-01-01' } });

    const callsBefore = useBankStatementsListMock.mock.calls.length;
    fireEvent.click(screen.getByText('Filtrovat'));

    // committed filters did not change → hook was not re-keyed with the bad range
    const args = lastHookCallArg();
    expect(args.dateFrom).toBeUndefined();
    expect(args.dateTo).toBeUndefined();
    expect(screen.getByText(/"Od" musí být dříve/)).toBeInTheDocument();
    expect(useBankStatementsListMock.mock.calls.length).toBeGreaterThanOrEqual(callsBefore);
  });

  it('clears all committed filters and resets page on Vymazat', () => {
    render(<ImportTab />);

    fireEvent.change(screen.getByPlaceholderText('Transfer ID...'), { target: { value: 'ABC' } });
    fireEvent.click(screen.getByText('Filtrovat'));
    fireEvent.click(screen.getByText('Vymazat'));

    const args = lastHookCallArg();
    expect(args.transferId).toBeUndefined();
    expect(args.account).toBeUndefined();
    expect(args.dateFrom).toBeUndefined();
    expect(args.dateTo).toBeUndefined();
    expect(args.errorsOnly).toBeUndefined();
    expect(args.skip).toBe(0);
  });
});
```

- [ ] **Step 2: Run the test**

Run: `cd frontend && npm test -- --testPathPattern='ImportTab.test.tsx' --watchAll=false`
Expected: all six tests PASS. If any fail because the locators don't match the current markup (e.g. button text "Vymazat" vs "Vyčistit"), reconcile by checking `ImportTab.tsx` — the current code uses "Filtrovat" / "Vymazat", which is what the test targets.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx
git commit -m "test(bank): ImportTab committed-filter wiring + dateFrom>dateTo guard"
```

---

## Task 15: Full end-to-end validation

Final gate before declaring the feature done. Run every check the CLAUDE.md "Validation before completion" section requires.

**Files:** (none — verification only)

- [ ] **Step 1: Backend build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: BUILD SUCCEEDED with no new warnings.

- [ ] **Step 2: Backend format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: no formatting changes required.

- [ ] **Step 3: Backend tests (all, including Testcontainers integration class — requires local Docker)**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: 100% PASS. If Docker is unavailable, run instead `dotnet test backend/Anela.Heblo.sln --filter "Category!=Integration"` and note that the integration tests will be verified in CI.

- [ ] **Step 4: Frontend build**

Run: `cd frontend && npm run build`
Expected: build succeeds with no TypeScript errors.

- [ ] **Step 5: Frontend lint**

Run: `cd frontend && npm run lint`
Expected: no new lint errors. Pre-existing warnings in unrelated files are out of scope.

- [ ] **Step 6: Frontend tests for the changed files**

Run: `cd frontend && npm test -- --testPathPattern='ImportTab' --watchAll=false`
Expected: PASS.

- [ ] **Step 7: Manual smoke check (optional but recommended)**

Start the backend and frontend dev servers, log in, navigate to the Bank module → Import tab, and verify:
- Typing into Transfer ID and clicking "Filtrovat" returns a smaller result set including only matching rows.
- Typing into Účet and clicking "Filtrovat" works the same.
- Selecting a date range filters by `StatementDate` (inclusive on both ends).
- Checking "Jen chyby" returns only failed imports.
- Setting "Od" later than "Do" shows the inline error and does not refresh the grid.
- "Vymazat" returns to the unfiltered list at page 1.
- The footer shows "(filtrováno)" whenever any of the five filters is committed.

If you cannot run the dev servers (no auth credentials available locally), state that explicitly rather than claiming success.

- [ ] **Step 8: No commit — verification only**

If any step failed, fix it in the appropriate task and re-run the gate. Do not commit "verification done" markers.

---

## Self-Review

**Spec coverage (against `spec.r1.md`):**
- FR-1 Transfer ID filter → Tasks 6, 7, 8, 9, 11, 12, 13.
- FR-2 Account filter (case-insensitive, trim, `EF.Functions.ILike`, reuses `IX_BankStatements_Account`) → Tasks 6, 7, 8, 9, 11, 12, 13.
- FR-3 Statement date range filter (`DateTime.TryParse`, inclusive day-granularity, open-ended, `DateFrom > DateTo` rejected) → Tasks 5, 8, 9, 10, 11, 12, 13.
- FR-4 Errors-only filter using `ImportResult != ImportStatus.Success` → Tasks 4, 8, 9, 11, 12, 13.
- FR-5 Combined filter AND semantics → covered by Task 7 (`CombineWithAndSemantics`) and the InMemory tests across Tasks 4–5.
- FR-6 Apply / Clear UX + page reset → Tasks 13, 14.
- FR-7 Empty / loading / error states preserved → No new code (existing branches in `ImportTab.tsx` untouched).
- NFR-1 Performance (server-side filtering, EF translation, no in-memory post-filter) → repository implementation, Tasks 4–6.
- NFR-2 Security (parameterised EF, length cap, wildcard escape, auth unchanged) → Tasks 6, 10.
- NFR-3 API compatibility (new params optional) → Task 11 (defaults to null).
- NFR-4 Testability (per-filter coverage + combined + baseline) → Tasks 4, 5, 7, 9, 10, 14.

**Architecture amendments (against `arch-review.r1.md`):**
- Amendment 1 (`BankStatementListFilter` record) → Tasks 2, 3.
- Amendment 2 (`CancellationToken` plumbed) → Task 3.
- Amendment 3 (use `ImportStatus.Success` constant) → Task 4.
- Amendment 4 (Testcontainers for ILike, InMemory for the rest) → Tasks 4–7.
- Amendment 5 (`EscapeLike` helper applied) → Tasks 6, 7.
- Amendment 6 (honest "(filtrováno)" indicator considers all five filters) → Task 13 (via `hasActiveFilter`).

**Prerequisites (against arch-review):**
- FluentValidation pipeline wiring → Task 1 (resolved as the first task, since the validator additions in Task 10 depend on it).
- `ValidationException` → 400 mapping → Task 1, repeated in Task 11.
- Docker for Testcontainers → called out in Task 7 and Task 15 step 3.
- OpenAPI client regeneration → automatic on backend Debug build per `docs/development/api-client-generation.md`; no explicit task.

**Placeholder scan:** No "TBD", "add appropriate error handling", or "similar to Task N" patterns. All test code, validator rules, controller actions, and frontend handlers are spelled out with their full implementation bodies.

**Type consistency:** `BankStatementListFilter` is referenced consistently as a `sealed record` with constructor parameters `(Id, TransferId, Account, StatementDate, ImportDate, DateFrom, DateTo, ErrorsOnly)`. `GetFilteredAsync` takes `(filter, skip, take, orderBy, ascending, cancellationToken)` everywhere it appears (Tasks 3, 4, 5, 6, 7, 9). Frontend `GetBankStatementListRequest` fields are camelCase TS, backend request DTO fields are PascalCase C# — both follow each language's convention and serialise identically via the default model binder.
