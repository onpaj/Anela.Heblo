### task: fix-webhook-replay-identity-resolution

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs:60-74`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventRequest.cs:1-9`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs:1-65`
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs`

#### Goal
Fix the single ADR-005 violation described in the spec (FR-1) in one pass: all three production files plus the test file change together, since they are tightly coupled (compiler enforces the wiring) and the fix is a single mechanical relocation of one identity read.

#### Context (from spec.r1.md / arch-review.r1.md / design.r1.md)
- ADR-005 (`docs/architecture/development_guidelines.md:288`) requires all identity resolution to happen inside the MediatR handler via injected `ICurrentUserService`. Controllers must never resolve identity themselves, and request DTOs must carry no client-settable `UserId`/`ModifiedBy`-style fields.
- `ICurrentUserService` (`backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`) exposes `CurrentUser GetCurrentUser()`. `CurrentUser` (`backend/src/Anela.Heblo.Domain/Features/Users/CurrentUser.cs`) is `public record CurrentUser(string? Id, string? Name, string? Email, bool IsAuthenticated)` — `.Name` is nullable, so the `?? "unknown"` fallback is required.
- `ICurrentUserService` is already registered in DI via `UsersModule.cs` (`AddUsersModule()`) and used by 60+ existing handlers — no new module wiring is needed.
- Reference pattern already live in `CreateAdjustmentHandler.cs` (`backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/CreateAdjustment/CreateAdjustmentHandler.cs:13,20,26,61`): `ICurrentUserService` injected via constructor, `_currentUserService.GetCurrentUser().Name ?? "unknown"` called inline in `Handle` to stamp an audit field.
- Reference pattern for mocking `ICurrentUserService` in tests, already live in `CreateJournalEntryHandlerTests.cs` (`backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalEntryHandlerTests.cs:16,23,48-50`): `new Mock<ICurrentUserService>()`, `.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser(Id: ..., Name: ..., Email: ..., IsAuthenticated: ...))`, then `.Object` passed into the handler's constructor.
- Grep confirms `ReplayedBy` is referenced only in `SmartsuppWebhookAuditController.cs`, `ReplayWebhookEventRequest.cs`, `ReplayWebhookEventHandler.cs`, and `ReplayWebhookEventHandlerTests.cs` — no frontend/OpenAPI client usage (the field was controller-populated, never client-supplied in the request body, since the route only carries `{id}` in the path).
- Existing behavior that must NOT change: `entry.ReplayCount += 1`, `entry.LastReplayedAt = DateTime.UtcNow`, the not-found (`ErrorCodes.ResourceNotFound`) and malformed-JSON (`ErrorCodes.InvalidOperation`) error paths, and the downstream `ProcessWebhookEventRequest` dispatch via `IMediator`.

#### Implementation steps

- [ ] **Step 1: Rewrite the test file to use a mocked `ICurrentUserService` instead of `ReplayedBy` on the request**

