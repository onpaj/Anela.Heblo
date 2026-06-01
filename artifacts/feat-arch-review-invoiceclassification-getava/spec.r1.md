# Specification: Refactor GetAvailableRuleTypes to MediatR Handler

## Summary
The `InvoiceClassificationController.GetAvailableRuleTypes()` endpoint currently bypasses MediatR and contains business logic in the controller, violating Clean Architecture's dependency rule and the project's development guidelines. This refactor moves the rule-type projection logic into a new `GetClassificationRuleTypesHandler` in the Application layer, making the controller action a thin MediatR dispatch matching the rest of the module.

## Background
The `InvoiceClassification` module follows a consistent MediatR + Vertical Slice pattern: every other endpoint in `InvoiceClassificationController` dispatches a request through `IMediator` to a feature-scoped handler under `Application/Features/InvoiceClassification/UseCases/`. The `GetAvailableRuleTypes()` action is the lone exception. It was implemented with two issues:

1. **Layering violation** — The controller takes an `IEnumerable<IClassificationRule>` dependency directly. `IClassificationRule` is a Domain abstraction, and Domain interfaces should not be wired into the API layer. The Application layer is the correct seam for orchestrating Domain types and projecting them to DTOs.
2. **Business logic in controller** — Iterating the rules and projecting each to `ClassificationRuleTypeDto` is application logic. Per `docs/architecture/development_guidelines.md`, controllers must not contain business logic. They dispatch via MediatR; handlers do the work.

The current shape also blocks unit testing the projection via handler tests (today it can only be exercised through controller/integration tests).

This refactor is purely structural — no behavior or contract change. The HTTP response shape, status codes, and route remain identical. Only the internal wiring moves.

## Functional Requirements

### FR-1: New MediatR Query and Handler
Introduce a query/handler pair that returns the list of available classification rule types.

