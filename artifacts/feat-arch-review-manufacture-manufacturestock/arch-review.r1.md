I have enough context to write the review.

```markdown
# Architecture Review: Remove Cross-Module Dependency in `ManufactureStockTakingController.GetManufactureStockTakingHistory`

## Skip Design: true

This is a backend-only module-boundary refactor. No new screens, components, or visual changes — only an internal namespace/type rename whose only visible surface is a regenerated TypeScript client and an updated import in one hook file.

## Architectural Fit Assessment

The proposed change fits the existing patterns exceptionally well; this is the textbook way to fix the violation in this codebase.

- **Cross-module contract already exists.** `IManufactureCatalogSource` (`backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs`) is a Manufacture-owned read abstraction over Catalog products, implemented by `CatalogManufactureCatalogSourceAdapter` in `Catalog.Infrastructure`. `SubmitManufactureStockTakingHandler` already uses this exact contract to read the same `CatalogAggregate`. The new history handler will follow the identical access pattern — no new infrastructure required.
- **ModuleBoundariesTests already covers this direction.** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` declares a `Manufacture → Catalog` rule with a `ManufactureCatalogAllowlist` containing the deliberate `CatalogAggregate` leak through `IManufactureCatalogSource`. A new handler (`GetManufactureStockTakingHistoryHandler`) and its compiler-generated async state machine MUST be added to that allowlist — see _Specification Amendments_ below.
- **Two stock-taking surfaces remain.** `StockTakingController` (Catalog-owned, `/api/StockTaking/history`) keeps serving the Catalog endpoint; `ManufactureStockTakingController` (Manufacture-owned, `/api/manufacture-stock-taking/history`) is freed of its Catalog import and serves a Manufacture-owned use case that reads the same domain collection. This is acceptable because the URL is part of the public contract a React hook depends on, and the duplication exists at the use-case layer only (handler + DTOs), not at the data layer.
- **Domain layer is shared.** `CatalogAggregate`, `StockTakingRecord`, and `StockTakingType` (in `Anela.Heblo.Domain.Features.Catalog`) are correctly classified as Domain types in the boundary tests. The new handler can reference them directly without a violation — only `Anela.Heblo.Application.Features.Catalog.*` is forbidden.

The primary integration points are: (1) the new use-case folder under `Manufacture/UseCases/GetManufactureStockTakingHistory/`, (2) the controller method signature in `ManufactureStockTakingController`, (3) the `ManufactureCatalogAllowlist` in the boundary test, and (4) the auto-generated TypeScript client + one hook file. There are no DI registration changes — MediatR auto-discovers the handler from the assembly scan.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ HTTP                                                                        │
│   GET /api/manufacture-stock-taking/history                                 │
│   └─► ManufactureStockTakingController.GetManufactureStockTakingHistory     │
│         [FeatureAuthorize(Manufacture_MaterialInventory)]                   │
│         └─► IMediator.Send(GetManufactureStockTakingHistoryRequest)         │
└─────────────────────────┬───────────────────────────────────────────────────┘
                          │ (Manufacture-owned dispatch only — no Catalog import)
                          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Application — Manufacture slice                                             │
│   GetManufactureStockTakingHistoryHandler                                   │
│     ├─► IManufactureCatalogSource.GetByIdAsync(productCode)  ◄── ALREADY    │
│     │       (Manufacture-owned cross-module contract)            EXISTS     │
│     ├─► sort/page CatalogAggregate.StockTakingHistory                       │
│     └─► project StockTakingRecord → ManufactureStockTakingHistoryItemDto    │
│           (manual projection — see Decision 2)                              │
└─────────────────────────┬───────────────────────────────────────────────────┘
                          │ implemented by
                          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Application — Catalog slice (unchanged)                                     │
│   CatalogManufactureCatalogSourceAdapter ─► ICatalogRepository              │
│                                                                             │
│   StockTakingController + GetStockTakingHistoryHandler                      │
│   (Catalog-owned, untouched, continues to serve /api/StockTaking/history)   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Manufacture-owned handler reads `product.StockTakingHistory` directly via `IManufactureCatalogSource` — no new contract method

