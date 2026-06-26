# Remove Unused `ILotsClient` from `GetCatalogDetailHandler` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the unused `ILotsClient` constructor dependency from `GetCatalogDetailHandler` (and its corresponding mock from the two handler test fixtures) without changing any runtime behavior.

**Architecture:** Pure compile-time refactor. The handler already reads lots from `catalogItem.Stock.Lots` (sourced from the cached `CatalogAggregate`), so the `ILotsClient` field is dead code. The interface itself, its DI registration in `FlexiAdapterServiceCollectionExtensions`, and other legitimate consumers (`CatalogDataRefreshService`, `CatalogRepository`) remain untouched.

**Tech Stack:** .NET 8, C# (nullable refs enabled), MediatR, xUnit + Moq + FluentAssertions.

---

## Pre-flight Context

**This is a refactor — there is no "new failing test" to add up front.** The existing `GetCatalogDetailHandlerTests` and `GetCatalogDetailHandlerFullHistoryTests` already cover the handler's behavior end-to-end (no `Setup()` or `Verify()` is ever called on `_lotsClientMock` in either fixture — confirmed by reading the files). The refactor is correct iff:

1. The handler file compiles after the field/parameter/using is removed.
2. Both test files compile after the mock field/instantiation/argument is removed.
3. All existing tests in those two fixtures continue to pass with no edits to test bodies.

The plan therefore uses a **lockstep edit** approach: edit the handler and both test files in successive tasks (each leaves the solution in a *non-compiling* state until task 3), then verify via a single build+test run at the end. This is unusual for TDD-style plans but appropriate for a signature-coupled refactor.

**Files in scope:**

| File | Path |
|------|------|
| Production handler | `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` |
| Test fixture A | `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` |
| Test fixture B | `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` |

**Files that MUST NOT be touched (per arch-review):**

- `backend/src/Anela.Heblo.Domain/Features/Catalog/Lots/ILotsClient.cs` — interface stays.
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — DI registration stays.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` — legitimate consumer.
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — `_lotsClientMock` here belongs to `CatalogRepository`, a different SUT.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` — same reason.

---

### Task 1: Remove `ILotsClient` from the handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`

This task leaves the **test projects intentionally broken** (they still pass `ILotsClient` to the constructor). Tasks 2 and 3 fix the tests; Task 4 is the verification gate.

- [ ] **Step 1: Remove the `using Anela.Heblo.Domain.Features.Catalog.Lots;` directive**

The handler uses no other type from the `Lots` namespace (verified by reading lines 1–257 of the file — `ILotsClient` is the only `Lots`-namespace reference). Remove line 4.

Edit `GetCatalogDetailHandler.cs` — replace:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
```

with:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
```

- [ ] **Step 2: Remove the `_lotsClient` field declaration**

Edit `GetCatalogDetailHandler.cs` — replace:

```csharp
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILotsClient _lotsClient;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetCatalogDetailHandler> _logger;
```

with:

```csharp
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetCatalogDetailHandler> _logger;
```

- [ ] **Step 3: Remove the constructor parameter and its assignment**

Edit `GetCatalogDetailHandler.cs` — replace the entire constructor block (currently lines 19–31):

```csharp
    public GetCatalogDetailHandler(
        ICatalogRepository catalogRepository,
        ILotsClient lotsClient,
        IMapper mapper,
        TimeProvider timeProvider,
        ILogger<GetCatalogDetailHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _lotsClient = lotsClient;
        _mapper = mapper;
        _timeProvider = timeProvider;
        _logger = logger;
    }
```

with:

```csharp
    public GetCatalogDetailHandler(
        ICatalogRepository catalogRepository,
        IMapper mapper,
        TimeProvider timeProvider,
        ILogger<GetCatalogDetailHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _mapper = mapper;
        _timeProvider = timeProvider;
        _logger = logger;
    }
```

- [ ] **Step 4: Verify the production project still compiles in isolation**

Run from repo root:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: **Build succeeded. 0 Errors.** (The application project does not reference the test project, so its build is independent.)

If errors appear, they likely point to a method body in the handler that still references `_lotsClient` — re-read the handler. The architecture review confirmed no such reference exists, so any error is a regression in this task.

- [ ] **Step 5: Do NOT commit yet**

The test projects will fail to compile until Tasks 2 and 3 are done. Commit only at the end of Task 4 (one atomic commit for the whole refactor).

---

### Task 2: Update `GetCatalogDetailHandlerTests`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs`

- [ ] **Step 1: Remove the `using Anela.Heblo.Domain.Features.Catalog.Lots;` directive**

This file references no other type from the `Lots` namespace (verified by reading lines 1–377). Remove line 9.

Edit `GetCatalogDetailHandlerTests.cs` — replace:

```csharp
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Services;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
```

with:

```csharp
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Services;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
```

- [ ] **Step 2: Remove the `_lotsClientMock` field**

Edit `GetCatalogDetailHandlerTests.cs` — replace:

```csharp
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILotsClient> _lotsClientMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetCatalogDetailHandler _handler;
```

with:

