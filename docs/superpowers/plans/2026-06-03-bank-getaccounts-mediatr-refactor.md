# Refactor `BankStatementsController.GetAccounts()` to MediatR Use Case — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the in-controller mapping logic in `BankStatementsController.GetAccounts()` into a new MediatR `GetBankAccounts` use case (Request + Response + Handler), drop the controller's dependency on `IOptions<BankAccountSettings>`, and preserve the existing HTTP wire contract exactly.

**Architecture:** Vertical Slice + MediatR. Three new files under `Application/Features/Bank/UseCases/GetBankAccounts/`. Handler reads `IOptions<BankAccountSettings>`, manually maps `BankAccountConfiguration` → `BankAccountDto`, and returns a `GetBankAccountsResponse : BaseResponse`. Controller becomes a thin shim that dispatches the request and unwraps `response.Accounts` so the JSON stays a top-level array.

**Tech Stack:** .NET 8, ASP.NET Core, MediatR, xUnit, Moq, `Microsoft.Extensions.Options`.

---

## File Structure

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsRequest.cs` — empty MediatR request.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsResponse.cs` — `BaseResponse` subtype with `List<BankAccountDto> Accounts`.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs` — reads settings, maps, returns response.
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs` — xUnit handler tests.
- `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs` — controller-level test guarding the wire shape (mitigates the High-severity risk of accidentally returning the envelope).

**Modify:**
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — refactor `GetAccounts`, drop `IOptions<BankAccountSettings>` from constructor, drop `_bankSettings` field, drop now-unused `using` directives, add `using` for the new namespace.

**No other files touched.** MediatR registration is by assembly scan (no DI edit needed). `BankAccountSettings`, `BankAccountDto`, `BaseResponse`, `BankAccountConfiguration` are unchanged.

---

## Task 1: Create `GetBankAccountsRequest`

Empty MediatR request class. Mirrors the empty-parameter style of `GetBankStatementListRequest` (a class, not a record, consistent with the project rule that contract types are classes).

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsRequest.cs`

- [ ] **Step 1: Write the file**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;

public class GetBankAccountsRequest : IRequest<GetBankAccountsResponse>
{
}
```

- [ ] **Step 2: Commit**

`GetBankAccountsResponse` does not yet exist, so the solution will not compile until Task 2. Stage the file but defer committing until Task 2 — this keeps the repo green commit-by-commit.

(No commit at this step — combined with Task 2.)

---

## Task 2: Create `GetBankAccountsResponse`

Class inheriting `Application.Shared.BaseResponse` (same parent as `GetBankStatementListResponse`). Exposes a settable `List<BankAccountDto> Accounts` initialised to an empty list. Initialisation style matches `GetBankStatementListResponse.Items` (`new List<BankAccountDto>()`, not `[]`) for stylistic consistency with the sibling response.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsResponse.cs`

- [ ] **Step 1: Write the file**

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;

public class GetBankAccountsResponse : BaseResponse
{
    public List<BankAccountDto> Accounts { get; set; } = new List<BankAccountDto>();
}
```

- [ ] **Step 2: Verify the Application project compiles**

