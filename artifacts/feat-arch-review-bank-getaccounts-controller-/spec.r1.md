# Specification: Refactor `BankStatementsController.GetAccounts()` to MediatR Use Case

## Summary
Refactor the `GetAccounts` action of `BankStatementsController` to delegate to a new MediatR handler, eliminating in-controller business logic and the controller's direct dependency on `IOptions<BankAccountSettings>`. This aligns the action with the two other actions in the same controller and with the project-wide pattern documented in `development_guidelines.md`.

## Background
The `GetAccounts` action currently constructs `BankAccountDto` instances directly inside the controller from `IOptions<BankAccountSettings>`. This violates the explicit rule in `docs/architecture/development_guidelines.md` that forbids business logic in controllers. The sibling actions (`ImportStatements`, `GetBankStatements`, `GetBankStatement`) already delegate to MediatR handlers in `Application/Features/Bank/UseCases/`, making `GetAccounts` an inconsistent outlier.

The current implementation also:
- Couples HTTP-layer code to configuration concerns (`IOptions<BankAccountSettings>` is injected into the controller).
- Cannot be unit-tested as a use case in isolation through MediatR.
- Prevents pipeline behaviours (logging, validation, error envelope) registered for `IRequest<TResponse>` from applying uniformly.

This refactor is a pure architecture cleanup. The HTTP contract (route, verb, response payload shape) must remain identical to avoid breaking the auto-generated OpenAPI TypeScript client used by the frontend.

## Functional Requirements

### FR-1: New `GetBankAccounts` use case
Create a new MediatR request/response/handler in `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/`:

- `GetBankAccountsRequest : IRequest<GetBankAccountsResponse>` — empty parameter object.
- `GetBankAccountsResponse : BaseResponse` — wraps a list of `BankAccountDto`. Use a property such as `Accounts` (or `Items`) of type `List<BankAccountDto>`, initialised to an empty list. The response must inherit from `Application.Shared.BaseResponse` to remain consistent with `GetBankStatementListResponse`.
- `GetBankAccountsHandler : IRequestHandler<GetBankAccountsRequest, GetBankAccountsResponse>` — receives `IOptions<BankAccountSettings>` and `ILogger<GetBankAccountsHandler>` via constructor injection; null-checks both, matching the pattern used by `ImportBankStatementHandler` and `GetBankStatementListHandler`.

The handler reads `_bankSettings.Accounts` (treating null as empty), maps each entry to a `BankAccountDto`, and returns the response. Mapping logic must match the current controller behaviour exactly:

```
Name           = a.Name
AccountNumber  = a.AccountNumber
Provider       = a.Provider.ToString()
Currency       = a.Currency.ToString()
```

**Acceptance criteria:**
- The three files exist under the namespace `Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts`.
- `GetBankAccountsResponse` inherits `BaseResponse` and exposes the account list as an `init` or settable property.
- The handler emits an `_logger.LogInformation` entry when invoked, naming the retrieved account count (consistent with `GetBankStatementListHandler`).
- The handler treats `_bankSettings.Accounts == null` as zero accounts and returns a successful empty response (does not throw).

### FR-2: Controller refactor
Update `BankStatementsController.GetAccounts()` so that:

- It is `async` and `await`s `_mediator.Send(new GetBankAccountsRequest(), cancellationToken)`.
- It accepts a `CancellationToken` parameter from the request.
- It returns the handler's `Accounts` collection inside `Ok(...)` so the wire shape stays `IEnumerable<BankAccountDto>`. The XML doc comment must remain.
- The `IOptions<BankAccountSettings>` parameter is removed from the constructor; the `_bankSettings` field and its using directives (`Microsoft.Extensions.Options`, `Anela.Heblo.Domain.Features.Bank`) are removed if no other constructor member references them.

**Acceptance criteria:**
- `BankStatementsController` no longer references `BankAccountSettings` or `IOptions<>`.
- `GET /api/bank-statements/accounts` returns the same JSON array shape (array of objects with `name`, `accountNumber`, `provider`, `currency`) as before — verified by comparing a sample response before and after.
- HTTP status codes for success (200) and unauthenticated (401, via `[Authorize]`) are unchanged.

### FR-3: Wire-compatible response shape
Because the frontend's OpenAPI-generated TypeScript client consumes this endpoint, the JSON body returned to clients **must** remain a top-level array of `BankAccountDto` objects, not an envelope. The handler returns the rich `GetBankAccountsResponse` for internal use, but the controller unwraps it and returns `Ok(response.Accounts)`.

**Acceptance criteria:**
- A sample HTTP call against the refactored endpoint returns a JSON array (`[ { ... }, ... ]`), not an object containing `accounts`.
- Running the OpenAPI client generation produces no breaking changes to `BankAccountDto` or the endpoint signature in the generated TS client.

### FR-4: Unit tests for the new handler
Add xUnit unit tests for `GetBankAccountsHandler` under the existing backend test project, mirroring the conventions used by other Bank handler tests:

- Returns an empty list when `BankAccountSettings.Accounts` is `null`.
- Returns an empty list when `BankAccountSettings.Accounts` is an empty list.
- Maps each configured account to a `BankAccountDto` with `Provider` and `Currency` rendered via `.ToString()`.
- Marks the response as `Success = true` (inherited from `BaseResponse`).
- Throws `ArgumentNullException` (or fails fast) when constructed with `null` options, matching the null-check pattern used by sibling handlers.

**Acceptance criteria:**
- Tests live alongside other Bank handler tests in the corresponding test project.
- All tests pass under `dotnet test`.

### FR-5: Existing controller-level integration tests updated
Any existing integration or controller tests that assert on `BankStatementsController.GetAccounts` continue to pass without changes to their HTTP-level expectations. If tests instantiate the controller directly with `IOptions<BankAccountSettings>`, update them to mock `IMediator` instead.

**Acceptance criteria:**
- `dotnet test` for the affected projects passes with no regressions.
- Tests that previously injected `IOptions<BankAccountSettings>` into the controller now inject a mocked `IMediator`.

## Non-Functional Requirements

### NFR-1: Performance
This endpoint reads in-memory configuration; latency must remain effectively constant (< 5 ms server-side excluding network) and must not regress against the current implementation. No new I/O, database calls, or external dependencies are introduced.

### NFR-2: Security
- `[Authorize]` on the controller continues to govern the endpoint; no change to authentication or authorization semantics.
- No new secrets are introduced; bank account configuration continues to flow through the existing `BankAccountSettings` options (loaded from Azure Key Vault in production via the existing config pipeline).
- The response payload is unchanged, so no new PII or sensitive data exposure is introduced.

### NFR-3: Consistency / Maintainability
- Naming, folder placement, null-check style, logging style, and `BaseResponse` inheritance must match the existing `GetBankStatementList` use case.
- DTOs remain classes (not records), per project rule.
- Code passes `dotnet build`, `dotnet format`, and project lint/analyzer checks.

### NFR-4: Backwards compatibility
The frontend, TS client, and any external consumer of `GET /api/bank-statements/accounts` must continue to work without code changes. No new route, no renamed fields, no envelope wrapping at the HTTP layer.

## Data Model

No persistent entities are added or modified.

In-memory types involved:
- `BankAccountSettings` (existing, `Domain.Features.Bank`) — read-only configuration container; unchanged.
- `BankAccountDto` (existing, `Application.Features.Bank.Contracts`) — unchanged.
- `GetBankAccountsRequest` (new) — empty record-like request class implementing `IRequest<GetBankAccountsResponse>`. Implemented as a class (consistent with `GetBankStatementListRequest`).
- `GetBankAccountsResponse` (new) — class inheriting `BaseResponse`, exposing `List<BankAccountDto> Accounts { get; set; } = new();`.

## API / Interface Design

### HTTP endpoint (unchanged contract)
```
GET /api/bank-statements/accounts
Authorization: required (existing [Authorize])
200 OK
Body: [
  {
    "name": "string",
    "accountNumber": "string",
    "provider": "string",
    "currency": "string"
  },
  ...
]
```

### Controller action (target shape)
```csharp
[HttpGet("accounts")]
public async Task<ActionResult<IEnumerable<BankAccountDto>>> GetAccounts(CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetBankAccountsRequest(), cancellationToken);
    return Ok(response.Accounts);
}
```

### MediatR contract
```csharp
public class GetBankAccountsRequest : IRequest<GetBankAccountsResponse> { }

public class GetBankAccountsResponse : BaseResponse
{
    public List<BankAccountDto> Accounts { get; set; } = new();
}
```

### Internal flow
1. ASP.NET routes request to `BankStatementsController.GetAccounts`.
2. Controller dispatches `GetBankAccountsRequest` via `IMediator`.
3. `GetBankAccountsHandler` reads `IOptions<BankAccountSettings>`, maps to DTOs, returns response.
4. Controller returns `response.Accounts` with HTTP 200.

## Dependencies

- **MediatR** — already in use across the application.
- **`IOptions<BankAccountSettings>`** — already registered; consumed by the handler instead of the controller.
- **`BaseResponse`** — already in `Application.Shared`.
- **`BankAccountDto`** — already exists.
- **xUnit / Moq (or NSubstitute) / FluentAssertions** — already used by the existing backend test suite.

No new NuGet packages, no new DI registrations (MediatR's assembly scan already picks up new handlers under `Application`).

## Out of Scope

- Renaming or restructuring `BankAccountDto`.
- Changing the route, HTTP verb, or wire format of the endpoint.
- Modifying `BankAccountSettings` or how it is loaded from configuration/Key Vault.
- Refactoring the other actions in `BankStatementsController` (they already follow the MediatR pattern).
- Adding pagination, filtering, sorting, or any new query parameters to the endpoint.
- Frontend changes — the TS client regenerates automatically and the consuming UI should require no edits.
- Introducing AutoMapper for the `BankAccountSettings → BankAccountDto` mapping; manual mapping in the handler is sufficient and matches the prior controller logic.
- Caching of the accounts list.

## Open Questions

None.

## Status: COMPLETE