**Options considered:**
- **A. Reuse `IManufactureCatalogSource.GetByIdAsync` and read `product.StockTakingHistory` in the handler** (mirrors `GetStockTakingHistoryHandler` exactly).
- **B. Add a dedicated method, e.g. `GetStockTakingHistoryAsync(productCode, ...)`, to `IManufactureCatalogSource`** (push the LINQ/paging into the contract or its Catalog-side adapter).
- **C. Introduce a Manufacture-owned read repository interface (`IManufactureStockTakingHistoryReader`)** implemented by a Catalog-side adapter.

**Chosen approach:** A.

**Rationale:**
- `SubmitManufactureStockTakingHandler` already follows pattern A — it pulls the full `CatalogAggregate` via `IManufactureCatalogSource.GetByIdAsync` and operates on the aggregate's collections. Doing anything different here would split the convention.
- B and C both grow the cross-module contract surface for no behavioural gain. The history sort/page logic is trivial in-memory LINQ over a collection that's already materialised; pushing it across the contract just adds parameters to argue about.
- The deliberate `CatalogAggregate` leak through `IManufactureCatalogSource` is already documented and allowlisted in `ModuleBoundariesTests` with a follow-up tracked ("introduce Manufacture-owned ProductCatalogSnapshot DTO"). Piggybacking on the same allowlist entry is correct; introducing a parallel reader interface would dilute that follow-up.

#### Decision 2: Manual projection from `StockTakingRecord` to `ManufactureStockTakingHistoryItemDto` — do NOT use AutoMapper

**Options considered:**
- **A. Manual projection** in the handler (`.Select(r => new ManufactureStockTakingHistoryItemDto { ... })`).
- **B. Add `CreateMap<StockTakingRecord, ManufactureStockTakingHistoryItemDto>()`** to a Manufacture-owned `AutoMapper.Profile`.

**Chosen approach:** A.

**Rationale:**
- The projection has 7 trivial fields (`Id`, `Type`, `Code`, `AmountNew`, `AmountOld`, `Date`, `User`, `Error`); `Difference` is computed on the DTO. AutoMapper buys nothing here and makes the handler test require an `IMapper` mock the way `GetStockTakingHistoryHandler` does.
- Eliminating the `IMapper` dependency removes one constructor parameter and one mock from the unit tests. The Manufacture submit handler (the closest sibling) does not use AutoMapper either — it builds the response object directly. Stay consistent with the sibling.
- The Catalog-side `CatalogMappingProfile` already declares `CreateMap<StockTakingRecord, StockTakingHistoryItemDto>()` — but that's a Catalog-scoped map. Adding a Manufacture-scoped map alongside it would create two near-identical AutoMapper configurations registered from different assemblies, which is more boilerplate than `new DTO { ... }`.

#### Decision 3: Manual `StockTakingType` → `string` rendering on the DTO

**Options considered:**
- **A. Keep `Type` as `StockTakingType` enum on the DTO** (matches Catalog `StockTakingHistoryItemDto`).
- **B. Render `Type` as `string` on the DTO** (e.g. `Type = record.Type.ToString()`).

**Chosen approach:** A.

**Rationale:**
- The spec's FR-6 requires JSON-equivalent output. The Catalog DTO exposes `StockTakingType` as the C# enum; System.Text.Json will serialise it identically (numeric or named per the project's JSON options). Changing the C# type would risk a payload diff.
- `StockTakingType` lives in `Anela.Heblo.Domain.Features.Catalog.Stock`, which is allowed (Domain is shared).

#### Decision 4: Keep the controller action name `GetManufactureStockTakingHistory` unchanged

**Rationale:** NSwag generates the TypeScript method name from `<controllerTag>_<actionName>`, so the frontend hook's call `apiClient.manufactureStockTaking_GetManufactureStockTakingHistory(...)` continues to compile against the regenerated client without a hook-side rename. Only the imported response *type* changes.

## Implementation Guidance

### Directory / Module Structure

New files (Manufacture-owned):
```
backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureStockTakingHistory/
  ├── GetManufactureStockTakingHistoryRequest.cs
  ├── GetManufactureStockTakingHistoryResponse.cs   (also declares ManufactureStockTakingHistoryItemDto)
  └── GetManufactureStockTakingHistoryHandler.cs

backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetManufactureStockTakingHistory/
  └── GetManufactureStockTakingHistoryHandlerTests.cs
```