Run from repo root:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds. The new request and response types compile; no handler is required yet for the Application project to build (handlers are discovered at runtime).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsResponse.cs
git commit -m "feat: add GetBankAccounts MediatR request and response"
```

---

## Task 3: Write failing tests for `GetBankAccountsHandler`

TDD step. Add the xUnit test class under the existing Bank test folder, mirroring conventions in `ImportBankStatementHandlerTests.cs` (Moq for `ILogger`, `Options.Create(...)` for settings, plain `Assert.*` rather than FluentAssertions because the existing Bank tests don't pull in FluentAssertions). Tests cover the FR-4 acceptance matrix:

1. `Handle_WithNullAccountsList_ReturnsEmptyResponse` — `BankAccountSettings.Accounts == null` → empty `response.Accounts`, `Success == true`.
2. `Handle_WithEmptyAccountsList_ReturnsEmptyResponse` — empty `Accounts` list → empty `response.Accounts`, `Success == true`.
3. `Handle_WithConfiguredAccounts_MapsEachAccountToDto` — verifies each `BankAccountConfiguration` becomes a `BankAccountDto` with `Provider` and `Currency` rendered via `.ToString()`.
4. `Constructor_WithNullOptions_ThrowsArgumentNullException` — handler constructed with `null` `IOptions<BankAccountSettings>` throws `ArgumentNullException`. **Note:** this requires the null-safe pattern `bankSettings?.Value ?? throw ...` in the constructor (see Task 4) — the sibling `ImportBankStatementHandler` uses `bankSettings.Value ?? throw ...` which would NRE instead of throwing the expected `ArgumentNullException`; this test pins down the tighter pattern called out in arch-review amendment #2.
5. `Constructor_WithNullLogger_ThrowsArgumentNullException` — matches the existing constructor null-check convention on logger.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class GetBankAccountsHandlerTests
{
    private readonly Mock<ILogger<GetBankAccountsHandler>> _mockLogger;

    public GetBankAccountsHandlerTests()
    {
        _mockLogger = new Mock<ILogger<GetBankAccountsHandler>>();
    }

    private GetBankAccountsHandler CreateHandler(BankAccountSettings settings)
    {
        return new GetBankAccountsHandler(Options.Create(settings), _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithNullAccountsList_ReturnsEmptyResponse()
    {
        var settings = new BankAccountSettings { Accounts = null! };
        var handler = CreateHandler(settings);

        var response = await handler.Handle(new GetBankAccountsRequest(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response.Accounts);
        Assert.Empty(response.Accounts);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task Handle_WithEmptyAccountsList_ReturnsEmptyResponse()
    {
        var settings = new BankAccountSettings { Accounts = new List<BankAccountConfiguration>() };
        var handler = CreateHandler(settings);

        var response = await handler.Handle(new GetBankAccountsRequest(), CancellationToken.None);

        Assert.NotNull(response.Accounts);
        Assert.Empty(response.Accounts);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task Handle_WithConfiguredAccounts_MapsEachAccountToDto()
    {
        var settings = new BankAccountSettings
        {
            Accounts = new List<BankAccountConfiguration>
            {
                new BankAccountConfiguration
                {
                    Name = "ComgateCZK",
                    Provider = BankClientProvider.Comgate,
                    AccountNumber = "123456789",
                    FlexiBeeId = 1,
                    Currency = CurrencyCode.CZK
                },
                new BankAccountConfiguration
                {
                    Name = "ComgateEUR",
                    Provider = BankClientProvider.Comgate,
                    AccountNumber = "987654321",
                    FlexiBeeId = 2,
                    Currency = CurrencyCode.EUR
                }
            }
        };
        var handler = CreateHandler(settings);

        var response = await handler.Handle(new GetBankAccountsRequest(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Accounts.Count);

        var first = response.Accounts[0];
        Assert.Equal("ComgateCZK", first.Name);
        Assert.Equal("123456789", first.AccountNumber);
        Assert.Equal(BankClientProvider.Comgate.ToString(), first.Provider);
        Assert.Equal(CurrencyCode.CZK.ToString(), first.Currency);

        var second = response.Accounts[1];
        Assert.Equal("ComgateEUR", second.Name);
        Assert.Equal("987654321", second.AccountNumber);
        Assert.Equal(BankClientProvider.Comgate.ToString(), second.Provider);
        Assert.Equal(CurrencyCode.EUR.ToString(), second.Currency);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GetBankAccountsHandler(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var settings = new BankAccountSettings { Accounts = new List<BankAccountConfiguration>() };
        Assert.Throws<ArgumentNullException>(() =>
            new GetBankAccountsHandler(Options.Create(settings), null!));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail with a compile error**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankAccountsHandlerTests"
```

Expected: build fails because `GetBankAccountsHandler` does not exist yet. This is the RED state. If the build unexpectedly succeeds, the handler class already exists — stop and investigate before proceeding.

(No commit at this step — combined with Task 4 once tests pass.)

---

## Task 4: Implement `GetBankAccountsHandler` to make tests pass

