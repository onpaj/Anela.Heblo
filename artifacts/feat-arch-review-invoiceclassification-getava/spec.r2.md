# Specification: Refactor GetAvailableRuleTypes to MediatR Handler

## Summary
The `InvoiceClassificationController.GetAvailableRuleTypes()` endpoint currently bypasses MediatR, performs DTO projection inside the controller, and takes a direct dependency on the Domain interface `IEnumerable<IClassificationRule>`. This refactor moves the projection into a new `GetClassificationRuleTypesHandler` in the Application layer, making the controller action a thin MediatR dispatch consistent with every other action in the file. The HTTP contract is unchanged.

## Background
The `InvoiceClassification` module follows a consistent MediatR + Vertical Slice pattern (see `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/`): every endpoint in `InvoiceClassificationController` dispatches a `...Request` through `IMediator` to a feature-scoped handler that returns a `...Response` class. The `GetAvailableRuleTypes()` action (lines 75–86 of `backend/src/Anela.Heblo.API/Controllers/InvoiceClassificationController.cs`) is the lone exception:

```csharp
[HttpGet("rule-types")]
public ActionResult<List<ClassificationRuleTypeDto>> GetAvailableRuleTypes()
{
    var ruleTypes = _classificationRules.Select(rule => new ClassificationRuleTypeDto
    {
        Identifier = rule.Identifier,
        DisplayName = rule.DisplayName,
        Description = rule.Description
    }).ToList();

    return Ok(ruleTypes);
}
```

Two issues:

1. **Layering violation** — The controller takes `IEnumerable<IClassificationRule>` directly (`InvoiceClassificationController.cs:22, 24`). `IClassificationRule` lives in `Anela.Heblo.Domain.Features.InvoiceClassification`, so the API project depends on a Domain abstraction without going through Application. Per `docs/architecture/development_guidelines.md`, the dependency direction must be API → Application → Domain.
2. **Business logic in controller** — Iterating rules and projecting them to `ClassificationRuleTypeDto` is application logic. The same guideline document states: *"Business logic must be in MediatR handlers, NOT in controllers."*

The shape also blocks handler-level unit testing of the projection (today only controller/integration tests can cover it).

This refactor is purely structural — no behavior or contract change. Route, verb, status codes, response JSON shape, and element ordering remain identical. Only the internal wiring moves.

### Conventions confirmed by codebase inspection

The previously-open questions in `spec.r1.md` are resolved by direct inspection of sibling handlers (the `answers.r1.md` artifact provided addressed a different feature and is not applicable here):

- **Request vs. Query naming.** Sibling handlers in the module use `...Request` (e.g. `GetAccountingTemplatesRequest`, `GetClassificationRulesRequest`, `GetClassificationHistoryRequest`). This refactor uses `GetClassificationRuleTypesRequest`.
- **Response wrapper convention.** Every sibling handler returns a dedicated `...Response` class extending `Anela.Heblo.Application.Shared.BaseResponse` (e.g. `GetAccountingTemplatesResponse { List<AccountingTemplateDto> Templates }`, `GetClassificationRulesResponse { List<ClassificationRuleDto> Rules }`). This refactor uses `GetClassificationRuleTypesResponse { List<ClassificationRuleTypeDto> RuleTypes }`.
- **Test project location.** Existing handler tests for this module live at `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/` (e.g. `ClassifyInvoicesHandlerTests.cs`). New tests go there.

### Important caveat — HTTP response shape

Because the existing action returns the bare list (`return Ok(ruleTypes)` where `ruleTypes` is `List<ClassificationRuleTypeDto>`) while sibling actions return their `...Response` envelope object, this refactor faces a tension:

- **Follow sibling convention strictly** → wrap result in `GetClassificationRuleTypesResponse` → **breaking change** to the JSON shape (consumers would now see `{ ruleTypes: [...] }` instead of `[...]`).
- **Preserve HTTP contract** (FR-4) → controller must unwrap the response envelope before returning `Ok(...)`.

This spec takes the second path: the **handler returns the envelope** (matches sibling convention internally) and the **controller unwraps it** to keep the external JSON shape identical. This preserves FR-4 (no consumer impact, no OpenAPI break) while keeping the Application layer consistent with siblings.

## Functional Requirements

### FR-1: New MediatR Request and Response

**Acceptance criteria:**
- New file `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationRuleTypes/GetClassificationRuleTypesRequest.cs` defines `public class GetClassificationRuleTypesRequest : IRequest<GetClassificationRuleTypesResponse>` with no properties.
- New file `.../GetClassificationRuleTypes/GetClassificationRuleTypesResponse.cs` defines `public class GetClassificationRuleTypesResponse : BaseResponse` with a single property `public List<ClassificationRuleTypeDto> RuleTypes { get; set; } = new();` and a parameterless constructor plus the `(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)` constructor matching the `BaseResponse` convention used by siblings (see `GetAccountingTemplatesResponse.cs`).
- Both files declare namespace `Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRuleTypes`.
- Per project rule, both types are `class`, not `record`.