```csharp
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetCatalogDetailHandler _handler;
```

- [ ] **Step 3: Remove the mock instantiation and the constructor argument**

Edit `GetCatalogDetailHandlerTests.cs` — replace the constructor body (currently lines 26–36):

```csharp
    public GetCatalogDetailHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _lotsClientMock = new Mock<ILotsClient>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        var loggerMock = new Mock<ILogger<GetCatalogDetailHandler>>();
        _handler = new GetCatalogDetailHandler(_catalogRepositoryMock.Object, _lotsClientMock.Object, _mapperMock.Object, _timeProviderMock.Object, loggerMock.Object);

        // No longer needed - using pre-calculated margins from CatalogAggregate
    }
```

with:

```csharp
    public GetCatalogDetailHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        var loggerMock = new Mock<ILogger<GetCatalogDetailHandler>>();
        _handler = new GetCatalogDetailHandler(_catalogRepositoryMock.Object, _mapperMock.Object, _timeProviderMock.Object, loggerMock.Object);

        // No longer needed - using pre-calculated margins from CatalogAggregate
    }
```

(The trailing comment is unrelated to lots — preserve it as-is per the "Surgical changes" rule in CLAUDE.md.)

- [ ] **Step 4: Confirm no further `_lotsClientMock` references remain in this file**

Run from repo root:

```bash
grep -n "_lotsClientMock\|ILotsClient" backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs
```

Expected output: *(no matches — empty)*

If anything matches, the file still has a stale reference — re-read the file and remove it.

- [ ] **Step 5: Do NOT commit yet**

The other test file (`GetCatalogDetailHandlerFullHistoryTests`) still passes `_lotsClientMock.Object` to the constructor, so the test project will not compile. Continue to Task 3.

---

### Task 3: Update `GetCatalogDetailHandlerFullHistoryTests`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs`

- [ ] **Step 1: Remove the `using Anela.Heblo.Domain.Features.Catalog.Lots;` directive**

This file references no other type from the `Lots` namespace (verified by reading lines 1–349). Remove line 8.

Edit `GetCatalogDetailHandlerFullHistoryTests.cs` — replace:

```csharp
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
```

with:

```csharp
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
```

- [ ] **Step 2: Remove the `_lotsClientMock` field**

Edit `GetCatalogDetailHandlerFullHistoryTests.cs` — replace:

```csharp
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILotsClient> _lotsClientMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetCatalogDetailHandler _handler;
```

with:

```csharp
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetCatalogDetailHandler _handler;
```

- [ ] **Step 3: Remove the mock instantiation and the constructor argument**

Edit `GetCatalogDetailHandlerFullHistoryTests.cs` — replace the constructor body (currently lines 26–36):

```csharp
    public GetCatalogDetailHandlerFullHistoryTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _lotsClientMock = new Mock<ILotsClient>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        var loggerMock = new Mock<ILogger<GetCatalogDetailHandler>>();
        _handler = new GetCatalogDetailHandler(_catalogRepositoryMock.Object, _lotsClientMock.Object, _mapperMock.Object, _timeProviderMock.Object, loggerMock.Object);

        // No longer needed - using pre-calculated margins from CatalogAggregate
    }
```

with:

```csharp
    public GetCatalogDetailHandlerFullHistoryTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        var loggerMock = new Mock<ILogger<GetCatalogDetailHandler>>();
        _handler = new GetCatalogDetailHandler(_catalogRepositoryMock.Object, _mapperMock.Object, _timeProviderMock.Object, loggerMock.Object);

        // No longer needed - using pre-calculated margins from CatalogAggregate
    }
```

- [ ] **Step 4: Confirm no further `_lotsClientMock` references remain in this file**

Run from repo root:

```bash
grep -n "_lotsClientMock\|ILotsClient" backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs
```

Expected output: *(no matches — empty)*

- [ ] **Step 5: Do NOT commit yet**

Proceed to Task 4 for the verification gate and commit.

---

### Task 4: Verify, format, and commit

**Files:** None (verification only).

This is the gate that proves the refactor preserved behavior. All three steps below must pass before committing.

- [ ] **Step 1: Confirm no stray `ILotsClient` / `_lotsClientMock` references remain in the handler test files**

Run from repo root:

```bash
grep -n "_lotsClient\|ILotsClient" \
  backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs
```

Expected output: *(no matches — empty)*

- [ ] **Step 2: Confirm `ILotsClient` is still used by legitimate consumers (regression guard)**

Run from repo root:

```bash
grep -rn "ILotsClient" backend/src --include="*.cs"
```

Expected: at least these matches still present (DI registration + legitimate consumers must not be accidentally collateral-damaged):

- `backend/src/Anela.Heblo.Domain/Features/Catalog/Lots/ILotsClient.cs` (the interface itself)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` (DI registration)
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` (legitimate consumer)
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogRepository.cs` *(if it references `ILotsClient` directly — confirmed by arch-review)*

If any of these have disappeared, you over-deleted. Restore them.

- [ ] **Step 3: Build the full backend solution**

Run from repo root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **Build succeeded. 0 Error(s).** Warnings unchanged from baseline.

If build fails on the test project complaining about `GetCatalogDetailHandler` constructor mismatch, one of Tasks 2 or 3 was incomplete — re-grep and re-edit.

- [ ] **Step 4: Run the targeted tests**

Run from repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetCatalogDetailHandler" \
  --no-build
```