Synchronous handler returning `Task.FromResult`. Matches the in-memory nature of the operation (no I/O), avoids the CS1998 "async without await" warning, and mirrors how no-I/O handlers are typically written. The null-check on `IOptions<BankAccountSettings>` uses `bankSettings?.Value ?? throw new ArgumentNullException(nameof(bankSettings))` — the null-conditional `?.` is the deliberate improvement over `ImportBankStatementHandler`'s pattern called out in arch-review amendment #2, required so that `Constructor_WithNullOptions_ThrowsArgumentNullException` (Task 3) passes cleanly instead of throwing NRE.

Log message: `"Retrieved {Count} bank accounts"` per arch-review amendment #5.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs`

- [ ] **Step 1: Write the handler**

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;

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

- [ ] **Step 2: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankAccountsHandlerTests"
```

Expected: all 5 tests pass (GREEN). If `Constructor_WithNullOptions_ThrowsArgumentNullException` throws `NullReferenceException` instead of `ArgumentNullException`, verify the `?.` is present on `bankSettings?.Value` in the constructor.

- [ ] **Step 3: Run `dotnet format` to apply style fixes**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --include backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs
```

Expected: no errors. Re-run the tests above if the formatter touched anything.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs
git commit -m "feat: implement GetBankAccountsHandler with unit tests"
```

---

## Task 5: Refactor `BankStatementsController` to dispatch via MediatR

Replace the in-controller mapping with `await _mediator.Send(new GetBankAccountsRequest(), cancellationToken)` and `return Ok(response.Accounts)`. Drop `IOptions<BankAccountSettings>` from the constructor, remove the `_bankSettings` field, and prune the now-unused `using` directives (`Microsoft.Extensions.Options`, `Anela.Heblo.Domain.Features.Bank`). Add `using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;` for the new request type.

The controller-level wire-shape guard added in Task 6 will catch any accidental envelope return; this task only changes implementation.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`

- [ ] **Step 1: Update the `using` directives**

In `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`, replace the existing using block (lines 1–9):

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
```

The two removed lines (vs. the current file) are `using Anela.Heblo.Domain.Features.Bank;` and `using Microsoft.Extensions.Options;`. The newly added line is `using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;`. `Anela.Heblo.Domain.Shared` and the rest stay as-is.

- [ ] **Step 2: Update the field declarations and constructor**

Replace the field block and constructor (lines 18–27 in the current file). Drop `_bankSettings` and the `IOptions<BankAccountSettings>` parameter:

```csharp
    private readonly IMediator _mediator;
    private readonly ILogger<BankStatementsController> _logger;

    public BankStatementsController(IMediator mediator, ILogger<BankStatementsController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```

- [ ] **Step 3: Refactor the `GetAccounts` action**

Replace the current `GetAccounts` action (lines 29–45) with the MediatR dispatch. Keep the XML doc comment exactly as-is. Accept a `CancellationToken` and `await` the handler.

```csharp
    /// <summary>
    /// Get list of configured bank accounts available for import
    /// </summary>
    [HttpGet("accounts")]
    public async Task<ActionResult<IEnumerable<BankAccountDto>>> GetAccounts(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetBankAccountsRequest(), cancellationToken);
        return Ok(response.Accounts);
    }
```

Do not touch `ImportStatements`, `GetBankStatements`, or `GetBankStatement` — they are already MediatR-based and out of scope.

- [ ] **Step 4: Verify the API project builds**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds. If you see a CS0246 about `BankAccountSettings` or `IOptions`, confirm no other member of the controller still references them and that the `using` directives are correct.

- [ ] **Step 5: Run `dotnet format` on the controller**

```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --include backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
```

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
git commit -m "refactor: dispatch BankStatementsController.GetAccounts via MediatR"
```

---

## Task 6: Add controller test guarding the wire shape

Add a focused xUnit test class that constructs `BankStatementsController` directly with a mocked `IMediator`, calls `GetAccounts`, and asserts the result is an `OkObjectResult` whose `Value` is `IEnumerable<BankAccountDto>` — not a `GetBankAccountsResponse`. This pins down FR-3 (wire-compatible response shape) and mitigates the High-severity risk from the arch review (accidentally returning `Ok(response)` instead of `Ok(response.Accounts)` would silently break the TS client).