**Acceptance criteria:**
- A new query class `GetClassificationRuleTypesRequest` (or matching project naming convention, e.g. `GetClassificationRuleTypesQuery`) exists under `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationRuleTypes/`.
- The query implements `IRequest<GetClassificationRuleTypesResponse>` (or returns `List<ClassificationRuleTypeDto>` consistently with the module's existing handler return-type convention — see Open Questions).
- A new handler class `GetClassificationRuleTypesHandler` exists in the same folder and implements `IRequestHandler<...>`.
- The handler accepts `IEnumerable<IClassificationRule>` via constructor injection.
- The handler iterates the injected rules and projects each into `ClassificationRuleTypeDto`, preserving the exact projection logic that exists today in the controller (same property mapping, same ordering).
- The handler returns the projected list synchronously-completed `Task` (no I/O involved).
- The handler is registered automatically via the existing MediatR assembly-scan registration in the Application layer (no manual `services.AddScoped` needed unless other handlers in the module require it).

### FR-2: Controller Action Becomes One-Line Dispatch
Refactor `InvoiceClassificationController.GetAvailableRuleTypes()` to dispatch through MediatR exactly like every other action in the file.

**Acceptance criteria:**
- The action body is a single `await _mediator.Send(new GetClassificationRuleTypesRequest(), cancellationToken)` call (or equivalent), wrapped in the same `Ok(...)` return pattern used by the other actions in the controller.
- The action accepts a `CancellationToken` parameter consistent with the rest of the controller.
- The HTTP route, verb, response DTO shape, and status codes (200 OK + JSON body) are unchanged.
- Action-level attributes (`[HttpGet]`, route, `[ProducesResponseType]`, etc.) match what is currently declared on the action.

### FR-3: Remove Domain Dependency From Controller
Strip the direct `IEnumerable<IClassificationRule>` dependency from the API layer.

**Acceptance criteria:**
- `InvoiceClassificationController`'s constructor no longer declares `IEnumerable<IClassificationRule>` as a parameter.
- Any backing field that held the rules (e.g. `_classificationRules`) is removed.
- `using` statements that referenced the Domain rule interface are removed from the controller file if no longer used.
- The API project's compilation graph no longer reaches `IClassificationRule` from this controller; the Domain dependency is isolated to the new Application-layer handler.

### FR-4: Behavior Parity
The externally visible behavior of the endpoint is identical before and after the refactor.

**Acceptance criteria:**
- A request to the existing route returns a JSON array with the same number of elements and same DTO content as before the change, given identical DI registrations of `IClassificationRule` implementations.
- Element ordering is preserved (matches whatever DI resolution order produces today — no new sort is introduced).
- No new query string parameters, headers, or response fields are added.
- The OpenAPI document re-generates without breaking changes to consumers; the generated TypeScript client requires no manual fixups beyond regeneration.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The work is in-process enumeration of a DI-resolved collection (typically <20 items). Response time and throughput are unchanged.

### NFR-2: Security
No change. The endpoint's authorization attributes, if any, are preserved exactly as they are on the existing action.

### NFR-3: Testability
The projection logic must be unit-testable without spinning up the API.

**Acceptance criteria:**
- A unit test class for `GetClassificationRuleTypesHandler` exists under `backend/test/Anela.Heblo.Tests/` (or the project's standard handler-test location — see Open Questions on test project path).
- Tests cover: (a) empty rule collection returns empty list, (b) a collection of fakes is projected correctly, (c) projection field mapping (each `ClassificationRuleTypeDto` property is populated from the expected source on `IClassificationRule`).
- Tests use plain fakes/mocks of `IClassificationRule`; no DI container needed.

### NFR-4: Architectural Compliance
The refactor must satisfy the project's stated layering rules.

**Acceptance criteria:**
- API → Application → Domain dependency direction is preserved. The API project does not reference `IClassificationRule` directly through this controller after the change.
- The handler resides in the Application layer's `InvoiceClassification` feature folder, matching the Vertical Slice organization used by sibling use cases.
- No business logic (projection, filtering, mapping) remains in the controller action.

## Data Model

No persistence changes. The relevant types are:

- `IClassificationRule` (Domain) — existing interface, unchanged. Provides the metadata being projected (rule type identifier, display name, and whatever other fields the current projection reads).
- `ClassificationRuleTypeDto` (Application/Contracts) — existing DTO returned by the endpoint, unchanged. Per project rules, this remains a **class**, not a record.
- `GetClassificationRuleTypesRequest` — new MediatR request marker; carries no payload.
- `GetClassificationRuleTypesResponse` — optional wrapper if the module's convention is to wrap list returns in a response envelope; otherwise the handler returns `List<ClassificationRuleTypeDto>` directly. (See Open Questions.)

Relationships: the handler depends on `IEnumerable<IClassificationRule>` resolved by DI; the request/response live alongside the handler in the feature folder.

## API / Interface Design

**Endpoint (unchanged externally):**
- Route, verb, and response shape as currently defined on `InvoiceClassificationController.GetAvailableRuleTypes()`.
- Response body: JSON array of `ClassificationRuleTypeDto`.

**Internal interface (new):**
```
GetClassificationRuleTypesRequest  -->  GetClassificationRuleTypesHandler  -->  List<ClassificationRuleTypeDto>
                                              ^
                                              | ctor-injected
                                       IEnumerable<IClassificationRule>
```

**Controller after change (illustrative):**
```csharp
[HttpGet("rule-types")]  // exact attributes preserved from current action
public async Task<IActionResult> GetAvailableRuleTypes(CancellationToken cancellationToken)
{
    var result = await _mediator.Send(new GetClassificationRuleTypesRequest(), cancellationToken);
    return Ok(result);
}
```

## Dependencies

- **MediatR** — already in use across the module; no version change.
- **Existing DI registrations of `IClassificationRule` implementations** — must remain registered so the handler can receive them via `IEnumerable<IClassificationRule>`. The refactor does not modify these registrations.
- **OpenAPI client generation** — runs on build; consumers (frontend TypeScript client) regenerate automatically. No manual frontend changes expected because the response contract is unchanged.

## Out of Scope

- Changing the response DTO (`ClassificationRuleTypeDto`) shape, fields, or naming.
- Changing the HTTP route, verb, or status codes.
- Adding pagination, filtering, sorting, or query parameters to the endpoint.
- Refactoring other actions in `InvoiceClassificationController` (they already use MediatR correctly).
- Touching `IClassificationRule`, its implementations, or their DI registration.
- Frontend changes beyond the automatic OpenAPI client regeneration.
- Adding new authorization policies or modifying existing ones.
- Performance optimizations or caching of the rule list.

## Open Questions

1. **Response wrapper convention.** Does the `InvoiceClassification` module's existing handler return convention use a dedicated response class (e.g. `GetClassificationRuleTypesResponse { List<ClassificationRuleTypeDto> Items }`) or return the list directly as `List<ClassificationRuleTypeDto>`? Inspect a sibling handler in `Application/Features/InvoiceClassification/UseCases/` and match it. **Assumption if unanswered:** match the closest existing sibling handler's pattern; if mixed, return `List<ClassificationRuleTypeDto>` directly for simplicity.
2. **Query vs. Request naming.** Does the project name MediatR read operations `...Query` or `...Request`? **Assumption if unanswered:** mirror the naming used by sibling handlers in the same module.
3. **Test project location.** Confirm whether handler unit tests go in `backend/test/Anela.Heblo.Application.Tests/` or another path used by sibling InvoiceClassification handler tests. **Assumption if unanswered:** match the existing pattern of other InvoiceClassification handler tests in the repo.

## Status: HAS_QUESTIONS