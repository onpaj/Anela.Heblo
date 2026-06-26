I have enough context to write the architecture review.

# Architecture Review: Refactor `BankStatementsController.GetAccounts()` to MediatR Use Case

## Skip Design: true

This is a backend-only refactor with no UI/UX work. The HTTP wire contract is unchanged, the OpenAPI-generated TypeScript client requires no edits, and no new visual components or screens are introduced.

## Architectural Fit Assessment

The proposal aligns cleanly with the existing project pattern. The codebase already follows Vertical Slice Architecture with MediatR (`docs/architecture/development_guidelines.md`), and the two sibling actions in `BankStatementsController` (`ImportStatements`, `GetBankStatements`) already delegate to handlers under `Application/Features/Bank/UseCases/`. The `GetAccounts` action is the documented outlier — `development_guidelines.md` explicitly lists "Business logic in Controller class" as a forbidden practice.

Main integration points:
- **MediatR pipeline** — already wired; no DI changes needed (assembly scan picks up new handlers).
- **`IOptions<BankAccountSettings>`** — already consumed by `ImportBankStatementHandler`, proving the pattern of injecting `IOptions<>` into an Application-layer handler works as expected.
- **`BaseResponse`** — `Application/Shared/BaseResponse.cs` is already the base for `GetBankStatementListResponse`; reusing it keeps the response surface consistent.
- **OpenAPI contract** — the controller-level unwrap (`Ok(response.Accounts)`) preserves the existing wire shape (`IEnumerable<BankAccountDto>`), so the generated TS client and any frontend hook bound to it remain untouched.

No conflicts with existing conventions were found.

## Proposed Architecture

### Component Overview

```
                  ┌─────────────────────────────────────────┐
                  │ ASP.NET (API)                           │
                  │                                         │
   HTTP GET ───►  │ BankStatementsController.GetAccounts()  │
/api/bank-state…  │   - depends on IMediator only           │
                  │   - returns Ok(response.Accounts)       │
                  └────────────────┬────────────────────────┘
                                   │ Send(GetBankAccountsRequest)
                                   ▼
                  ┌─────────────────────────────────────────┐
                  │ MediatR pipeline (existing behaviours)  │
                  └────────────────┬────────────────────────┘
                                   ▼
                  ┌─────────────────────────────────────────┐
                  │ Application/Features/Bank/UseCases/     │
                  │   GetBankAccounts/                      │
                  │     • GetBankAccountsRequest            │
                  │     • GetBankAccountsResponse           │
                  │     • GetBankAccountsHandler            │
                  │       ── reads IOptions<BankAccount…>   │
                  │       ── maps BankAccountConfiguration  │
                  │          → BankAccountDto               │
                  └────────────────┬────────────────────────┘
                                   │ uses
                                   ▼
                  ┌─────────────────────────────────────────┐
                  │ Domain/Features/Bank/                   │
                  │   BankAccountSettings (read-only conf)  │
                  └─────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Controller unwraps the rich response into a bare array
**Options considered:**
- (a) Controller returns `Ok(response.Accounts)`, preserving the legacy `IEnumerable<BankAccountDto>` wire shape.
- (b) Controller returns `Ok(response)`, exposing the full `GetBankAccountsResponse` (with `Success`, `ErrorCode`, etc.) on the wire.
- (c) Change the handler to return `List<BankAccountDto>` directly (no `BaseResponse` wrapper).

**Chosen approach:** (a).

**Rationale:** The frontend's OpenAPI-generated TypeScript client expects an array (current contract); (b) would silently change the response schema and force frontend changes — explicitly out of scope. (c) breaks the project-wide convention (all sibling handlers inherit `BaseResponse`) and forfeits future pipeline benefits (uniform error envelope, validation behaviour). (a) keeps both: handler-side consistency for internal use and wire-side compatibility for clients. This same pattern is already used by `GetBankStatement` (line 134–155 of the controller), which calls a handler and returns a slice of its response.

#### Decision 2: Manual mapping in the handler — no AutoMapper profile
**Options considered:**
- (a) Manual `.Select(a => new BankAccountDto { ... })` inside the handler.
- (b) Add a profile entry in `BankMappingProfile` for `BankAccountConfiguration → BankAccountDto`.

**Chosen approach:** (a).

**Rationale:** The mapping is four trivial assignments (two of which are `.ToString()` on enums). AutoMapper's value here is negligible and adds an extra indirection that complicates testing. The brief and the spec both explicitly exclude AutoMapper for this mapping. `BankMappingProfile` should be reserved for the heavier `BankStatementImport → BankStatementImportDto` mappings that already live there.

#### Decision 3: Drop `_bankSettings` field from the controller entirely
**Options considered:**
- (a) Remove `IOptions<BankAccountSettings>` from the constructor, the `_bankSettings` field, and the now-unused `using` directives.
- (b) Keep the field in case future actions need it.

**Chosen approach:** (a).

**Rationale:** YAGNI. No other controller member references `_bankSettings`; `ImportBankStatementHandler` (the other consumer of the settings) injects `IOptions<BankAccountSettings>` itself. Keeping a dead field invites future drift back into the forbidden "business logic in controller" pattern. `Domain.Features.Bank` and `Microsoft.Extensions.Options` `using` directives should also be removed if no other reference remains — verify after the edit.

#### Decision 4: Handler treats `Accounts == null` as empty, does not throw
**Options considered:**
- (a) Treat null `Accounts` collection as zero configured accounts; return empty list, `Success = true`.
- (b) Treat null `Accounts` as a configuration error and return an error response.

**Chosen approach:** (a).

**Rationale:** Matches the exact behaviour of the current controller (`_bankSettings.Accounts ?? []`). Changing this would be a behavioural regression — clients that expect an empty array on a misconfigured environment would suddenly receive an error envelope (or, if (b) is added on top of decision 1, an HTTP error). Preserving the existing semantics is the entire point of this refactor.

## Implementation Guidance

### Directory / Module Structure

Create three new files (no other files touched in Application/Domain):

```
backend/src/Anela.Heblo.Application/Features/Bank/UseCases/
└── GetBankAccounts/
    ├── GetBankAccountsRequest.cs
    ├── GetBankAccountsResponse.cs
    └── GetBankAccountsHandler.cs
