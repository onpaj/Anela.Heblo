I have enough to write the review. Key findings: (1) mapping is via AutoMapper, not static mappers; (2) `IBankStatementImportRepository.GetByIdAsync(int id)` has no CancellationToken parameter — spec FR-3 conflicts with FR-5; (3) current `NotFound` returns a body, spec's example loses it; (4) `BankStatementImportDto` already exists as the item DTO — creating `GetBankStatementByIdResponse` duplicates it; (5) handlers use a Vertical Slice folder under `Application/Features/Bank/UseCases/`.

```markdown
# Architecture Review: GetBankStatement by Id — Dedicated MediatR Handler

## Skip Design: true

This is a backend-only internal refactor. HTTP contract, request/response JSON shapes, and database schema are unchanged. The OpenAPI-generated TypeScript client may pick up a renamed type, but no visual or UX work is involved.

## Architectural Fit Assessment

The proposal aligns with the project's established Vertical Slice + MediatR + Controllers conventions:

- `docs/architecture/development_guidelines.md` §ADR-003 mandates controllers as thin dispatchers and forbids business logic in controllers; the null-check → `404` decision today violates this.
- Existing slices under `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/` (`GetBankStatementList`, `GetBankAccounts`, `ImportBankStatement`) all follow the `{Request,Response,Handler}.cs` triplet pattern; the new slice fits the same mould.
- `IBankStatementImportRepository.GetByIdAsync(int id)` (Domain layer) is already implemented in `Persistence/Features/Bank/BankStatementImportRepository.cs:79` via `DbSet.FindAsync`. It is the right primitive for a keyed lookup but is currently unreachable from HTTP.
- MediatR registration is centralised in `ApplicationModule.cs:61` (`AddMediatR(...RegisterServicesFromAssembly(...))`); a new handler in this assembly is auto-discovered, no DI wiring required.

Main integration points: `BankStatementsController.GetBankStatement(int id)` (the call site), the existing `BankMappingProfile` (AutoMapper), and the existing `BankStatementImportDto` (already the single-item shape).

## Proposed Architecture

### Component Overview

```
HTTP Client
   │  GET /api/bank-statements/{id}
   ▼
BankStatementsController.GetBankStatement(id, ct)        ← thin dispatcher
   │  _mediator.Send(new GetBankStatementByIdRequest { Id = id }, ct)
   ▼
GetBankStatementByIdHandler (NEW)
   │  IBankStatementImportRepository.GetByIdAsync(request.Id)
   │  IMapper.Map<BankStatementImportDto>(entity)        ← reuses BankMappingProfile
   ▼
BankStatementImportRepository.GetByIdAsync               ← unchanged
   │  _context.BankStatements.FindAsync(id)
   ▼