Edited files:
```
backend/src/Anela.Heblo.API/Controllers/ManufactureStockTakingController.cs   (remove Catalog using, retype action)
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs           (extend ManufactureCatalogAllowlist — see Specification Amendments)
frontend/src/api/hooks/useManufactureStockTaking.ts                            (swap imported type)
frontend/src/api/generated/api-client.ts                                       (auto-regenerated by NSwag on `dotnet build`)
```

Untouched (verify with grep after change):
```
backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockTakingHistory/   (all 3 files)
backend/src/Anela.Heblo.API/Controllers/StockTakingController.cs
backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockTakingHistoryHandlerTests.cs
frontend/src/api/hooks/useStockTaking.ts
```

### Interfaces and Contracts

```csharp
// New — Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory
public class GetManufactureStockTakingHistoryRequest
    : IRequest<GetManufactureStockTakingHistoryResponse>
{
    [Required]
    [StringLength(50, ErrorMessage = "Product code cannot exceed 50 characters")]
    public string ProductCode { get; set; } = null!;

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "date";
    public bool SortDescending { get; set; } = true;
}

public class GetManufactureStockTakingHistoryResponse : BaseResponse
{
    public List<ManufactureStockTakingHistoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public GetManufactureStockTakingHistoryResponse() : base() { }
    public GetManufactureStockTakingHistoryResponse(
        ErrorCodes errorCode,
        Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}

public class ManufactureStockTakingHistoryItemDto
{
    public int Id { get; set; }
    public StockTakingType Type { get; set; }       // domain enum — allowed
    public string Code { get; set; } = null!;
    public double AmountNew { get; set; }
    public double AmountOld { get; set; }
    public DateTime Date { get; set; }
    public string? User { get; set; }
    public string? Error { get; set; }
    public double Difference => AmountNew - AmountOld;
}

public class GetManufactureStockTakingHistoryHandler
    : IRequestHandler<GetManufactureStockTakingHistoryRequest, GetManufactureStockTakingHistoryResponse>
{
    private readonly IManufactureCatalogSource _catalogSource;

    public GetManufactureStockTakingHistoryHandler(IManufactureCatalogSource catalogSource)
    {
        _catalogSource = catalogSource;
    }

    public async Task<GetManufactureStockTakingHistoryResponse> Handle(
        GetManufactureStockTakingHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _catalogSource.GetByIdAsync(request.ProductCode, cancellationToken);
        if (product is null)
        {
            return new GetManufactureStockTakingHistoryResponse(
                ErrorCodes.ProductNotFound,
                new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
        }

        IEnumerable<StockTakingRecord> query = product.StockTakingHistory;
        query = request.SortBy?.ToLower() switch
        {
            "code"      => request.SortDescending ? query.OrderByDescending(x => x.Code)      : query.OrderBy(x => x.Code),
            "type"      => request.SortDescending ? query.OrderByDescending(x => x.Type)      : query.OrderBy(x => x.Type),
            "amountnew" => request.SortDescending ? query.OrderByDescending(x => x.AmountNew) : query.OrderBy(x => x.AmountNew),
            "amountold" => request.SortDescending ? query.OrderByDescending(x => x.AmountOld) : query.OrderBy(x => x.AmountOld),
            "user"      => request.SortDescending ? query.OrderByDescending(x => x.User)      : query.OrderBy(x => x.User),
            _           => request.SortDescending ? query.OrderByDescending(x => x.Date)      : query.OrderBy(x => x.Date),
        };

        var materialised = query.ToList();
        var pagedItems = materialised
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new ManufactureStockTakingHistoryItemDto
            {
                Id        = r.Id,
                Type      = r.Type,
                Code      = r.Code,
                AmountNew = r.AmountNew,
                AmountOld = r.AmountOld,
                Date      = r.Date,
                User      = r.User,
                Error     = r.Error,
            })
            .ToList();

        return new GetManufactureStockTakingHistoryResponse
        {
            Items      = pagedItems,
            TotalCount = materialised.Count,
            PageNumber = request.PageNumber,
            PageSize   = request.PageSize,
        };
    }
}
```

