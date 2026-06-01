# Architecture Review: Refactor GetAvailableRuleTypes to MediatR Handler

## Skip Design: true

Backend-only structural refactor — no UI/UX work, no new visual components, no design decisions.

## Architectural Fit Assessment

The spec aligns precisely with established conventions in this codebase. Verified against the actual code:

- **MediatR + Vertical Slice is the established pattern** in `Features/InvoiceClassification/UseCases/*`. Every other action in `InvoiceClassificationController` dispatches via `IMediator` (lines 31, 39, 46, 53, 61, 69, 89, 97, 120, 132). The target action (line 75–86) is the lone exception.
- **MediatR handler scan is global** — `ApplicationModule.cs:61` does `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly))`, so a new handler is picked up with no DI changes (confirmed against sibling module comments such as `CatalogModule.cs:38`).
- **`IClassificationRule` is in Domain** (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRule.cs`) — controller currently couples API → Domain, bypassing Application. This violates the dependency direction defined in `docs/architecture/development_guidelines.md` and the "no business logic in controllers" rule (line 37 of that doc).
- **Sibling response/request shape is uniform**: empty `...Request : IRequest<...Response>` (see `GetAccountingTemplatesRequest.cs`), `...Response : BaseResponse` with `(ErrorCodes, Dictionary<string,string>?)` overload (see `GetAccountingTemplatesResponse.cs`). The spec mirrors this exactly.
- **`ClassificationRuleTypeDto` is already a `class`** (`Application/Features/InvoiceClassification/Contracts/ClassificationRuleTypeDto.cs`), satisfying the project's "DTOs never records" rule.
- **DI registration of `IClassificationRule` is in `InvoiceClassificationModule.cs:16–20`** as 5 scoped implementations. The handler receives them via `IEnumerable<IClassificationRule>` — same lifetime as sibling handlers (scoped). No DI changes required.

Integration points: (a) the controller constructor (drops the Domain dependency), (b) one new use case folder, (c) one new test file. Surface area is small and contained.

## Proposed Architecture

### Component Overview

```
backend/
├── src/Anela.Heblo.API/Controllers/
│   └── InvoiceClassificationController.cs        ── EDIT: drop IClassificationRule dep,
│                                                          inject IMediator only,
│                                                          rewrite action as MediatR dispatch
│
├── src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/
│   └── GetClassificationRuleTypes/               ── NEW folder
│       ├── GetClassificationRuleTypesRequest.cs    ── NEW: empty IRequest<...Response>
│       ├── GetClassificationRuleTypesResponse.cs   ── NEW: BaseResponse + List<DTO> RuleTypes
│       └── GetClassificationRuleTypesHandler.cs    ── NEW: ctor-inject IEnumerable<IClassificationRule>,
│                                                          project to DTOs
│
└── test/Anela.Heblo.Tests/Features/InvoiceClassification/
    └── GetClassificationRuleTypesHandlerTests.cs ── NEW: xUnit + FluentAssertions + Moq

Layer dependency (after refactor):
  API ──► Application ──► Domain
  (controller depends only on Application use case namespace + IMediator)
```

Runtime call path:

```
HTTP GET /api/InvoiceClassification/rule-types
        │
        ▼
Controller.GetAvailableRuleTypes(ct)
        │  await _mediator.Send(new GetClassificationRuleTypesRequest(), ct)
        ▼
MediatR pipeline ──► GetClassificationRuleTypesHandler.Handle(...)
        │           uses IEnumerable<IClassificationRule> from DI (5 registrations)
        │           projects to List<ClassificationRuleTypeDto>
        ▼
GetClassificationRuleTypesResponse { RuleTypes = [...] }
        │
        ▼
Controller returns Ok(response.RuleTypes)   ← UNWRAP to preserve bare-array HTTP contract
```

### Key Design Decisions

#### Decision 1: Return bare array from controller (unwrap response envelope)

**Options considered:**
- **A.** Controller returns `Ok(response)` (envelope, matching all sibling controllers).
- **B.** Controller returns `Ok(response.RuleTypes)` (bare array, matching the pre-refactor contract).

**Chosen approach:** B — unwrap in the controller.

**Rationale:** The current HTTP response is a bare JSON array, while every other action returns an envelope object. Switching to A would break the OpenAPI schema and the generated TypeScript client in `frontend/src/api/generated/`. FR-4 of the spec (and the brief itself) treats this as a structural refactor with no consumer impact — preserving the array is the contract guarantee. Internal consistency (handler returns `BaseResponse`-derived envelope, sibling handler convention) is preserved; the deviation is one line in the controller and is the smallest possible departure. **Add a code comment in the controller action body** explaining the one-line unwrap so future contributors don't "fix it" to match siblings.

#### Decision 2: `Task.FromResult` over `async`/`await` in handler

**Options considered:**
- **A.** `public Task<...> Handle(...) => Task.FromResult(new Response { ... });`
- **B.** `public async Task<...> Handle(...) { /* no await */ return new Response { ... }; }`

**Chosen approach:** A — synchronous body wrapped in `Task.FromResult`.

**Rationale:** No I/O is performed (DI-resolved in-memory collection). `async` with no `await` causes a CS1998 warning and adds state-machine overhead for no benefit. `Task.FromResult` is the idiomatic C# choice for genuinely-sync MediatR handlers. Accept `CancellationToken` in the signature (required by `IRequestHandler`) but it can be safely ignored — there's nothing to cancel.

#### Decision 3: Inject `IEnumerable<IClassificationRule>` directly into the handler

**Options considered:**
- **A.** Handler depends on `IEnumerable<IClassificationRule>` (the Domain abstraction).
- **B.** Introduce an Application-layer `IClassificationRuleCatalog` service that wraps the enumeration.

**Chosen approach:** A.

**Rationale:** The Application layer is *allowed* to reference Domain (per the dependency rule API → Application → Domain). The whole point of moving the projection out of the controller is that it's now in the layer that *should* depend on Domain. Introducing a wrapper interface adds an abstraction with no second consumer (YAGNI). `GetAccountingTemplatesHandler` and `GetClassificationRulesHandler` both depend directly on Domain abstractions (`IInvoiceClassificationsClient`, `IClassificationRuleRepository`) — this matches the local pattern.

#### Decision 4: No AutoMapper for this projection

**Options considered:**
- **A.** Inline `Select(...)` projection in the handler.
- **B.** Add an AutoMapper profile entry mapping `IClassificationRule` → `ClassificationRuleTypeDto`.

**Chosen approach:** A.

**Rationale:** Three fields, all strings, name-aligned. Inline projection is shorter, debuggable without reflection, and avoids polluting `InvoiceClassificationMappingProfile.cs` with a mapping for a DI-resolved metadata interface (AutoMapper profiles in this codebase are used for persistence entity → DTO mappings, not service interface → DTO).

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationRuleTypes/
    GetClassificationRuleTypesRequest.cs
    GetClassificationRuleTypesResponse.cs
    GetClassificationRuleTypesHandler.cs

backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/
    GetClassificationRuleTypesHandlerTests.cs
```

Mirror the **exact** file layout and namespace of `GetAccountingTemplates/`. Namespace: `Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRuleTypes`.

### Interfaces and Contracts

**Request (empty marker):**
```csharp
public class GetClassificationRuleTypesRequest : IRequest<GetClassificationRuleTypesResponse>
{
}
```

**Response (BaseResponse-derived, dual constructor matching siblings):**
```csharp
public class GetClassificationRuleTypesResponse : BaseResponse
{
    public List<ClassificationRuleTypeDto> RuleTypes { get; set; } = new();

    public GetClassificationRuleTypesResponse() : base() { }

    public GetClassificationRuleTypesResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

**Handler:**
```csharp
public class GetClassificationRuleTypesHandler
    : IRequestHandler<GetClassificationRuleTypesRequest, GetClassificationRuleTypesResponse>
{
    private readonly IEnumerable<IClassificationRule> _classificationRules;

    public GetClassificationRuleTypesHandler(IEnumerable<IClassificationRule> classificationRules)
    {
        _classificationRules = classificationRules;
    }

    public Task<GetClassificationRuleTypesResponse> Handle(
        GetClassificationRuleTypesRequest request,
        CancellationToken cancellationToken)
    {
        var ruleTypes = _classificationRules
            .Select(rule => new ClassificationRuleTypeDto
            {
                Identifier = rule.Identifier,
                DisplayName = rule.DisplayName,
                Description = rule.Description
            })
            .ToList();

        return Task.FromResult(new GetClassificationRuleTypesResponse { RuleTypes = ruleTypes });
    }
}
```

**Controller action (note the one-line unwrap to preserve HTTP contract):**
```csharp
[HttpGet("rule-types")]
public async Task<ActionResult<List<ClassificationRuleTypeDto>>> GetAvailableRuleTypes(
    CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetClassificationRuleTypesRequest(), cancellationToken);
    // Unwrap to preserve bare-array JSON contract; envelope kept internally for sibling consistency.
    return Ok(response.RuleTypes);
}
```

**Controller constructor:**
```csharp
private readonly IMediator _mediator;