### FR-2: New MediatR Handler

**Acceptance criteria:**
- New file `.../GetClassificationRuleTypes/GetClassificationRuleTypesHandler.cs` defines `public class GetClassificationRuleTypesHandler : IRequestHandler<GetClassificationRuleTypesRequest, GetClassificationRuleTypesResponse>`.
- Handler constructor takes a single parameter: `IEnumerable<IClassificationRule> classificationRules`, stored in a `private readonly` field.
- `Handle(GetClassificationRuleTypesRequest request, CancellationToken cancellationToken)` projects each rule into a `ClassificationRuleTypeDto` with the same three field mappings as the current controller (`Identifier`, `DisplayName`, `Description`), returns a populated `GetClassificationRuleTypesResponse`.
- The method body is `Task.FromResult(...)` (no async I/O is needed), or the method is `async` with no `await` and a suppression — the project's other simple handlers may guide which is preferred, but functional correctness is the only hard requirement.
- The handler is picked up automatically by the existing MediatR assembly-scan registration in the Application layer; no manual `services.AddScoped<...>` is required.

### FR-3: Refactor Controller Action

**Acceptance criteria:**
- `InvoiceClassificationController.GetAvailableRuleTypes()` becomes:
  ```csharp
  [HttpGet("rule-types")]
  public async Task<ActionResult<List<ClassificationRuleTypeDto>>> GetAvailableRuleTypes(CancellationToken cancellationToken)
  {
      var response = await _mediator.Send(new GetClassificationRuleTypesRequest(), cancellationToken);
      return Ok(response.RuleTypes);
  }
  ```
- The action signature changes from synchronous `ActionResult<List<ClassificationRuleTypeDto>>` to `Task<ActionResult<List<ClassificationRuleTypeDto>>>` and accepts a `CancellationToken`, matching the other actions in the file (e.g. lines 31, 39, 46, 53, 61, 69, 89, 97, 120, 132).
- The HTTP route (`"rule-types"`), verb (`HttpGet`), `ActionResult<T>` envelope, response JSON shape (bare array of `ClassificationRuleTypeDto`), and status code (200 OK) are unchanged.
- A `using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRuleTypes;` directive is added to the controller file.

### FR-4: Remove Domain Dependency From Controller

**Acceptance criteria:**
- The constructor parameter `IEnumerable<IClassificationRule> classificationRules` is removed from `InvoiceClassificationController`.
- The field `private readonly IEnumerable<IClassificationRule> _classificationRules;` is removed.
- The controller constructor is updated to take only `IMediator mediator`.
- The `using Anela.Heblo.Domain.Features.InvoiceClassification;` directive (line 12) is removed from the controller file (no other reference to the Domain namespace exists in this file).
- Building the API project does not produce a compile-time reference from `InvoiceClassificationController` to `IClassificationRule`.

### FR-5: Behavior Parity

**Acceptance criteria:**
- A GET to `/api/InvoiceClassification/rule-types` returns a JSON array (not an object) with the same number of elements and the same per-element shape (`{ identifier, displayName, description }`) as before the change, given identical DI registrations of `IClassificationRule` implementations.
- Element ordering matches the DI-resolution order produced today — no new sort is introduced.
- No new query string parameters, headers, or response fields are added or removed.
- The OpenAPI document regenerates without breaking changes to consumers. The generated TypeScript client (`frontend/src/api/generated/`) requires no manual fixups beyond automatic regeneration.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The work is in-process enumeration of a DI-resolved collection (typically <20 items). Latency and throughput are unchanged. An extra MediatR pipeline dispatch is introduced but its overhead is negligible relative to network and serialization costs already present in every other endpoint in the controller.

### NFR-2: Security
No change. The endpoint has no `[Authorize]` attribute at the action level today; if a class-level policy applies, it continues to apply unchanged. No new data is exposed, no input is accepted from the client.

### NFR-3: Testability

**Acceptance criteria:**
- New file `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetClassificationRuleTypesHandlerTests.cs` is created.
- Tests use xUnit + FluentAssertions + Moq/NSubstitute consistent with `ClassifyInvoicesHandlerTests.cs` in the same folder.
- Test cases at minimum:
  1. `Handle_WithEmptyRuleCollection_ReturnsEmptyList` — empty `IEnumerable<IClassificationRule>` → response with empty `RuleTypes`, `Success == true`.
  2. `Handle_WithMultipleRules_ProjectsEachToDto` — three fake rules → response contains three DTOs in the same order, each with `Identifier`/`DisplayName`/`Description` matching the source.
  3. `Handle_PreservesEnumerationOrder` — explicit order check on a multi-element list.