Controller change:
```csharp
// before: [FromQuery] GetStockTakingHistoryRequest request
// after:
[HttpGet("history")]
public async Task<ActionResult<GetManufactureStockTakingHistoryResponse>> GetManufactureStockTakingHistory(
    [FromQuery] GetManufactureStockTakingHistoryRequest request,
    CancellationToken cancellationToken = default)
{
    Logger.LogInformation(
        "Received manufacture stock taking history request for product code {ProductCode}, page {PageNumber}",
        request?.ProductCode, request?.PageNumber);

    var response = await _mediator.Send(request!, cancellationToken);
    return HandleResponse(response);
}
```
Drop the `using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;` line.

Frontend change in `frontend/src/api/hooks/useManufactureStockTaking.ts`:
- Replace `GetStockTakingHistoryResponse` import (from `../generated/api-client`) with `GetManufactureStockTakingHistoryResponse`.
- Update the `getStockTakingHistory` function's `Promise<...>` return type to match.
- The internal interface `GetStockTakingHistoryRequest` declared inside the hook file is hand-written (not imported), so the build does not require renaming it. Per the spec's preference for clarity, rename it to `GetManufactureStockTakingHistoryRequest` — leave the hook function name (`useStockTakingHistory`) as-is since it is part of the React component's call surface.

### Data Flow

```
Browser → React useStockTakingHistory hook
       → apiClient.manufactureStockTaking_GetManufactureStockTakingHistory(productCode, ...)
         (HTTP GET /api/manufacture-stock-taking/history?...)
       → [FeatureAuthorize(Manufacture_MaterialInventory)] gate
       → ManufactureStockTakingController.GetManufactureStockTakingHistory
       → IMediator.Send(GetManufactureStockTakingHistoryRequest)            ◄── Manufacture-owned
       → GetManufactureStockTakingHistoryHandler.Handle
       → IManufactureCatalogSource.GetByIdAsync(productCode)               ◄── Manufacture contract
       → CatalogManufactureCatalogSourceAdapter (Catalog-side impl)
       → ICatalogRepository.GetByIdAsync (Catalog-internal)
       ← CatalogAggregate { StockTakingHistory: [...] }
       ← sort/page/project to ManufactureStockTakingHistoryItemDto
       ← GetManufactureStockTakingHistoryResponse (JSON payload, identical shape to today)
```

