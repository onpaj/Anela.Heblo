# Specification: GetBankStatement by Id — Dedicated MediatR Handler

## Summary
Replace the controller-level workaround in `BankStatementsController.GetBankStatement(int id)` with a purpose-built MediatR handler (`GetBankStatementByIdHandler`) that calls `IBankStatementImportRepository.GetByIdAsync` directly. The controller becomes a thin dispatcher: send the request, translate `null` to `404 NotFound`, or return `200 Ok`. This removes business logic from the controller, eliminates the misuse of the list handler for a point-lookup, and activates an existing-but-unused repository method.

## Background
Project guidelines mandate that **business logic must be in MediatR handlers, not controllers**. Today, `BankStatementsController.GetBankStatement(int id)` (lines 142–169 of `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`) violates this rule in two ways:

1. It constructs a `GetBankStatementListRequest { Id = id, Take = 1 }`, sends it to the **list** handler, then calls `FirstOrDefault()` on the resulting collection — leaking pagination semantics into a point-lookup path.
2. The "if null, return `NotFound`" decision is business logic embedded in the controller rather than expressed as the handler's contract.

Meanwhile, `IBankStatementImportRepository.GetByIdAsync(int id)` (declared in `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`, implemented in `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`) is not reached by any handler. It exists, is tested at the repository level, and is the right tool — but is currently dead code from the perspective of HTTP traffic.

This refactor was identified by the daily arch-review routine on 2026-06-03 and is purely internal: HTTP contract, request/response shapes visible to clients, and database schema all remain unchanged.

## Functional Requirements

### FR-1: New MediatR request `GetBankStatementByIdRequest`
Define a request DTO under `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/`.

- Implements `IRequest<GetBankStatementByIdResponse?>` (nullable response signals "not found").
- Contains a single property `int Id`.
- Per project rules, the request is a **class**, not a C# record (DTOs are classes to keep OpenAPI client generators happy).

**Acceptance criteria:**
- `GetBankStatementByIdRequest.cs` exists in the specified folder.
- Type is a class with public parameterless constructor and `int Id { get; set; }`.
- Type implements `IRequest<GetBankStatementByIdResponse?>`.

### FR-2: New response type `GetBankStatementByIdResponse`
Define a response DTO with the same fields currently surfaced by `GetBankStatementListResponse.Items[0]` for an `Id` lookup.

- The response **must be byte-for-byte JSON-equivalent** to what `GetBankStatement(int id)` currently returns when the record is found, so the existing OpenAPI/TypeScript client and any external consumers continue to work without changes.
- Response is a class (not a record).

**Acceptance criteria:**
- `GetBankStatementByIdResponse.cs` exists in the specified folder.
- Property names, types, casing, and nullability mirror the item shape of `GetBankStatementListResponse.Items` exactly.
- Manually diffing a serialized response against the current endpoint output shows no differences for the same `Id`.

### FR-3: New handler `GetBankStatementByIdHandler`
Implement `IRequestHandler<GetBankStatementByIdRequest, GetBankStatementByIdResponse?>`.

Behavior:
- Calls `IBankStatementImportRepository.GetByIdAsync(request.Id, cancellationToken)`.
- If repository returns `null` → handler returns `null`.
- Otherwise → maps the domain entity to `GetBankStatementByIdResponse` and returns it.
- Mapping reuses the same projection logic used by the list handler for a single item (extract a shared mapper if duplication arises; see FR-6).
- No I/O beyond the single repository call. No pagination, no `Take`, no `FirstOrDefault` on a list.

**Acceptance criteria:**
- Handler resides in `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/`.
- Handler is registered automatically by the existing MediatR assembly scan (no manual DI wiring needed — verify by integration test).
- Cancellation token is propagated to the repository call.
- For a missing id, handler returns `null` without throwing.
- For an existing id, the returned response matches FR-2 exactly.

### FR-4: Controller becomes a thin dispatcher
Refactor `BankStatementsController.GetBankStatement(int id)` to:

```csharp
[HttpGet("{id:int}")]
public async Task<IActionResult> GetBankStatement(int id, CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetBankStatementByIdRequest { Id = id }, cancellationToken);
    return response is null ? NotFound() : Ok(response);
}
```

- The controller no longer references `GetBankStatementListRequest`, `Take`, or `FirstOrDefault` for this endpoint.
- HTTP route, verb, attributes, and authorization remain unchanged.
- Response status codes remain `200 OK` (found) / `404 NotFound` (missing). The `NotFound()` body shape (empty vs. payload) must match the existing behavior — preserve whatever the current controller returns to avoid breaking clients.

**Acceptance criteria:**
- Diff of the controller shows only the body of `GetBankStatement(int id)` changed; route, attributes, and signature unchanged (except adding a `CancellationToken` parameter if not already present).
- A `curl` / integration test against the endpoint returns the same status codes and JSON bodies as before for both the found and not-found cases.

### FR-5: No changes to the repository
`IBankStatementImportRepository.GetByIdAsync(int id, CancellationToken)` is used as-is. No new methods, no signature changes, no implementation changes.

**Acceptance criteria:**
- `git diff` shows zero changes under `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` and `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`.