public InvoiceClassificationController(IMediator mediator)
{
    _mediator = mediator;
}
```

Required `using` adjustments to `InvoiceClassificationController.cs`:
- **Add:** `using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRuleTypes;`
- **Remove:** `using Anela.Heblo.Domain.Features.InvoiceClassification;` (line 12) — verify no other reference to it remains in the file before deleting (grep the file after edits).

### Data Flow

1. Client `GET /api/InvoiceClassification/rule-types`.
2. ASP.NET Core resolves `InvoiceClassificationController` with `IMediator` only.
3. Action constructs an empty `GetClassificationRuleTypesRequest` and `Send`s it with the request `CancellationToken`.
4. MediatR resolves `GetClassificationRuleTypesHandler` (scoped — same scope as the controller).
5. Handler's `IEnumerable<IClassificationRule>` is materialized from DI (5 scoped instances per `InvoiceClassificationModule.cs:16–20`, in registration order).
6. Handler projects each rule to a `ClassificationRuleTypeDto`, wraps in `GetClassificationRuleTypesResponse`, returns synchronously via `Task.FromResult`.
7. Controller unwraps `response.RuleTypes` and returns `Ok(...)`.
8. ASP.NET serializes as a JSON array. Element order is the DI-registration order from `InvoiceClassificationModule.cs` — identical to today.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Contributor "fixes" the controller to `return Ok(response)` for consistency with siblings, breaking the bare-array contract. | Medium | Inline comment in the action explaining the unwrap. Add a controller integration test (or assert in an existing test) that the response body is a JSON array, not an object. |
| OpenAPI regeneration alters the operation's response type (e.g., from `ClassificationRuleTypeDto[]` to a generated `GetClassificationRuleTypesResponse`) because Swashbuckle infers from the `Send` envelope rather than the controller return type. | Medium | Controller action signature already declares `ActionResult<List<ClassificationRuleTypeDto>>` — Swashbuckle uses the declared return type, so the OpenAPI schema should be unchanged. **Verify** by running the OpenAPI generator and diffing `frontend/src/api/generated/` after the refactor; if the generated TypeScript client changes, treat that as a regression and adjust the action's response type attribute. |
| Element ordering changes if DI resolution order differs from today. | Low | DI order is deterministic given identical registrations; no registration changes are in scope. Spec NFR-3 test case 3 (`Handle_PreservesEnumerationOrder`) locks this in. |
| `IEnumerable<IClassificationRule>` is enumerated multiple times by accident (it's lazy). | Low | The handler enumerates exactly once via `Select(...).ToList()`. Test case 1 (empty collection) also exercises the enumeration. |
| `dotnet format` rewrites `using` order in the controller, producing a noisy diff. | Low | Run `dotnet format` after edits and commit the formatted result; review the diff to confirm only intentional changes. |
| Hidden test or DI consumer relies on the controller's `IEnumerable<IClassificationRule>` field (reflection, internals-visible-to). | Very Low | `grep` the solution for `_classificationRules` and `IEnumerable<IClassificationRule>` references in API and test projects before merging. |

## Specification Amendments

The spec (`spec.r2.md`) is internally consistent, accurately reflects the codebase, and resolves its prior open questions correctly. Two small clarifications worth folding into the spec:

1. **Mandate a comment on the controller unwrap.** FR-3 should explicitly require an inline comment such as `// Unwrap to preserve bare-array JSON contract; envelope kept internally for sibling consistency.` This is a deliberate deviation from sibling pattern and needs a guardrail against well-meaning "consistency" edits. (This satisfies the WHY-only comment rule from the project's coding behavior — the comment is non-obvious context, not redundant narration.)