ProductNotFound path: same as today — `BaseResponse` error envelope with `ErrorCodes.ProductNotFound` and `{ "ProductCode": "..." }` parameters.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ModuleBoundariesTests` fails after adding the new handler (the deliberate `CatalogAggregate` leak through `IManufactureCatalogSource` produces a new violation entry for the new handler and its compiler-generated async state machine). | **High** | Pre-emptively add three allowlist entries in `ManufactureCatalogAllowlist` — see _Specification Amendments_. Run `dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests"` after implementation to confirm no orphan violations. |
| TypeScript client regeneration produces a method name that drifts from what the hook calls (e.g. method renamed by NSwag because the controller action name changed). | Low | The controller **action method name** stays `GetManufactureStockTakingHistory`, so NSwag emits the same `manufactureStockTaking_GetManufactureStockTakingHistory` method. Only the request/response **type** names change. |
| JSON shape drift after rename (e.g. case differences, enum serialisation, missing `Difference` computed property). | Medium | The new DTO declares exactly the same property names and types as `StockTakingHistoryItemDto`, including `StockTakingType Type` (not `string`) and the computed `Difference`. Sanity-check with a manual diff of the response payload before/after on staging using the same `productCode`. |
| Two handlers reading the same `CatalogAggregate.StockTakingHistory` collection drift over time (e.g. one adds filtering, the other doesn't), producing inconsistent results between `/api/StockTaking/history` and `/api/manufacture-stock-taking/history`. | Medium | Out of scope per the spec, but worth a follow-up tracked alongside the existing `ProductCatalogSnapshot` follow-up: consider extracting a shared `StockTakingHistoryQuery` static helper in the Domain layer once a third caller appears. Add a brief code comment in both handlers cross-referencing each other. |
| The frontend internal `GetStockTakingHistoryRequest` interface is reused by another file the spec didn't enumerate. | Low | Before renaming, grep `frontend/src` for `GetStockTakingHistoryRequest` to confirm `useManufactureStockTaking.ts` is the only declaration site, and verify no other file imports it from this hook module. |
| Test for the new handler reaches lower coverage than 80% because of the sort `switch` branches. | Low | Use a parameterised `[Theory]` over `SortBy ∈ { "date", "code", "type", "amountnew", "amountold", "user", null }` with both `SortDescending = true/false` — six × two parameter rows hit every branch. Pattern: `SubmitManufactureStockTakingHandlerTests` (Moq + FluentAssertions). |
| Bypassing the Catalog handler also bypasses the Catalog-defined `IMapper` configuration; if the Catalog handler is later changed to enrich `StockTakingHistoryItemDto` with a new field, the Manufacture DTO will silently fall behind. | Low | Add a TODO comment in `ManufactureStockTakingHistoryItemDto` noting it mirrors `StockTakingHistoryItemDto` and pointing to the Catalog DTO. Long-term: the `ProductCatalogSnapshot` follow-up will eliminate the duplication. |

## Specification Amendments

**Amendment 1 — Add `ModuleBoundariesTests` allowlist entries (required to keep CI green).**

The spec's NFR-3 says `grep` on the controller must return no matches, but does not flag the boundary test side-effect. The new handler legitimately consumes `CatalogAggregate` through `IManufactureCatalogSource` — that's the deliberate pragmatic leak the existing `ManufactureCatalogAllowlist` documents. Three entries (mirroring the pattern used for `SubmitManufactureStockTakingHandler`) must be added to `ManufactureCatalogAllowlist` in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`:

```
"Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory.GetManufactureStockTakingHistoryHandler -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
"Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory.GetManufactureStockTakingHistoryHandler+<Handle>d__2 -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
// Add the StockTakingRecord entry only if the test flags it; current allowlist does not list StockTakingRecord for SubmitManufactureStockTakingHandler, but the Submit handler doesn't iterate the history collection. Verify after first run and add if needed:
// "Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory.GetManufactureStockTakingHistoryHandler+<Handle>d__2 -> Anela.Heblo.Domain.Features.Catalog.Stock.StockTakingRecord",
```
Confirm the actual async state machine suffix (`d__2`, `d__3`, etc.) with the test failure output and adjust. Add a comment block at the top of these entries referencing the `Manufacture → Catalog` follow-up (introduce `ProductCatalogSnapshot` DTO) so they retire alongside the existing entries.

**Amendment 2 — Drop AutoMapper from the new handler (FR-5 acceptance criteria clarification).**

The spec mentions AutoMapper as optional in the Dependencies section. The architecture decision (Decision 2 above) is to use manual projection. Update FR-5 acceptance criteria to expect the handler to be tested with a single `Mock<IManufactureCatalogSource>` dependency only — no `IMapper` mock. This matches `SubmitManufactureStockTakingHandlerTests`.

**Amendment 3 — Confirm hook-internal interface rename is not a behavioural requirement.**

FR-4 includes the internal `GetStockTakingHistoryRequest` rename as an open item ("or left in place"). Resolve as: rename to `GetManufactureStockTakingHistoryRequest` for naming consistency; it is a documentation/clarity change, not a contract change. Acceptance: no consumer outside `useManufactureStockTaking.ts` references the old name (verifiable via grep over `frontend/src`).

**Amendment 4 — DTO `Type` field declared as `StockTakingType` enum, not `string` (FR-6 reinforcement).**

Spec text in the API section says `"type": "Manufacture|Receive|..."` which reads like a string. Clarify that the C# DTO declares `public StockTakingType Type { get; set; }` so the JSON shape matches the existing Catalog DTO byte-for-byte under the same `JsonSerializerOptions`. The spec amendment is non-functional; it just disambiguates an implementation choice.

## Prerequisites

None — no infrastructure, configuration, migration, or feature-flag work is required.

- No DB schema change (NFR + spec confirmed).
- No DI registration change — MediatR `AddMediatR(...)` already scans the Application assembly and will pick up the new handler automatically.
- No new NuGet packages.
- No environment-variable, Key Vault, or App Settings change.
- The OpenAPI generation toolchain (`backend/src/Anela.Heblo.API` + NSwag) is already configured to regenerate `frontend/src/api/generated/api-client.ts` on `dotnet build`; no toolchain edit required.
- No feature-flag gate — the existing `Feature.Manufacture_MaterialInventory` authorization on the controller is preserved as-is.
```