- Tests construct fake `IClassificationRule` implementations (mocked stubs returning the three properties) without spinning up a DI container or the API host.

### NFR-4: Architectural Compliance

**Acceptance criteria:**
- The API project no longer references `IClassificationRule` from `InvoiceClassificationController`. (It may still reference the Domain namespace elsewhere in the project; this requirement is scoped to the controller file.)
- The new handler resides in `Application/Features/InvoiceClassification/UseCases/GetClassificationRuleTypes/`, matching the Vertical Slice convention used by all sibling use cases.
- No business logic (projection, mapping, filtering, ordering) remains in `GetAvailableRuleTypes()`. The action body is exactly two statements: dispatch and `Ok(...)`.
- `dotnet build` succeeds, `dotnet format` reports no diff on the changed files, and existing tests remain green.

## Data Model

No persistence changes. Types involved:

| Type | Layer | Status | Purpose |
|------|-------|--------|---------|
| `IClassificationRule` | Domain | Unchanged | Provides `Identifier`, `DisplayName`, `Description` properties. |
| `ClassificationRuleTypeDto` | Application/Contracts | Unchanged | `class` (not `record`); shape is `{ Identifier, DisplayName, Description }`. |
| `GetClassificationRuleTypesRequest` | Application/UseCases | **New** | Empty marker; `IRequest<GetClassificationRuleTypesResponse>`. |
| `GetClassificationRuleTypesResponse` | Application/UseCases | **New** | Extends `BaseResponse`; carries `List<ClassificationRuleTypeDto> RuleTypes`. |

DI relationships: the new handler depends on `IEnumerable<IClassificationRule>` resolved by the existing DI registrations (untouched). The request/response types live alongside the handler in the feature folder.

## API / Interface Design

### External (HTTP) — unchanged

```
GET /api/InvoiceClassification/rule-types
→ 200 OK
[
  { "identifier": "...", "displayName": "...", "description": "..." },
  ...
]
```

### Internal (process)

```
Controller.GetAvailableRuleTypes(ct)
    │
    ▼
IMediator.Send(GetClassificationRuleTypesRequest, ct)
    │
    ▼
GetClassificationRuleTypesHandler.Handle(...)
    │ uses
    ▼
IEnumerable<IClassificationRule>   ◄── ctor-injected
    │ projects to
    ▼
GetClassificationRuleTypesResponse { RuleTypes: List<ClassificationRuleTypeDto> }
    │
    ▼
Controller returns Ok(response.RuleTypes)   ← unwraps to keep external array shape
```

### Controller after refactor (illustrative, full action body)

```csharp
[HttpGet("rule-types")]
public async Task<ActionResult<List<ClassificationRuleTypeDto>>> GetAvailableRuleTypes(CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetClassificationRuleTypesRequest(), cancellationToken);
    return Ok(response.RuleTypes);
}
```

### Controller constructor after refactor

```csharp
private readonly IMediator _mediator;

public InvoiceClassificationController(IMediator mediator)
{
    _mediator = mediator;
}
```

## Dependencies

- **MediatR** — already in use across the module; no version change.
- **Existing DI registrations of `IClassificationRule` implementations** — must remain registered (likely in `InvoiceClassificationModule.cs`) so the handler receives them via `IEnumerable<IClassificationRule>`. The refactor does not modify these registrations.
- **`Anela.Heblo.Application.Shared.BaseResponse`** — base class for the new response, already used by every sibling response in the module.
- **OpenAPI client generation** — runs on build; consumer (frontend TypeScript client) regenerates automatically. No manual frontend changes expected because the response contract is unchanged.

## Out of Scope

- Changing the `ClassificationRuleTypeDto` shape, fields, or naming.
- Changing the HTTP route, verb, response status codes, or external JSON shape.
- Adding pagination, filtering, sorting, or query parameters to the endpoint.
- Refactoring other actions in `InvoiceClassificationController` (they already use MediatR correctly).
- Touching `IClassificationRule`, its concrete implementations, or their DI registration.
- Frontend changes beyond automatic OpenAPI client regeneration.
- Adding or modifying authorization policies.
- Performance optimizations or caching of the rule list.
- Reorganizing or renaming the `IClassificationRule` interface across layers.
- Mapping via AutoMapper — this projection is small and explicit; sibling handlers use AutoMapper only when the source/target are persistence entities, not DI-resolved metadata.

## Open Questions

None.

## Status: COMPLETE