2. **Add OpenAPI verification to the validation steps.** NFR-4 currently asks for `dotnet build`, `dotnet format`, and tests-green. Add: *"The OpenAPI document regenerates with no diff to the `/api/InvoiceClassification/rule-types` operation schema (response type still `ClassificationRuleTypeDto[]`). The generated TypeScript client (`frontend/src/api/generated/`) produces no diff for `getAvailableRuleTypes`."* This makes FR-5's existing contract claim a concrete, checkable step.

3. **Pick `Task.FromResult` explicitly.** FR-2 currently leaves the choice between `async`-no-`await` and `Task.FromResult` open. Decide on `Task.FromResult` (no CS1998 warning, clearer intent). Minor; either works functionally.

No other amendments needed.

## Prerequisites

None. All required infrastructure exists:

- **MediatR is registered globally** via `ApplicationModule.cs:61` (`AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly))`). The new handler is auto-discovered with zero DI changes.
- **`IClassificationRule` implementations are already registered** (`InvoiceClassificationModule.cs:16–20`) — five scoped registrations. Handler scope (scoped via MediatR) matches.
- **`BaseResponse`, `ErrorCodes`, and `ClassificationRuleTypeDto` already exist** in their canonical locations.
- **Test project** (`Anela.Heblo.Tests`) already references xUnit, FluentAssertions, and Moq (per `ClassifyInvoicesHandlerTests.cs:5–7`); no new test dependencies needed.
- **No database migration, configuration entry, environment variable, or infrastructure change** is required.

Implementation can begin immediately.