```

Modify:
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — refactor `GetAccounts`, drop `IOptions<BankAccountSettings>` from constructor and the `_bankSettings` field, remove now-unused `using` directives.

Tests:
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs` — new xUnit class mirroring `ImportBankStatementHandlerTests` conventions (Moq for `ILogger`, `Options.Create(...)` for settings, FluentAssertions optional but matching existing style — current Bank tests use plain `Assert`).

### Interfaces and Contracts

```csharp
// GetBankAccountsRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;

public class GetBankAccountsRequest : IRequest<GetBankAccountsResponse>
{
}
```

```csharp
// GetBankAccountsResponse.cs
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;

public class GetBankAccountsResponse : BaseResponse
{
    public List<BankAccountDto> Accounts { get; set; } = new List<BankAccountDto>();
}
```
Use `new List<BankAccountDto>()` (matches `GetBankStatementListResponse.Items` style at `GetBankStatementListResponse.cs:8`) rather than the collection-expression `[]` to keep the style consistent with the sibling response.

```csharp
// GetBankAccountsHandler.cs (sketch)
public class GetBankAccountsHandler : IRequestHandler<GetBankAccountsRequest, GetBankAccountsResponse>
{
    private readonly BankAccountSettings _bankSettings;
    private readonly ILogger<GetBankAccountsHandler> _logger;

    public GetBankAccountsHandler(
        IOptions<BankAccountSettings> bankSettings,
        ILogger<GetBankAccountsHandler> logger)
    {
        _bankSettings = bankSettings?.Value ?? throw new ArgumentNullException(nameof(bankSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetBankAccountsResponse> Handle(GetBankAccountsRequest request, CancellationToken cancellationToken)
    {
        var accounts = (_bankSettings.Accounts ?? new List<BankAccountConfiguration>())
            .Select(a => new BankAccountDto
            {
                Name = a.Name,
                AccountNumber = a.AccountNumber,
                Provider = a.Provider.ToString(),
                Currency = a.Currency.ToString(),
            })
            .ToList();

        _logger.LogInformation("Retrieved {Count} bank accounts", accounts.Count);

        return Task.FromResult(new GetBankAccountsResponse { Accounts = accounts });
    }
}
```

Note: `Handle` is sync — wrap with `Task.FromResult`. Don't mark the method `async` without `await` (analyzer CS1998). This matches the pure in-memory nature of the operation; mirrors how a no-I/O handler would be structured.

Null-check pattern: `ImportBankStatementHandler.cs:33` uses `bankSettings.Value ?? throw...`. That dereferences `bankSettings` before the null check. Prefer `bankSettings?.Value ?? throw new ArgumentNullException(nameof(bankSettings))` to handle a null wrapper cleanly — this is a minor improvement on the sibling pattern, justified by the test acceptance criterion (FR-4) requiring `ArgumentNullException` when constructed with `null` options.

Controller (target shape):
```csharp
public BankStatementsController(IMediator mediator, ILogger<BankStatementsController> logger)
{
    _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
}

[HttpGet("accounts")]
public async Task<ActionResult<IEnumerable<BankAccountDto>>> GetAccounts(CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetBankAccountsRequest(), cancellationToken);
    return Ok(response.Accounts);
}
```

Verify and drop these from the file's `using` directives after the edit:
- `Anela.Heblo.Domain.Features.Bank` — referenced only by `BankAccountSettings`.
- `Microsoft.Extensions.Options` — referenced only by `IOptions<>`.

### Data Flow