EF Core / PostgreSQL
```

### Key Design Decisions

#### Decision 1: Response type — reuse `BankStatementImportDto`, do **not** create a new `GetBankStatementByIdResponse`

**Options considered:**
1. Create `GetBankStatementByIdResponse` as a new class mirroring `BankStatementImportDto` (spec FR-2 as written).
2. Create `GetBankStatementByIdResponse : BankStatementImportDto` (inheritance shim).
3. Use `BankStatementImportDto` directly as the handler's response type — `IRequest<BankStatementImportDto?>`.

**Chosen approach:** Option 3. The handler's contract is `IRequestHandler<GetBankStatementByIdRequest, BankStatementImportDto?>`.

**Rationale:** `BankStatementImportDto` already *is* the single-item shape — it is what the current endpoint serialises (`controller.cs:162: return Ok(statement)` where `statement` is `BankStatementImportDto`). Creating a parallel type with the same fields violates DRY, expands the OpenAPI surface area, and forces FR-6's mapping question (which is moot if no second DTO exists). The `BankMappingProfile.CreateMap<BankStatementImport, BankStatementImportDto>()` already handles the mapping for the list handler — the new handler reuses it via `IMapper`. Wire-format compatibility is guaranteed because the type is literally the same one the existing endpoint returns. This also keeps `GetBankStatementListResponse` (which extends `BaseResponse` and carries pagination metadata) where it belongs — list responses only.

#### Decision 2: Mapping — `IMapper` via existing AutoMapper profile, not a static mapper

**Options considered:**
1. Extract a `static BankStatementMapper.ToDto(BankStatementImport)` and call it from both handlers (spec FR-6 preference).
2. Inject `IMapper` and reuse `BankMappingProfile.CreateMap<BankStatementImport, BankStatementImportDto>()`.

**Chosen approach:** Option 2.

**Rationale:** The codebase already standardises on AutoMapper for this exact transformation (`backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs:11`). Introducing a parallel static mapper alongside the AutoMapper profile splits the source of truth and creates a drift hazard. The existing list handler (`GetBankStatementListHandler.cs:55`) uses `_mapper.Map<List<BankStatementImportDto>>(items)` — the new handler does `_mapper.Map<BankStatementImportDto>(entity)` against the same profile. Zero duplication, zero changes to the list handler.

#### Decision 3: `NotFound` body — preserve the existing payload

**Chosen approach:** Controller returns `NotFound(new { message = $"Bank statement import with ID {id} not found" })`, not `NotFound()`.

**Rationale:** The current controller (`BankStatementsController.cs:159`) returns a JSON object with a `message` field on 404. Spec FR-4's example (`return response is null ? NotFound() : Ok(response);`) silently drops that body, which is a wire-format change that NFR-3 explicitly forbids. Any client checking the 404 body would break. Preserve the message.

#### Decision 4: Repository `GetByIdAsync` signature — leave it alone, do not pass `CancellationToken`

**Chosen approach:** The handler calls `_repository.GetByIdAsync(request.Id)` without a cancellation token, matching the current interface.

**Rationale:** Spec FR-3 says "propagate `CancellationToken` to the repository call" but FR-5 says "no signature changes". The interface today is `Task<BankStatementImport?> GetByIdAsync(int id)` with no `CancellationToken` parameter (`IBankStatementImportRepository.cs:13`). Adding one would touch both the interface and the implementation, contradicting FR-5. Following the brief's "surgical" intent: leave the repository alone. Adding cancellation support to `GetByIdAsync` is a worthwhile follow-up but out of scope for this refactor — file a separate issue. The handler still accepts and propagates `CancellationToken` on its public surface (MediatR contract); only the inner repo call drops it.

#### Decision 5: Route attribute — keep `[HttpGet("{id}")]`, do **not** add `:int` constraint

**Chosen approach:** Leave the route template exactly as `[HttpGet("{id}")]`.

**Rationale:** Spec FR-4's code example uses `[HttpGet("{id:int}")]`, but the existing attribute is `[HttpGet("{id}")]`. Changing the constraint is a wire-level change to error behaviour for non-integer paths (today: 400 from model binding on `int id`; with `:int`: 404 from routing). NFR-3 mandates HTTP contract preservation. Keep the template literal-identical.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Bank/UseCases/
└── GetBankStatementById/                              # NEW slice
    ├── GetBankStatementByIdRequest.cs                  # class : IRequest<BankStatementImportDto?>
    └── GetBankStatementByIdHandler.cs                  # uses IMapper + IBankStatementImportRepository

backend/src/Anela.Heblo.API/Controllers/
└── BankStatementsController.cs                         # MODIFIED — only GetBankStatement(int id) body

backend/test/Anela.Heblo.Tests/Features/Bank/
└── GetBankStatementByIdHandlerTests.cs                 # NEW unit tests
    (Optional) Add controller integration test alongside existing BankStatementImportIntegrationTests.cs
```

**No new files for response DTO, no new mapper file, no new AutoMapper profile registration.** Total new C# files: 2 production + 1 test.

### Interfaces and Contracts

**Request (new):**
```csharp
namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;

public class GetBankStatementByIdRequest : IRequest<BankStatementImportDto?>
{
    public int Id { get; set; }
}
```
- Class (not record) per project rule.
- Nullable response signals "not found" without exceptions.
- Reuses `BankStatementImportDto` from `Application.Features.Bank.Contracts` — no new response type.

**Handler signature (new):**
```csharp
public class GetBankStatementByIdHandler
    : IRequestHandler<GetBankStatementByIdRequest, BankStatementImportDto?>
{
    // ctor: IBankStatementImportRepository, IMapper, ILogger<...>
}
```

**Controller (modified):**
```csharp
[HttpGet("{id}")]
public async Task<ActionResult<BankStatementImportDto>> GetBankStatement(int id, CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetBankStatementByIdRequest { Id = id }, cancellationToken);
    return response is null
        ? NotFound(new { message = $"Bank statement import with ID {id} not found" })
        : Ok(response);
}
```
- Return type stays `ActionResult<BankStatementImportDto>` (matches current declaration on line 142).
- `CancellationToken` parameter added (the original action lacks one).
- Top-level `try/catch (Exception)` is removed — global ASP.NET error handling already covers unhandled exceptions, and the catch was the only piece this action used. If the project enforces controller-level catches via a convention not visible here, keep the catch; otherwise drop it for consistency with `GetAccounts` (lines 28–33), which has no try/catch.
- `using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;` can be removed if no other action in this controller still references it (the `GetBankStatements` list endpoint still does, so keep it).
- Add `using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;`.

### Data Flow