Replace the full contents of `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs` with:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class ReplayWebhookEventHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit_{Guid.NewGuid()}").Options);

    private static Mock<ICurrentUserService> CreateCurrentUserServiceMock(string? name = "ondra@anela.cz")
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: name, Email: "ondra@anela.cz", IsAuthenticated: true));
        return mock;
    }

    [Fact]
    public async Task Handle_DispatchesProcessWebhookEvent_AndIncrementsReplayCount()
    {
        using var ctx = CreateContext();
        var id = Guid.NewGuid();
        var body = """{"event":"conversation.opened","timestamp":"2026-05-13T10:00:00Z","account_id":"acc-1","app_id":"app-1","data":{"k":1}}""";
        ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
        {
            Id = id,
            ReceivedAt = DateTime.UtcNow,
            RawBody = body,
            EventName = "conversation.opened",
            AccountId = "acc-1",
            AppId = "app-1",
            EventTimestamp = DateTime.Parse("2026-05-13T10:00:00Z").ToUniversalTime(),
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
        await ctx.SaveChangesAsync();

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<ProcessWebhookEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessWebhookEventResponse { Handled = true });

        var currentUserService = CreateCurrentUserServiceMock("ondra@anela.cz");

        var handler = new ReplayWebhookEventHandler(ctx, mediator.Object, currentUserService.Object);
        var response = await handler.Handle(
            new ReplayWebhookEventRequest { Id = id }, default);

        response.Success.Should().BeTrue();
        response.ReplayCount.Should().Be(1);

        mediator.Verify(m => m.Send(It.Is<ProcessWebhookEventRequest>(r =>
            r.EventName == "conversation.opened" &&
            r.AccountId == "acc-1" &&
            r.AppId == "app-1" &&
            r.Data.GetProperty("k").GetInt32() == 1),
            It.IsAny<CancellationToken>()), Times.Once);

        var updated = await ctx.SmartsuppWebhookAuditEntries.SingleAsync();
        updated.ReplayCount.Should().Be(1);
        updated.LastReplayedAt.Should().NotBeNull();
        updated.LastReplayedBy.Should().Be("ondra@anela.cz");

        // Replay must not create a new audit row
        (await ctx.SmartsuppWebhookAuditEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenIdMissing()
    {
        using var ctx = CreateContext();
        var currentUserService = CreateCurrentUserServiceMock();
        var handler = new ReplayWebhookEventHandler(ctx, Mock.Of<IMediator>(), currentUserService.Object);

        var response = await handler.Handle(
            new ReplayWebhookEventRequest { Id = Guid.NewGuid() }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidOperation_WhenRawBodyIsMalformedJson()
    {
        using var ctx = CreateContext();
        var id = Guid.NewGuid();
        ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
        {
            Id = id,
            ReceivedAt = DateTime.UtcNow,
            RawBody = "not-json-at-all",
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.MalformedJson,
        });
        await ctx.SaveChangesAsync();

        var currentUserService = CreateCurrentUserServiceMock();
        var handler = new ReplayWebhookEventHandler(ctx, Mock.Of<IMediator>(), currentUserService.Object);
        var response = await handler.Handle(
            new ReplayWebhookEventRequest { Id = id }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }
}
```

This drops `ReplayedBy` from all three `ReplayWebhookEventRequest` construction sites, and passes a mocked `ICurrentUserService.Object` as the third argument to all three `ReplayWebhookEventHandler` construction sites — but `ReplayWebhookEventHandler`'s constructor doesn't yet accept a third argument, so this will not compile against current production code.

- [ ] **Step 2: Run the test file to verify it fails to build**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplayWebhookEventHandlerTests"`
Expected: Build FAILS with a compiler error, e.g. `CS1729: 'ReplayWebhookEventHandler' does not contain a constructor that takes 3 arguments` (and/or `CS0117: 'ReplayWebhookEventRequest' does not contain a definition for 'ReplayedBy'` is avoided since the property still exists at this point — only the handler constructor arity mismatch should fire).

- [ ] **Step 3: Remove `ReplayedBy` from `ReplayWebhookEventRequest`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventRequest.cs` with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;

public class ReplayWebhookEventRequest : IRequest<ReplayWebhookEventResponse>
{
    public Guid Id { get; set; }
}
```

- [ ] **Step 4: Inject `ICurrentUserService` into `ReplayWebhookEventHandler` and resolve `LastReplayedBy` from it**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs` with:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;

public class ReplayWebhookEventHandler
    : IRequestHandler<ReplayWebhookEventRequest, ReplayWebhookEventResponse>
{
    private readonly ApplicationDbContext _context;
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public ReplayWebhookEventHandler(
        ApplicationDbContext context,
        IMediator mediator,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    public async Task<ReplayWebhookEventResponse> Handle(
        ReplayWebhookEventRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await _context.SmartsuppWebhookAuditEntries
            .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

        if (entry is null)
            return new ReplayWebhookEventResponse(ErrorCodes.ResourceNotFound);

        JsonElement data;
        try
        {
            using var doc = JsonDocument.Parse(entry.RawBody);
            data = doc.RootElement.TryGetProperty("data", out var d) ? d.Clone() : default;
        }
        catch (JsonException)
        {
            return new ReplayWebhookEventResponse(ErrorCodes.InvalidOperation);
        }

        var timestamp = entry.EventTimestamp ?? DateTime.UtcNow;

        await _mediator.Send(new ProcessWebhookEventRequest
        {
            EventName = entry.EventName ?? "",
            Timestamp = timestamp,
            AccountId = entry.AccountId ?? "",
            AppId = entry.AppId ?? "",
            Data = data,
        }, cancellationToken);

        entry.ReplayCount += 1;
        entry.LastReplayedAt = DateTime.UtcNow;
        entry.LastReplayedBy = _currentUserService.GetCurrentUser().Name ?? "unknown";
        await _context.SaveChangesAsync(cancellationToken);

        return new ReplayWebhookEventResponse
        {
            ReplayCount = entry.ReplayCount,
            LastReplayedAt = entry.LastReplayedAt,
        };
    }
}
```

- [ ] **Step 5: Remove the controller's identity resolution**

In `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs`, replace the `Replay` action (currently lines 60-74):

```csharp
    [HttpPost("{id:guid}/replay")]
    [ProducesResponseType(typeof(ReplayWebhookEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReplayWebhookEventResponse>> Replay(
        Guid id,
        CancellationToken cancellationToken)
    {
        var replayedBy = User.Identity?.Name ?? "unknown";
        var response = await _mediator.Send(new ReplayWebhookEventRequest
        {
            Id = id,
            ReplayedBy = replayedBy,
        }, cancellationToken);
        return HandleResponse(response);
    }
```

with:

```csharp
    [HttpPost("{id:guid}/replay")]
    [ProducesResponseType(typeof(ReplayWebhookEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReplayWebhookEventResponse>> Replay(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new ReplayWebhookEventRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }
```

No other lines in this file change. No `using` needs removing — the controller does not import any `Users`-namespace type.

- [ ] **Step 6: Run the test file to verify it now builds and passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplayWebhookEventHandlerTests"`
Expected: Build succeeds; all 3 tests PASS (`Handle_DispatchesProcessWebhookEvent_AndIncrementsReplayCount`, `Handle_ReturnsResourceNotFound_WhenIdMissing`, `Handle_ReturnsInvalidOperation_WhenRawBodyIsMalformedJson`).

- [ ] **Step 7: Search for any other reference to `ReplayedBy` and confirm none remain outside the four touched files**

Run: `cd backend && grep -rn "ReplayedBy" --include="*.cs" src/ test/ | grep -v "LastReplayedBy"`
Expected: No output (empty) — the only remaining `ReplayedBy`-containing identifier in the codebase is `LastReplayedBy` (the unchanged domain/DB column name), which is excluded by the grep.

- [ ] **Step 8: Run the full Smartsupp test suite to check for regressions**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Smartsupp"`
Expected: All PASS. No other Smartsupp test should reference `ReplayedBy` or need changes (only `ReplayWebhookEventHandlerTests` touches this request/handler pair).

- [ ] **Step 9: Build and format the whole backend solution**

Run: `cd backend && dotnet build`
Expected: Build succeeds with no new warnings/errors.

Run: `cd backend && dotnet format`
Expected: No unexpected changes beyond what was already written above (or none at all, if the code above is already correctly formatted).

- [ ] **Step 10: Commit**

```bash
cd backend
git add src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs \
        src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventRequest.cs \
        src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs \
        test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs
git commit -m "fix(smartsupp): resolve webhook replay identity via ICurrentUserService (ADR-005)"
```

#### Acceptance criteria
- `ReplayWebhookEventRequest` no longer declares a `ReplayedBy` (or any other client-settable identity) property — confirmed by Step 3 and the Step 7 grep.
- `SmartsuppWebhookAuditController.Replay` no longer references `User.Identity` and sends `ReplayWebhookEventRequest` with only `Id` set — confirmed by Step 5.
- `ReplayWebhookEventHandler` takes `ICurrentUserService` as a constructor dependency and uses it to populate `entry.LastReplayedBy` in `Handle` — confirmed by Step 4.
- `entry.LastReplayedBy` is set from `ICurrentUserService.GetCurrentUser().Name` (falling back to `"unknown"` when null/empty), not from any request field — confirmed by Step 4's code and the Step 6 test run.
- Existing behavior for `entry.ReplayCount += 1`, `entry.LastReplayedAt`, the not-found and malformed-JSON error paths, and the downstream `ProcessWebhookEventRequest` dispatch is unchanged — confirmed by Step 4 (identical logic, only the `LastReplayedBy` line changed) and the Step 6/8 test runs, which retain the original assertions for these behaviors.
- `ReplayWebhookEventHandlerTests` is updated to construct `ReplayWebhookEventHandler` with a mocked `ICurrentUserService` instead of passing `ReplayedBy` on the request, and asserts `entry.LastReplayedBy` against the mock's returned value — confirmed by Step 1 and Step 6.
- `dotnet build` and `dotnet format` succeed; all tests in the touched test file (and no other test references `ReplayWebhookEventRequest.ReplayedBy`) pass — confirmed by Steps 7-9.