Expected: **All tests pass.** Specifically:

- `GetCatalogDetailHandlerTests` — 4 test methods (`Handle_Should_Return_Response_With_Default_13_Months`, `Handle_Should_Use_Custom_MonthsBack_Value` (×3 inline data), `Handle_Should_Handle_Edge_Cases_For_MonthsBack` (×3 inline data), `Handle_Should_Throw_Exception_When_Product_Not_Found`, `Handle_Should_Return_Properly_Ordered_Historical_Data`).
- `GetCatalogDetailHandlerFullHistoryTests` — 3 test methods (`Handle_Should_Return_All_Records_When_MonthsBack_Is_999`, `Handle_Should_Filter_Records_When_MonthsBack_Is_13`, `Handle_Should_Exclude_PreFloor_Records_When_MonthsBack_Is_999`).

Total: ~13 test executions counting `[Theory]` permutations. **Zero failures, zero skips.**

If any test fails, do NOT fix the test — re-read the handler. Behavior must be identical to before, so a failure means the refactor accidentally changed behavior (e.g. removed a required line). Roll back and re-do.

- [ ] **Step 5: Run the broader Catalog test slice as a safety net**

Run from repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Catalog" \
  --no-build
```

Expected: **All tests pass.** Critically, `CatalogRepositoryTests` and `CatalogRepositoryCacheOptimizationTests` must still pass — their `_lotsClientMock` was deliberately not touched (they test a different SUT).

- [ ] **Step 6: Apply `dotnet format` to the three touched files only**

Run from repo root:

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs \
            backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs \
            backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs
```

Expected: **Format complete.** If `dotnet format` rewrites unrelated lines (whitespace, member ordering) in these files, accept those changes — they are scoped to the three files we already own in this commit.

- [ ] **Step 7: Re-run the build after formatting**

Run from repo root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **Build succeeded. 0 Error(s).** Formatter occasionally introduces stylistic edits the compiler dislikes (rare) — re-build to be safe.

- [ ] **Step 8: Inspect the staged diff**

Run from repo root:

```bash
git status
git diff backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs
git diff backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs
git diff backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs
```

Expected: **Only the three files** are modified. Each diff shows:
- Removed `using Anela.Heblo.Domain.Features.Catalog.Lots;`
- Removed `ILotsClient` / `_lotsClient` / `_lotsClientMock` references
- No changes to any test method body, no changes to mapping logic, no changes to other handler fields

If `git status` shows any file outside this list, investigate before committing — the refactor must be surgical.

- [ ] **Step 9: Commit**

Run from repo root:

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs

git commit -m "refactor(catalog): remove unused ILotsClient from GetCatalogDetailHandler

The handler never invokes the injected ILotsClient — lots come from
CatalogAggregate.Stock.Lots via the cache pipeline. Remove the dead
constructor parameter, field, and the matching mock from both handler
test fixtures. DI registration and other legitimate consumers of
ILotsClient (CatalogDataRefreshService, CatalogRepository) are
unchanged."
```

Expected: clean commit, no pre-commit hook failures.

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Covered by |
|------------------|------------|
| FR-1: Remove `ILotsClient` parameter + `_lotsClient` field; no orphaned usings; handler still compiles | Task 1 (steps 1–3) + Task 4 (steps 1, 3) |
| FR-2: `catalogItemDto.Lots` still populated from `catalogItem.Stock.Lots`; no new client calls; response shape unchanged | Task 1 (only field/parameter touched — handler body untouched, verified by Step 8 diff inspection) + Task 4 (step 4 — existing tests pin behavior) |
| FR-3: Existing unit tests updated to match new ctor; mocks for `ILotsClient` removed where unnecessary | Task 2 + Task 3 |
| FR-4: DI registration of `ILotsClient` intact; other consumers untouched | Task 4 (step 2 — regression guard grep) |
| NFR-1 perf / NFR-2 security / NFR-3 maintainability / NFR-4 backward-compat | All implicitly preserved — refactor is signature-only, no behavior change |
| Arch-review amendment 1: only `GetCatalogDetailHandlerTests` and `GetCatalogDetailHandlerFullHistoryTests` touched on test side | Explicit file list in Task 2 + Task 3; `CatalogRepository*Tests` in the "MUST NOT touch" list |
| Arch-review amendment 2: drop `using ...Catalog.Lots;` in handler + both test files | Task 1 step 1, Task 2 step 1, Task 3 step 1 |

**Placeholder scan:** No `TBD`, no `implement later`, no `similar to Task N`. Every code edit is shown in full; every command has explicit expected output.

**Type/name consistency:** `_lotsClient` (field), `ILotsClient` (interface), `_lotsClientMock` (test field) used consistently across all tasks.

**No spec gaps identified.**