### FR-6: Avoid duplicating the entity → DTO mapping
The list handler already projects `BankStatementImport` → `GetBankStatementListResponse.Items[i]`. The new handler must produce a structurally identical projection for the single-item case.

- Preferred: extract a private static mapper (e.g. `BankStatementMapper.ToDto(BankStatementImport)`) in `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/` and call it from both handlers.
- Acceptable if simpler: inline a small projection in the new handler and a follow-up issue documenting the duplication. The brief is a surgical refactor; extracting a shared mapper is in scope only if it's a trivial lift.

**Acceptance criteria:**
- The two handlers produce equivalent DTOs for the same domain entity (verified by a unit test that constructs an entity and asserts equality between `listHandler.Map(entity)` and `byIdHandler.Map(entity)`, or by sharing the mapper).
- If a shared mapper is extracted, the list handler is updated to use it.

### FR-7: Test coverage
Add tests under `backend/test/Anela.Heblo.Tests/` (mirror the existing Bank feature test layout):

- **Unit test** for `GetBankStatementByIdHandler`:
  - Returns mapped response when repository returns an entity.
  - Returns `null` when repository returns `null`.
  - Propagates `CancellationToken` to the repository.
- **Integration / controller test** for the endpoint:
  - `GET /api/bank-statements/{existingId}` → `200 OK` with expected JSON.
  - `GET /api/bank-statements/{missingId}` → `404 NotFound`.
  - JSON shape matches the pre-refactor output (snapshot or field-by-field assertion).

**Acceptance criteria:**
- All new tests pass locally and in CI.
- Existing Bank-feature tests continue to pass with no modifications, except updates necessitated by an extracted shared mapper (FR-6).
- Coverage of the new handler ≥ 80% per the project's testing rule.

## Non-Functional Requirements

### NFR-1: Performance
- Endpoint latency must not regress. `GetByIdAsync` is a single keyed lookup; expected to be at least as fast as the current list-handler-with-`Take=1` path.
- No additional database round-trips introduced.

### NFR-2: Security
- No changes to authorization. Whatever `[Authorize]` / policy attributes apply to the existing endpoint apply unchanged.
- No new inputs accepted; the `id` route parameter is already constrained to `int` and validated by ASP.NET model binding.

### NFR-3: Backwards compatibility
- HTTP contract (route, verb, request shape, response JSON shape, status codes) is unchanged.
- OpenAPI spec and the generated TypeScript client must continue to work without regeneration changes that affect callers. If the regenerated client produces a different type name for the response (e.g. `GetBankStatementByIdResponse` vs `GetBankStatementListResponseItem`), update the small number of frontend call sites accordingly — but the wire format must remain identical.

### NFR-4: Code quality
- Conforms to project guidelines in `docs/architecture/development_guidelines.md` (Vertical Slice layout under `Features/Bank/UseCases/GetBankStatementById/`).
- Passes `dotnet build` and `dotnet format` cleanly.
- No new warnings introduced.

## Data Model
No data model changes. Existing entity `BankStatementImport` and existing table/columns are untouched. The new DTO `GetBankStatementByIdResponse` mirrors the existing list-item DTO and is purely a transport concern.

## API / Interface Design

### HTTP endpoint (unchanged externally)
```
GET /api/bank-statements/{id}
→ 200 OK  + GetBankStatementByIdResponse (JSON, shape identical to current list-item response)
→ 404 Not Found (body shape preserved from current behavior)
```

### MediatR request/response (new internal contract)
```
Request:  GetBankStatementByIdRequest  { int Id }                 // class, IRequest<GetBankStatementByIdResponse?>
Response: GetBankStatementByIdResponse { …fields mirror list-item DTO… }   // class
Handler:  GetBankStatementByIdHandler  : IRequestHandler<…>       // calls IBankStatementImportRepository.GetByIdAsync
```

### Folder layout (Vertical Slice)
```
backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/
├── GetBankStatementByIdRequest.cs
├── GetBankStatementByIdResponse.cs
└── GetBankStatementByIdHandler.cs
```
(If a shared mapper is extracted, place it at `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/BankStatementMapper.cs` or similar — final location to be confirmed by the architect.)

## Dependencies
- **MediatR** — already in use across the project; no version change.
- **Existing `IBankStatementImportRepository.GetByIdAsync`** — must remain in place.
- **Existing OpenAPI client generation pipeline** — will regenerate types on build; verify the frontend still compiles after regeneration.
- No new NuGet packages, no new services, no new configuration.

## Out of Scope
- Changes to `GetBankStatementListRequest` / `GetBankStatementListHandler` beyond (optionally) consuming a shared mapper extracted in FR-6.
- Changes to other Bank endpoints (`POST`, `PUT`, list, etc.).
- Repository interface or implementation changes.
- Database schema changes or migrations.
- Frontend behavior changes beyond regenerating the OpenAPI TypeScript client to pick up the (renamed) response type.
- Adding caching, observability, or retry logic to the new handler.
- Rewriting the controller's other actions, even if they exhibit similar smells.

## Open Questions
None.

## Status: COMPLETE