**Found path:**
1. `GET /api/bank-statements/42` → controller action.
2. Controller builds `GetBankStatementByIdRequest { Id = 42 }`, sends via `IMediator.Send`.
3. MediatR routes to `GetBankStatementByIdHandler.Handle`.
4. Handler calls `_repository.GetByIdAsync(42)` → EF `FindAsync` → entity row.
5. Handler maps entity to `BankStatementImportDto` via `_mapper.Map<BankStatementImportDto>(entity)`.
6. Handler returns DTO. Controller wraps in `Ok(...)`. Status `200`, body is the DTO JSON — byte-identical to today.

**Not-found path:**
1. Steps 1–3 as above.
2. `_repository.GetByIdAsync(42)` returns `null`.
3. Handler returns `null` (no throw, no log spam beyond an info-level "not found" log line — optional).
4. Controller maps `null → NotFound(new { message = ... })`. Status `404`, body identical to today.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| OpenAPI/TypeScript client regenerates with a different type name and breaks frontend build. | LOW | Decision 1 reuses `BankStatementImportDto` — the generated TS type for this endpoint stays unchanged. Verify by running `npm run build` after `dotnet build`. |
| Wire format drift on `404` body (spec example drops the `message`). | MEDIUM | Decision 3 explicitly preserves the existing 404 body. Add an integration test asserting both the status code and the `message` field. |
| Repository signature mismatch (spec passes CT, interface has no CT). | LOW | Decision 4 — handler does not pass a CT to the repo. Document this in a follow-up issue if cancellation matters for this query. |
| Route constraint change (`{id:int}`) accidentally introduced from spec example. | LOW | Decision 5 — keep `[HttpGet("{id}")]`. Code review checklist item. |
| Hidden caller of `GetBankStatementListRequest { Id = ..., Take = 1 }` pattern elsewhere in code. | LOW | `grep -rn "GetBankStatementListRequest" backend/src` to verify the controller is the only consumer of the `Id` filter on the list request. If found elsewhere, leave them — out of scope. |
| List handler's `IMapper.Map<List<BankStatementImportDto>>` path differs subtly from single-entity `IMapper.Map<BankStatementImportDto>` (e.g., null handling). | LOW | AutoMapper applies the same `CreateMap` configuration for collections and single items. Add a unit test asserting field-by-field equality between the two paths for the same entity instance. |

## Specification Amendments

The following amendments to `spec.r1.md` are required:

1. **Remove FR-2 entirely.** Do not create `GetBankStatementByIdResponse`. The handler returns `BankStatementImportDto?` directly. See Decision 1.

2. **Amend FR-3:**
   - Handler signature becomes `IRequestHandler<GetBankStatementByIdRequest, BankStatementImportDto?>`.
   - Repository call is `_repository.GetByIdAsync(request.Id)` — no `CancellationToken` argument, because the interface does not accept one and FR-5 forbids interface changes. See Decision 4.
   - Mapping uses `IMapper` and the existing `BankMappingProfile`. See Decision 2.

3. **Amend FR-4:**
   - Keep route template as `[HttpGet("{id}")]` (no `:int` constraint). See Decision 5.
   - 404 response must keep the body: `NotFound(new { message = $"Bank statement import with ID {id} not found" })`. See Decision 3.
   - Controller's return type stays `ActionResult<BankStatementImportDto>`.

4. **Remove FR-6 entirely.** No new mapper file. Both handlers use the existing `BankMappingProfile` via `IMapper`. The acceptance criterion about asserting equality between two mapping paths is folded into FR-7 unit tests.

5. **Amend FR-7 test list:**
   - `GetBankStatementByIdHandlerTests.cs` with three cases: returns DTO when entity exists, returns `null` when repo returns `null`, calls repository exactly once with the expected id.
   - The "propagates `CancellationToken` to the repository" assertion is removed (Decision 4).
   - Add an integration test asserting `GET /api/bank-statements/{missingId}` returns 404 **with the `message` field**, not just status 404.
   - Add an integration test asserting `GET /api/bank-statements/{existingId}` returns a JSON body field-by-field equal to the same id queried via the list endpoint with `?id=...&take=1` — this is the wire-compatibility guarantee.

6. **Amend NFR-3:** Add an explicit assertion that the OpenAPI `operationId` for `GET /api/bank-statements/{id}` and the generated TypeScript response type **do not change**. (They won't, given Decision 1, but the architect should pin this.)

## Prerequisites

None. This refactor requires no migrations, no config, no new infrastructure, no new NuGet packages, and no upstream coordination.

Before starting:
- Run `dotnet build` on `main` to baseline the OpenAPI/TypeScript generation output.
- Run `npm run build` in `frontend/` to baseline the TypeScript client.
- After implementation, re-run both and diff the generated artifacts — expected diff is zero.
```