```
Client (TS API client)
  │  GET /api/bank-statements/accounts  (cookie/JWT)
  ▼
[Authorize] → BankStatementsController.GetAccounts(ct)
  │  _mediator.Send(new GetBankAccountsRequest(), ct)
  ▼
MediatR → GetBankAccountsHandler.Handle(req, ct)
  │  reads IOptions<BankAccountSettings>.Value
  │  for each BankAccountConfiguration → new BankAccountDto { Name, AccountNumber, Provider=ToString(), Currency=ToString() }
  │  emits LogInformation("Retrieved {Count} bank accounts", count)
  │  returns GetBankAccountsResponse { Accounts = [...], Success = true }
  ▼
Controller → Ok(response.Accounts)  ─── [ {name, accountNumber, provider, currency}, ... ]
  ▼
Client receives JSON array (unchanged wire shape)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Accidentally returning `Ok(response)` instead of `Ok(response.Accounts)` would silently break the TS client by changing the JSON shape from array to envelope. | High | Add an integration test (or extend an existing controller test) that asserts the response is a JSON array and that the first element exposes top-level `name`/`accountNumber` keys (not nested under `accounts`). Verify by manual `curl` against the local dev server. |
| Removing `using Microsoft.Extensions.Options` / `Anela.Heblo.Domain.Features.Bank` may also strip a reference needed by something not visible at a glance. | Low | Run `dotnet build` + `dotnet format` after the edit; the analyzer flags unused usings and missing references. |
| The `?? throw` null-check inverts the order of `bankSettings.Value` vs the null check (vs. sibling). A reviewer comparing diffs may flag inconsistency. | Low | Document the intent in the test (a `Constructor_WithNullOptions_Throws` case) — passing test justifies the pattern. Alternatively replicate sibling's pattern verbatim if strict mirroring is desired. |
| Existing controller test that instantiates `BankStatementsController` with `IOptions<BankAccountSettings>` will fail to compile after the constructor change. | Medium | Spec FR-5 already addresses this. Before refactoring the controller, grep for `BankStatementsController(` to find any direct instantiations, then update them in the same PR. |
| Async controller signature change (`ActionResult` → `Task<ActionResult>`) is technically a method-signature change, but ASP.NET routes both transparently. | Low | Verified — the sibling `ImportStatements`/`GetBankStatements` actions use the same async signature; no routing or OpenAPI implications. |
| `Handle` being sync-but-`Task`-returning might trigger `CA1849` or similar analyzer warnings depending on project ruleset. | Low | Use `Task.FromResult(...)`. If the project enforces an async-only convention, mark the method `async` and accept the CS1998 warning suppression or insert `await Task.CompletedTask`. Check analyzer output during `dotnet build`. |

## Specification Amendments

1. **Clarify the `Handle` method shape.** The spec is silent on whether `Handle` should be `async`. Recommend: keep it synchronous internally and return `Task.FromResult(...)` since no I/O is performed. Add this note to FR-1.

2. **Tighten the null-check pattern for `IOptions<BankAccountSettings>`.** Use `bankSettings?.Value ?? throw new ArgumentNullException(nameof(bankSettings))` rather than the sibling's `bankSettings.Value ?? throw ...` — this is required to satisfy FR-4's acceptance criterion that constructing with null options throws cleanly. Mention explicitly in FR-1 acceptance.

3. **Explicit list of `using` directives to remove from the controller.** Add to FR-2 acceptance: after the edit, `BankStatementsController.cs` no longer contains `using Anela.Heblo.Domain.Features.Bank;` or `using Microsoft.Extensions.Options;` (assuming no other reference remains — confirm with the analyzer).

4. **Add a controller integration test for wire shape (optional but recommended).** Spec covers handler-level unit tests (FR-4) and assumes wire compatibility (FR-3); a single `WebApplicationFactory`-based test asserting the JSON is a top-level array provides a concrete guard against the highest-severity risk above. Either add a new test or augment the test introduced by FR-5.

5. **Log message style.** FR-1 says "naming the retrieved account count, consistent with `GetBankStatementListHandler`". Sibling logs use the template `"Retrieved {Count} bank statements"`. Suggest: `"Retrieved {Count} bank accounts"` — make this the explicit expected string in the acceptance criterion to remove ambiguity.

## Prerequisites

None. All required infrastructure is already in place:

- MediatR is registered and uses assembly scan, so the new handler auto-registers — no edit to `BankModule.cs` or `Program.cs` needed.
- `IOptions<BankAccountSettings>` is already registered in `BankModule.cs:13` via `services.Configure<BankAccountSettings>(...)`.
- `BaseResponse` exists at `Application/Shared/BaseResponse.cs`.
- `BankAccountDto` exists at `Application/Features/Bank/Contracts/BankAccountDto.cs`.
- xUnit, Moq, and the Bank test project layout are already established (`backend/test/Anela.Heblo.Tests/Features/Bank/`).
- No DB migration, no config change, no Azure Key Vault entry, no new NuGet package, no new DI registration.

Implementation can start immediately.