This is the structural equivalent of FR-5: there is no pre-existing `BankStatementsControllerTests.cs`, so nothing needs to be updated; we instead add the new test class with the post-refactor constructor signature.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs`

- [ ] **Step 1: Write the controller test**

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class BankStatementsControllerTests
{
    private readonly Mock<IMediator> _mockMediator;
    private readonly Mock<ILogger<BankStatementsController>> _mockLogger;
    private readonly BankStatementsController _controller;

    public BankStatementsControllerTests()
    {
        _mockMediator = new Mock<IMediator>();
        _mockLogger = new Mock<ILogger<BankStatementsController>>();
        _controller = new BankStatementsController(_mockMediator.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetAccounts_DispatchesGetBankAccountsRequest()
    {
        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBankAccountsResponse());

        await _controller.GetAccounts(CancellationToken.None);

        _mockMediator.Verify(
            m => m.Send(It.IsAny<GetBankAccountsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAccounts_ReturnsOkWithBareArrayOfDtos_NotEnvelope()
    {
        var accounts = new List<BankAccountDto>
        {
            new BankAccountDto
            {
                Name = "ComgateCZK",
                AccountNumber = "123456789",
                Provider = "Comgate",
                Currency = "CZK",
            }
        };
        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBankAccountsResponse { Accounts = accounts });

        var actionResult = await _controller.GetAccounts(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsAssignableFrom<IEnumerable<BankAccountDto>>(ok.Value);
        Assert.NotSame(typeof(GetBankAccountsResponse), ok.Value!.GetType());
        Assert.Single(payload);
        Assert.Equal("ComgateCZK", payload.First().Name);
    }

    [Fact]
    public async Task GetAccounts_WithEmptyHandlerResponse_ReturnsOkWithEmptyArray()
    {
        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBankAccountsResponse());

        var actionResult = await _controller.GetAccounts(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsAssignableFrom<IEnumerable<BankAccountDto>>(ok.Value);
        Assert.Empty(payload);
    }
}
```

- [ ] **Step 2: Run the new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankStatementsControllerTests"
```

Expected: all 3 tests pass. The `Assert.NotSame(typeof(GetBankAccountsResponse), ok.Value!.GetType())` line is the canary that fails if someone later changes the controller to `Ok(response)`.

- [ ] **Step 3: Run `dotnet format` on the new test file**

```bash
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --include backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs
git commit -m "test: guard BankStatementsController.GetAccounts wire shape"
```

---

## Task 7: Final validation across the solution

Make sure no regression was introduced anywhere else in the backend. Run the full backend test suite and a clean build to catch any stale references.

- [ ] **Step 1: Full backend build**

```bash
dotnet build
```

(Run from repo root; this builds the whole solution.) Expected: build succeeds, zero errors, zero new warnings introduced by changed files.

- [ ] **Step 2: Run the full backend test suite**

```bash
dotnet test
```

Expected: all tests pass. New test classes:
- `Anela.Heblo.Tests.Features.Bank.GetBankAccountsHandlerTests` — 5 tests passing.
- `Anela.Heblo.Tests.Controllers.BankStatementsControllerTests` — 3 tests passing.

If any pre-existing test that constructs `BankStatementsController` directly fails to compile (none exist at plan time — verified via `Grep` for `BankStatementsController` under `backend/test/`), update its constructor call to the new two-argument form and re-run.

- [ ] **Step 3: Final `dotnet format` pass on the whole solution**

```bash
dotnet format
```

Expected: no errors. If anything was reformatted, re-run `dotnet build` and `dotnet test` and amend the prior commit only if the format changes are inside files already in that commit.

- [ ] **Step 4: Manual JSON-shape sanity check (optional but recommended)**

Spin up the API locally and curl the endpoint to visually confirm the JSON is still a top-level array (not an object with `accounts`). The controller test already covers this structurally; this is belt-and-suspenders.

```bash
# In one terminal:
dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj

# In another terminal (replace the token/auth with whatever your dev setup uses):
curl -s http://localhost:5001/api/bank-statements/accounts -H "Authorization: Bearer <dev-token>" | jq '.'
```

Expected: a JSON array literal (`[ { ... } ]`), not an object wrapper. Skip if local auth setup is unavailable — the controller test in Task 6 provides equivalent structural guarantees.

- [ ] **Step 5: Confirm OpenAPI client regeneration produces no diff (optional but recommended)**

If your dev environment regenerates the TypeScript client on build (per `docs/development/api-client-generation.md`), run a `frontend` build and confirm the generated `BankAccountDto` and the `getAccounts` method signature in the TS client are byte-identical to `main`.

```bash
cd frontend
npm run build
git diff -- src/api  # path depends on local generation output; adjust as needed
```

Expected: no diff in the generated TS client for this endpoint. If the generator shows a diff, the controller signature change leaked into the contract — re-inspect FR-3.

- [ ] **Step 6: No further commits required**

All changes were committed in Tasks 2, 4, 5, and 6. If `dotnet format` in Step 3 produced changes to files in a single prior commit, you may amend; if it touched files across multiple commits, prefer a new small commit:

```bash
# Only if Step 3 produced cross-commit changes:
git add -A
git commit -m "chore: dotnet format"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (new `GetBankAccounts` use case) — Tasks 1, 2, 4. Three files in `Application/Features/Bank/UseCases/GetBankAccounts/`. Response inherits `BaseResponse`. Handler logs `"Retrieved {Count} bank accounts"`. Null `Accounts` treated as empty. ✓
- FR-2 (controller refactor) — Task 5. `async`, `await _mediator.Send(...)`, `CancellationToken` parameter, `Ok(response.Accounts)`, XML doc preserved, `IOptions<BankAccountSettings>` and `_bankSettings` removed, `using` directives pruned. ✓
- FR-3 (wire-compatible response shape) — Task 5 returns `Ok(response.Accounts)`; Task 6's `GetAccounts_ReturnsOkWithBareArrayOfDtos_NotEnvelope` test pins it. ✓
- FR-4 (unit tests for handler) — Task 3 + Task 4 cover null/empty `Accounts`, mapping with `.ToString()`, `Success == true`, and `ArgumentNullException` on null options. ✓
- FR-5 (existing controller tests updated) — verified no such tests existed at plan time via `Grep`; Task 6 adds the post-refactor equivalent. ✓
- NFR-1 (performance) — handler is synchronous wrapped in `Task.FromResult`; no I/O; no regression vs. controller-side mapping. ✓
- NFR-2 (security) — `[Authorize]` untouched, no new secrets, payload unchanged. ✓
- NFR-3 (consistency) — folder, naming, `BaseResponse` inheritance, log style, DTO-as-class rule all match siblings; `dotnet format` step included. ✓
- NFR-4 (backwards compat) — Task 6 guards the wire shape; Task 7 Step 5 verifies via TS client regen. ✓
- Arch-review amendment #1 (clarify `Handle` is sync + `Task.FromResult`) — Task 4. ✓
- Arch-review amendment #2 (`bankSettings?.Value` null-conditional) — Task 4 + Task 3's `Constructor_WithNullOptions_ThrowsArgumentNullException`. ✓
- Arch-review amendment #3 (explicit list of removed `using` directives) — Task 5 Step 1. ✓
- Arch-review amendment #4 (controller integration test) — Task 6. ✓
- Arch-review amendment #5 (log message `"Retrieved {Count} bank accounts"`) — Task 4. ✓

**Placeholder scan:** No "TBD", "fill in", "similar to", or open-ended "handle edge cases". Every code block is complete and self-contained.

**Type/identifier consistency:** `GetBankAccountsRequest`, `GetBankAccountsResponse`, `GetBankAccountsHandler`, `Accounts` (property), `BankAccountDto`, `BankAccountSettings`, `BankAccountConfiguration`, `BankClientProvider`, `CurrencyCode` — all names match across tasks and match the real codebase per the file reads done during plan authoring. The log template `"Retrieved {Count} bank accounts"` is identical in Task 4's implementation and arch-review amendment #5.

Plan is complete.
