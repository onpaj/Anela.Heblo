# Terminal Box-Fill Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a mobile terminal workflow that lets a warehouse worker scan a box, add manufactured products to it, and scan again to move the box to `InTransit` ("v přepravě").

**Architecture:** One new thin backend use case (`OpenOrResumeBoxByCode`) handles the atomic open-or-resume step. Every other step reuses existing transport-box endpoints. The bulk of the feature is a new React terminal workflow under `/terminal/box-fill` with three steps plus a done screen.

**Tech Stack:** .NET 8 + MediatR + MVC controllers (backend, xUnit + Moq + FluentAssertions tests); React + TypeScript + react-query + Tailwind (frontend, Jest + React Testing Library tests).

**Spec:** `docs/superpowers/specs/2026-05-16-terminal-box-fill-design.md`

---

## File Structure

**Backend (new files):**
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeRequest.cs`
- `.../OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeResponse.cs`
- `.../OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs`

**Backend (modified):**
- `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` — add `open-by-code` endpoint
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs` — add endpoint test

**Frontend (new files):**
- `frontend/src/api/hooks/useBoxFill.ts` — feature data layer (open/add/remove/transit)
- `frontend/src/components/terminal/TerminalScanInput.tsx` — reusable scan field
- `frontend/src/components/terminal/box-fill/boxCode.ts` — box-code format helper
- `frontend/src/components/terminal/box-fill/ScanBoxStep.tsx`
- `frontend/src/components/terminal/box-fill/AmountEntrySheet.tsx`
- `frontend/src/components/terminal/box-fill/OverdraftSheet.tsx`
- `frontend/src/components/terminal/box-fill/AddItemsStep.tsx`
- `frontend/src/components/terminal/box-fill/ConfirmTransitStep.tsx`
- `frontend/src/components/terminal/box-fill/BoxFillDoneStep.tsx`
- `frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx`
- Test files under matching `__tests__/` folders.

**Frontend (modified):**
- `frontend/src/types/errors.ts` — add `TransportBoxDuplicateActiveBoxFound` code
- `frontend/src/components/terminal/TerminalHome.tsx` — add the fourth tile
- `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx` — cover the new tile
- `frontend/src/App.tsx` — route `/terminal/box-fill` to `BoxFillWorkflow`

---

## Task 1: Backend — `OpenOrResumeBoxByCode` use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs`

Context: MediatR handlers are auto-discovered by assembly scan — no manual registration needed. `ITransportBoxRepository.GetByCodeAsync` returns a box (with `Items` and `StateLog` eagerly loaded) ordered so non-`Closed` boxes win over `Closed` ones. A box only has a non-null `Code` after `Open` was called, so `GetByCodeAsync` never returns a `New`-state box.

- [ ] **Step 1: Create the request and response contracts**

Create `OpenOrResumeBoxByCodeRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;

public class OpenOrResumeBoxByCodeRequest : IRequest<OpenOrResumeBoxByCodeResponse>
{
    public string BoxCode { get; set; } = string.Empty;
}
```

Create `OpenOrResumeBoxByCodeResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;

public class OpenOrResumeBoxByCodeResponse : BaseResponse
{
    public TransportBoxDto? TransportBox { get; set; }
    public bool Resumed { get; set; }

    public OpenOrResumeBoxByCodeResponse() : base()
    {
    }

    public OpenOrResumeBoxByCodeResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters)
    {
    }
}
```

- [ ] **Step 2: Write the failing handler tests**

Create `OpenOrResumeBoxByCodeHandlerTests.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class OpenOrResumeBoxByCodeHandlerTests
{
    private static readonly DateTime FixedTime = new(2026, 5, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITransportBoxRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<OpenOrResumeBoxByCodeHandler>> _loggerMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly OpenOrResumeBoxByCodeHandler _handler;

    public OpenOrResumeBoxByCodeHandlerTests()
    {
        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("00000000-0000-0000-0000-000000000001", "Test User", "test@example.com", true));
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedTime));
        _mapperMock.Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>())).Returns(new TransportBoxDto());
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransportBox b, CancellationToken _) => b);
        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _repositoryMock.Setup(r => r.IsBoxCodeActiveAsync(It.IsAny<string>())).ReturnsAsync(false);

        _handler = new OpenOrResumeBoxByCodeHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object);
    }

    private static TransportBox OpenedBox(string code)
    {
        var box = new TransportBox { ConcurrencyStamp = Guid.NewGuid().ToString(), ExtraProperties = "{}" };
        box.Open(code, FixedTime, "Test User");
        return box;
    }

    private static TransportBox ClosedBox(string code)
    {
        var box = OpenedBox(code);
        box.AddItem("P-1", "Product 1", 1, FixedTime, "Test User");
        box.ToTransit(FixedTime, "Test User");
        box.Receive(FixedTime, "Test User");
        box.ToPick(FixedTime, "Test User");
        box.Close(FixedTime, "Test User");
        return box;
    }

    private static TransportBox InTransitBox(string code)
    {
        var box = OpenedBox(code);
        box.AddItem("P-1", "Product 1", 1, FixedTime, "Test User");
        box.ToTransit(FixedTime, "Test User");
        return box;
    }

    [Fact]
    public async Task Handle_EmptyCode_ReturnsRequiredFieldMissing()
    {
        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "  " }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
    }

    [Fact]
    public async Task Handle_InvalidCodeFormat_ReturnsValidationError()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync(It.IsAny<string>())).ReturnsAsync((TransportBox?)null);

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "XYZ" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Handle_NoExistingBox_CreatesAndOpensNewBox()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync((TransportBox?)null);
        TransportBox? added = null;
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransportBox b, CancellationToken _) => b)
            .Callback<TransportBox, CancellationToken>((b, _) => added = b);

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Resumed.Should().BeFalse();
        added.Should().NotBeNull();
        added!.State.Should().Be(TransportBoxState.Opened);
        added.Code.Should().Be("B001");
    }

    [Fact]
    public async Task Handle_ExistingOpenedBox_ResumesWithoutCreating()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync(OpenedBox("B001"));

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Resumed.Should().BeTrue();
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingClosedBox_CreatesNewBox()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync(ClosedBox("B001"));

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Resumed.Should().BeFalse();
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BoxBusyInTransit_ReturnsDuplicateActiveBoxFound()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync(InTransitBox("B001"));

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxDuplicateActiveBoxFound);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OpenOrResumeBoxByCodeHandlerTests"`
Expected: FAIL — `OpenOrResumeBoxByCodeHandler` does not exist (compile error).

- [ ] **Step 4: Implement the handler**

Create `OpenOrResumeBoxByCodeHandler.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;

public class OpenOrResumeBoxByCodeHandler : IRequestHandler<OpenOrResumeBoxByCodeRequest, OpenOrResumeBoxByCodeResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<OpenOrResumeBoxByCodeHandler> _logger;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public OpenOrResumeBoxByCodeHandler(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<OpenOrResumeBoxByCodeHandler> logger,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<OpenOrResumeBoxByCodeResponse> Handle(OpenOrResumeBoxByCodeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.BoxCode))
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.RequiredFieldMissing,
                    new Dictionary<string, string> { { "field", "BoxCode" } });
            }

            var code = request.BoxCode.Trim().ToUpper();
            var user = _currentUserService.GetCurrentUser();
            var userName = user.IsAuthenticated ? user.Name ?? "Unknown User" : "Anonymous";
            var now = DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Utc);

            var existing = await _repository.GetByCodeAsync(code);

            // Resume an in-progress box.
            if (existing != null && existing.State == TransportBoxState.Opened)
            {
                return new OpenOrResumeBoxByCodeResponse
                {
                    TransportBox = _mapper.Map<TransportBoxDto>(existing),
                    Resumed = true
                };
            }

            // A box with this code is busy in a non-resumable state.
            if (existing != null && existing.State != TransportBoxState.Closed)
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                    new Dictionary<string, string> { { "code", code }, { "state", existing.State.ToString() } });
            }

            // No box, or only a Closed box with this code — create and open a fresh one.
            if (await _repository.IsBoxCodeActiveAsync(code))
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                    new Dictionary<string, string> { { "code", code } });
            }

            var box = new TransportBox
            {
                CreatorId = Guid.TryParse(user.Id, out var userId) ? userId : null,
                CreationTime = now,
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                ExtraProperties = "{}"
            };
            box.Open(code, now, userName);

            await _repository.AddAsync(box, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Opened new transport box {Code} (id {BoxId}) by {User}", code, box.Id, userName);

            return new OpenOrResumeBoxByCodeResponse
            {
                TransportBox = _mapper.Map<TransportBoxDto>(box),
                Resumed = false
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error opening box by code {Code}: {Error}", request.BoxCode, ex.Message);
            return new OpenOrResumeBoxByCodeResponse(ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "details", ex.Message } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening box by code {Code}", request.BoxCode);
            return new OpenOrResumeBoxByCodeResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "details", ex.Message } });
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OpenOrResumeBoxByCodeHandlerTests"`
Expected: PASS — 6 tests pass.

- [ ] **Step 6: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs
git commit -m "feat(logistics): add OpenOrResumeBoxByCode use case"
```

---

## Task 2: Backend — `open-by-code` controller endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs`

- [ ] **Step 1: Write the failing controller test**

In `TransportBoxControllerTests.cs`, add this `using` to the top with the other `using` lines:

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;
```

Add these two tests inside the `TransportBoxControllerTests` class, before the closing brace:

```csharp
    [Fact]
    public async Task OpenOrResumeBoxByCode_Success_Returns200()
    {
        var response = new OpenOrResumeBoxByCodeResponse { Success = true, Resumed = false };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<OpenOrResumeBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.OpenOrResumeBoxByCode(
            new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task OpenOrResumeBoxByCode_Failure_Returns400()
    {
        var response = new OpenOrResumeBoxByCodeResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<OpenOrResumeBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.OpenOrResumeBoxByCode(
            new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxControllerTests.OpenOrResumeBoxByCode"`
Expected: FAIL — `_controller.OpenOrResumeBoxByCode` does not exist (compile error).

- [ ] **Step 3: Add the endpoint to the controller**

In `TransportBoxController.cs`, add this `using` with the other use-case `using` lines:

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;
```

Add this method inside `TransportBoxController`, immediately after the `GetTransportBoxByCode` method (before the class closing brace):

```csharp
    /// <summary>
    /// Open a transport box by code, or resume it if one is already Opened (terminal box-fill workflow)
    /// </summary>
    [HttpPost("open-by-code")]
    public async Task<ActionResult<OpenOrResumeBoxByCodeResponse>> OpenOrResumeBoxByCode(
        [FromBody] OpenOrResumeBoxByCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxControllerTests.OpenOrResumeBoxByCode"`
Expected: PASS — 2 tests pass.

- [ ] **Step 5: Build, format, and commit**

```bash
cd backend && dotnet build && dotnet format
git add backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxControllerTests.cs
git commit -m "feat(logistics): expose open-by-code transport box endpoint"
```

---

## Task 3: Frontend — box-fill data layer

**Files:**
- Create: `frontend/src/api/hooks/useBoxFill.ts`
- Create: `frontend/src/components/terminal/box-fill/boxCode.ts`
- Create: `frontend/src/components/terminal/box-fill/__tests__/boxCode.test.ts`
- Create: `frontend/src/api/hooks/__tests__/useBoxFill.test.ts`
- Modify: `frontend/src/types/errors.ts`

Context: terminal hooks talk to the API with raw `fetch` via the authenticated client's internals (the same pattern as `useAddItemToBox` in `useTransportBoxes.ts`), because the generated client is only regenerated on a full build. The `add` and `remove` endpoints return HTTP 400 with a JSON body on business failures, so the parser must read the body regardless of status.

- [ ] **Step 1: Add the missing error code to the frontend enum**

In `frontend/src/types/errors.ts`, find the line `TransportBoxItemError = 1404,` and add a line directly after it:

```typescript
  TransportBoxDuplicateActiveBoxFound = 1405,
```

(The Czech message for this key already exists in `frontend/src/i18n.ts`.)

- [ ] **Step 2: Write the failing box-code helper test**

Create `frontend/src/components/terminal/box-fill/__tests__/boxCode.test.ts`:

```typescript
import { isValidBoxCode } from "../boxCode";

describe("isValidBoxCode", () => {
  it("accepts B followed by exactly 3 digits", () => {
    expect(isValidBoxCode("B001")).toBe(true);
    expect(isValidBoxCode("b123")).toBe(true);
  });

  it("trims surrounding whitespace before validating", () => {
    expect(isValidBoxCode("  B001  ")).toBe(true);
  });

  it("rejects wrong prefixes, lengths, and non-digits", () => {
    expect(isValidBoxCode("A001")).toBe(false);
    expect(isValidBoxCode("B01")).toBe(false);
    expect(isValidBoxCode("B0012")).toBe(false);
    expect(isValidBoxCode("BXYZ")).toBe(false);
    expect(isValidBoxCode("")).toBe(false);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/boxCode.test.ts`
Expected: FAIL — cannot find module `../boxCode`.

- [ ] **Step 4: Implement the box-code helper**

Create `frontend/src/components/terminal/box-fill/boxCode.ts`:

```typescript
// A valid transport box code is the letter B followed by exactly 3 digits (e.g. B001).
export const isValidBoxCode = (code: string): boolean => /^B\d{3}$/i.test(code.trim());
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/boxCode.test.ts`
Expected: PASS.

- [ ] **Step 6: Create the box-fill hooks**

Create `frontend/src/api/hooks/useBoxFill.ts`:

```typescript
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export interface TerminalBoxItem {
  id: number;
  productCode: string;
  productName: string;
  amount: number;
  lotNumber?: string;
}

export interface TerminalBox {
  id: number;
  code: string;
  state: string;
  itemCount: number;
  items: TerminalBoxItem[];
}

export interface BoxFillResult {
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
  transportBox?: TerminalBox;
  resumed?: boolean;
}

export interface AddBoxItemInput {
  boxId: number;
  productCode: string;
  productName: string;
  amount: number;
  sourceInventoryId?: number;
  lotNumber?: string;
  expirationDate?: string;
  allowNegativeStock?: boolean;
}

const getInternals = (): ApiClientWithInternals =>
  getAuthenticatedApiClient() as unknown as ApiClientWithInternals;

// The add/remove/state endpoints return HTTP 400 with a JSON body on business
// failures, so always read the body and surface { success: false } from it.
const parseResult = async (response: Response): Promise<BoxFillResult> => {
  try {
    return (await response.json()) as BoxFillResult;
  } catch {
    return { success: false };
  }
};

export const useOpenOrResumeBox = () =>
  useMutation({
    mutationFn: async (boxCode: string): Promise<BoxFillResult> => {
      const api = getInternals();
      const response = await api.http.fetch(`${api.baseUrl}/api/transport-boxes/open-by-code`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ boxCode }),
      });
      return parseResult(response);
    },
  });

export const useAddBoxItem = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddBoxItemInput): Promise<BoxFillResult> => {
      const api = getInternals();
      const response = await api.http.fetch(`${api.baseUrl}/api/transport-boxes/${input.boxId}/items`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      });
      return parseResult(response);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};

export const useRemoveBoxItem = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { boxId: number; itemId: number }): Promise<BoxFillResult> => {
      const api = getInternals();
      const response = await api.http.fetch(
        `${api.baseUrl}/api/transport-boxes/${input.boxId}/items/${input.itemId}`,
        { method: "DELETE" },
      );
      return parseResult(response);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.manufacturedProductInventory });
    },
  });
};

export const useSendBoxToTransit = () =>
  useMutation({
    mutationFn: async (boxId: number): Promise<BoxFillResult> => {
      const api = getInternals();
      const response = await api.http.fetch(`${api.baseUrl}/api/transport-boxes/${boxId}/state`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ boxId, newState: "InTransit" }),
      });
      return parseResult(response);
    },
  });
```

- [ ] **Step 7: Write the hook test**

Create `frontend/src/api/hooks/__tests__/useBoxFill.test.ts`:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useOpenOrResumeBox, useAddBoxItem } from "../useBoxFill";
import * as clientModule from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { manufacturedProductInventory: ["manufactured-product-inventory"] },
}));

const mockGetClient = clientModule.getAuthenticatedApiClient as jest.MockedFunction<
  typeof clientModule.getAuthenticatedApiClient
>;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

const setFetch = (response: Partial<Response> & { json: () => Promise<unknown> }) => {
  mockGetClient.mockReturnValue({
    baseUrl: "http://test",
    http: { fetch: jest.fn().mockResolvedValue(response) },
  } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);
};

describe("useBoxFill", () => {
  it("useOpenOrResumeBox returns the parsed success body", async () => {
    setFetch({
      ok: true,
      json: async () => ({ success: true, resumed: true, transportBox: { id: 1, code: "B001", state: "Opened", itemCount: 0, items: [] } }),
    });

    const { result } = renderHook(() => useOpenOrResumeBox(), { wrapper: createWrapper });
    const res = await result.current.mutateAsync("B001");

    expect(res.success).toBe(true);
    expect(res.resumed).toBe(true);
    expect(res.transportBox?.code).toBe("B001");
  });

  it("useAddBoxItem surfaces a failure body returned with HTTP 400", async () => {
    setFetch({
      ok: false,
      json: async () => ({ success: false, errorCode: 1404 }),
    });

    const { result } = renderHook(() => useAddBoxItem(), { wrapper: createWrapper });
    const res = await result.current.mutateAsync({
      boxId: 1, productCode: "P-1", productName: "Product 1", amount: 2,
    });

    await waitFor(() => expect(res.success).toBe(false));
    expect(res.errorCode).toBe(1404);
  });
});
```

- [ ] **Step 8: Run the hook test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useBoxFill.test.ts`
Expected: PASS — 2 tests pass.

- [ ] **Step 9: Commit**

```bash
git add frontend/src/api/hooks/useBoxFill.ts frontend/src/api/hooks/__tests__/useBoxFill.test.ts frontend/src/components/terminal/box-fill/boxCode.ts frontend/src/components/terminal/box-fill/__tests__/boxCode.test.ts frontend/src/types/errors.ts
git commit -m "feat(terminal): add box-fill data layer and box-code helper"
```

---

## Task 4: Frontend — `TerminalScanInput` shared component

**Files:**
- Create: `frontend/src/components/terminal/TerminalScanInput.tsx`
- Test: `frontend/src/components/terminal/__tests__/TerminalScanInput.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/terminal/__tests__/TerminalScanInput.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TerminalScanInput from "../TerminalScanInput";

describe("TerminalScanInput", () => {
  it("submits a trimmed, uppercased value and clears the field", () => {
    const onScan = jest.fn();
    render(<TerminalScanInput label="Kód boxu" onScan={onScan} />);

    const input = screen.getByTestId("terminal-scan-input") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "  b001  " } });
    fireEvent.click(screen.getByTestId("terminal-scan-submit"));

    expect(onScan).toHaveBeenCalledWith("B001");
    expect(input.value).toBe("");
  });

  it("does not submit an empty value", () => {
    const onScan = jest.fn();
    render(<TerminalScanInput label="Kód boxu" onScan={onScan} />);

    fireEvent.click(screen.getByTestId("terminal-scan-submit"));

    expect(onScan).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/__tests__/TerminalScanInput.test.tsx`
Expected: FAIL — cannot find module `../TerminalScanInput`.

- [ ] **Step 3: Implement the component**

Create `frontend/src/components/terminal/TerminalScanInput.tsx`:

```tsx
import React, { useRef, useState } from "react";

interface TerminalScanInputProps {
  label: string;
  placeholder?: string;
  onScan: (value: string) => void;
  disabled?: boolean;
  autoFocus?: boolean;
}

const TerminalScanInput: React.FC<TerminalScanInputProps> = ({
  label,
  placeholder,
  onScan,
  disabled = false,
  autoFocus = true,
}) => {
  const [value, setValue] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = value.trim();
    if (!trimmed) return;
    onScan(trimmed.toUpperCase());
    setValue("");
    inputRef.current?.focus();
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <label className="block text-sm font-medium text-neutral-slate">{label}</label>
      <input
        ref={inputRef}
        type="text"
        autoFocus={autoFocus}
        disabled={disabled}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        placeholder={placeholder ?? "Naskenujte kód"}
        data-testid="terminal-scan-input"
        className="w-full px-4 py-3 text-lg font-mono border border-border-light rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-blue disabled:opacity-50"
      />
      <button
        type="submit"
        disabled={disabled || !value.trim()}
        data-testid="terminal-scan-submit"
        className="w-full py-3 text-base font-semibold text-white bg-primary-blue rounded-xl disabled:opacity-50"
      >
        Potvrdit
      </button>
    </form>
  );
};

export default TerminalScanInput;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/__tests__/TerminalScanInput.test.tsx`
Expected: PASS — 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/TerminalScanInput.tsx frontend/src/components/terminal/__tests__/TerminalScanInput.test.tsx
git commit -m "feat(terminal): add reusable TerminalScanInput component"
```

---

## Task 5: Frontend — `ScanBoxStep`

**Files:**
- Create: `frontend/src/components/terminal/box-fill/ScanBoxStep.tsx`
- Test: `frontend/src/components/terminal/box-fill/__tests__/ScanBoxStep.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/terminal/box-fill/__tests__/ScanBoxStep.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import ScanBoxStep from "../ScanBoxStep";
import * as useBoxFill from "../../../../api/hooks/useBoxFill";

jest.mock("../../../../utils/errorHandler", () => ({
  getErrorMessage: () => "Chyba kódu",
}));

const mockMutateAsync = jest.fn();
jest.spyOn(useBoxFill, "useOpenOrResumeBox").mockReturnValue({
  mutateAsync: mockMutateAsync,
  isPending: false,
} as unknown as ReturnType<typeof useBoxFill.useOpenOrResumeBox>);

const scan = (code: string) => {
  fireEvent.change(screen.getByTestId("terminal-scan-input"), { target: { value: code } });
  fireEvent.click(screen.getByTestId("terminal-scan-submit"));
};

describe("ScanBoxStep", () => {
  beforeEach(() => mockMutateAsync.mockReset());

  it("rejects an invalid box code without calling the API", () => {
    render(<ScanBoxStep onBoxReady={jest.fn()} />);
    scan("XYZ");
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  it("calls onBoxReady when the box opens successfully", async () => {
    const box = { id: 1, code: "B001", state: "Opened", itemCount: 0, items: [] };
    mockMutateAsync.mockResolvedValue({ success: true, resumed: true, transportBox: box });
    const onBoxReady = jest.fn();

    render(<ScanBoxStep onBoxReady={onBoxReady} />);
    scan("B001");

    await waitFor(() => expect(onBoxReady).toHaveBeenCalledWith(box, true));
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/ScanBoxStep.test.tsx`
Expected: FAIL — cannot find module `../ScanBoxStep`.

- [ ] **Step 3: Implement the component**

Create `frontend/src/components/terminal/box-fill/ScanBoxStep.tsx`:

```tsx
import React, { useState } from "react";
import { AlertCircle, Loader } from "lucide-react";
import TerminalScanInput from "../TerminalScanInput";
import { useOpenOrResumeBox, type TerminalBox } from "../../../api/hooks/useBoxFill";
import { isValidBoxCode } from "./boxCode";
import { getErrorMessage } from "../../../utils/errorHandler";

interface ScanBoxStepProps {
  onBoxReady: (box: TerminalBox, resumed: boolean) => void;
}

const ScanBoxStep: React.FC<ScanBoxStepProps> = ({ onBoxReady }) => {
  const [error, setError] = useState<string | null>(null);
  const openBox = useOpenOrResumeBox();

  const handleScan = async (code: string) => {
    setError(null);
    if (!isValidBoxCode(code)) {
      setError("Neplatný kód boxu. Očekává se formát B + 3 číslice (např. B001).");
      return;
    }
    const result = await openBox.mutateAsync(code);
    if (!result.success || !result.transportBox) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Box se nepodařilo otevřít");
      return;
    }
    onBoxReady(result.transportBox, result.resumed ?? false);
  };

  return (
    <div className="space-y-4 pt-2">
      <h1 className="text-xl font-bold text-neutral-slate">Naskenujte box</h1>
      <p className="text-sm text-neutral-gray">
        Naskenujte kód prázdného nebo rozpracovaného boxu pro zahájení plnění.
      </p>
      <TerminalScanInput
        label="Kód boxu"
        placeholder="B001"
        onScan={(v) => void handleScan(v)}
        disabled={openBox.isPending}
      />
      {openBox.isPending && (
        <div className="flex items-center gap-2 text-sm text-neutral-gray">
          <Loader className="h-4 w-4 animate-spin" /> Otevírám box...
        </div>
      )}
      {error && (
        <div
          role="alert"
          className="flex items-center gap-2 text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2"
        >
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          {error}
        </div>
      )}
    </div>
  );
};

export default ScanBoxStep;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/ScanBoxStep.test.tsx`
Expected: PASS — 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/box-fill/ScanBoxStep.tsx frontend/src/components/terminal/box-fill/__tests__/ScanBoxStep.test.tsx
git commit -m "feat(terminal): add box-fill scan step"
```

---

## Task 6: Frontend — `AmountEntrySheet` and `OverdraftSheet`

**Files:**
- Create: `frontend/src/components/terminal/box-fill/AmountEntrySheet.tsx`
- Create: `frontend/src/components/terminal/box-fill/OverdraftSheet.tsx`
- Test: `frontend/src/components/terminal/box-fill/__tests__/AmountEntrySheet.test.tsx`
- Test: `frontend/src/components/terminal/box-fill/__tests__/OverdraftSheet.test.tsx`

Context: both are presentational bottom-sheet components. They take a `ManufacturedProductInventoryItem` (type exported from `frontend/src/api/hooks/useManufacturedProductInventory.ts`, with fields `id`, `productCode`, `productName`, `lotNumber?`, `expirationDate?`, `amount`).

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/terminal/box-fill/__tests__/AmountEntrySheet.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import AmountEntrySheet from "../AmountEntrySheet";
import type { ManufacturedProductInventoryItem } from "../../../../api/hooks/useManufacturedProductInventory";

const item: ManufacturedProductInventoryItem = {
  id: 7, productCode: "P-1", productName: "Krém", amount: 10, createdAt: "", createdBy: "", log: [],
};

describe("AmountEntrySheet", () => {
  it("prefills the initial amount and confirms a positive number", () => {
    const onConfirm = jest.fn();
    render(
      <AmountEntrySheet item={item} initialAmount={3} isSubmitting={false} onConfirm={onConfirm} onCancel={jest.fn()} />,
    );

    expect((screen.getByTestId("amount-entry-input") as HTMLInputElement).value).toBe("3");
    fireEvent.click(screen.getByTestId("amount-entry-confirm"));
    expect(onConfirm).toHaveBeenCalledWith(3);
  });

  it("shows an error and does not confirm a non-positive amount", () => {
    const onConfirm = jest.fn();
    render(
      <AmountEntrySheet item={item} isSubmitting={false} onConfirm={onConfirm} onCancel={jest.fn()} />,
    );

    fireEvent.click(screen.getByTestId("amount-entry-confirm"));
    expect(onConfirm).not.toHaveBeenCalled();
    expect(screen.getByText("Zadejte kladné číslo")).toBeInTheDocument();
  });
});
```

Create `frontend/src/components/terminal/box-fill/__tests__/OverdraftSheet.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import OverdraftSheet from "../OverdraftSheet";
import type { ManufacturedProductInventoryItem } from "../../../../api/hooks/useManufacturedProductInventory";

const item: ManufacturedProductInventoryItem = {
  id: 7, productCode: "P-1", productName: "Krém", amount: 4, createdAt: "", createdBy: "", log: [],
};

describe("OverdraftSheet", () => {
  it("offers negative-stock and remaining-only choices", () => {
    const onAddNegative = jest.fn();
    const onAddRemaining = jest.fn();
    render(
      <OverdraftSheet
        item={item}
        requestedAmount={10}
        isSubmitting={false}
        onAddNegative={onAddNegative}
        onAddRemaining={onAddRemaining}
        onCancel={jest.fn()}
      />,
    );

    fireEvent.click(screen.getByTestId("overdraft-add-negative"));
    fireEvent.click(screen.getByTestId("overdraft-add-remaining"));
    expect(onAddNegative).toHaveBeenCalledTimes(1);
    expect(onAddRemaining).toHaveBeenCalledTimes(1);
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/AmountEntrySheet.test.tsx src/components/terminal/box-fill/__tests__/OverdraftSheet.test.tsx`
Expected: FAIL — cannot find modules `../AmountEntrySheet` and `../OverdraftSheet`.

- [ ] **Step 3: Implement `AmountEntrySheet`**

Create `frontend/src/components/terminal/box-fill/AmountEntrySheet.tsx`:

```tsx
import React, { useState } from "react";
import { AlertCircle } from "lucide-react";
import type { ManufacturedProductInventoryItem } from "../../../api/hooks/useManufacturedProductInventory";

interface AmountEntrySheetProps {
  item: ManufacturedProductInventoryItem;
  initialAmount?: number;
  isSubmitting: boolean;
  onConfirm: (amount: number) => void;
  onCancel: () => void;
}

const AmountEntrySheet: React.FC<AmountEntrySheetProps> = ({
  item,
  initialAmount,
  isSubmitting,
  onConfirm,
  onCancel,
}) => {
  const [value, setValue] = useState(initialAmount ? String(initialAmount) : "");
  const [error, setError] = useState<string | null>(null);

  const submit = () => {
    const parsed = parseFloat(value);
    if (!value || isNaN(parsed) || parsed <= 0) {
      setError("Zadejte kladné číslo");
      return;
    }
    onConfirm(parsed);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/40" onClick={onCancel}>
      <div
        className="bg-white rounded-t-2xl w-full max-w-md p-5 space-y-4"
        onClick={(e) => e.stopPropagation()}
      >
        <div>
          <p className="font-semibold text-neutral-slate">{item.productName}</p>
          <p className="text-xs text-neutral-gray font-mono">
            {item.productCode}
            {item.lotNumber ? ` • Šarže: ${item.lotNumber}` : ""} • Sklad: {item.amount}
          </p>
        </div>
        <input
          type="number"
          inputMode="decimal"
          autoFocus
          value={value}
          onChange={(e) => {
            setValue(e.target.value);
            setError(null);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              submit();
            }
          }}
          step="0.01"
          min="0.01"
          placeholder="Množství"
          data-testid="amount-entry-input"
          className="w-full px-4 py-3 text-lg border border-border-light rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-blue"
        />
        {error && (
          <div className="flex items-center gap-1 text-xs text-red-600">
            <AlertCircle className="h-3 w-3" /> {error}
          </div>
        )}
        <div className="flex gap-3">
          <button
            type="button"
            onClick={onCancel}
            className="flex-1 py-3 text-base font-medium text-neutral-slate bg-gray-100 rounded-xl"
          >
            Zrušit
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={isSubmitting}
            data-testid="amount-entry-confirm"
            className="flex-1 py-3 text-base font-semibold text-white bg-primary-blue rounded-xl disabled:opacity-50"
          >
            Přidat
          </button>
        </div>
      </div>
    </div>
  );
};

export default AmountEntrySheet;
```

- [ ] **Step 4: Implement `OverdraftSheet`**

Create `frontend/src/components/terminal/box-fill/OverdraftSheet.tsx`:

```tsx
import React from "react";
import { AlertCircle } from "lucide-react";
import type { ManufacturedProductInventoryItem } from "../../../api/hooks/useManufacturedProductInventory";

interface OverdraftSheetProps {
  item: ManufacturedProductInventoryItem;
  requestedAmount: number;
  isSubmitting: boolean;
  onAddNegative: () => void;
  onAddRemaining: () => void;
  onCancel: () => void;
}

const OverdraftSheet: React.FC<OverdraftSheetProps> = ({
  item,
  requestedAmount,
  isSubmitting,
  onAddNegative,
  onAddRemaining,
  onCancel,
}) => {
  const missing = requestedAmount - item.amount;
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/40" onClick={onCancel}>
      <div
        className="bg-white rounded-t-2xl w-full max-w-md p-5 space-y-4"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start gap-3">
          <AlertCircle className="h-6 w-6 text-amber-500 flex-shrink-0 mt-0.5" />
          <div>
            <p className="font-semibold text-neutral-slate">{item.productName}</p>
            <p className="text-sm text-neutral-gray mt-1">
              Na skladě je pouze <strong>{item.amount}</strong>, požadováno <strong>{requestedAmount}</strong>.
            </p>
          </div>
        </div>
        <button
          type="button"
          onClick={onAddNegative}
          disabled={isSubmitting}
          data-testid="overdraft-add-negative"
          className="w-full py-4 text-base font-semibold text-white bg-amber-600 rounded-xl disabled:opacity-50"
        >
          Přidat záporný stav ({requestedAmount} ks, {missing} chybí)
        </button>
        <button
          type="button"
          onClick={onAddRemaining}
          disabled={isSubmitting}
          data-testid="overdraft-add-remaining"
          className="w-full py-4 text-base font-semibold text-neutral-slate bg-gray-100 rounded-xl disabled:opacity-50"
        >
          Přidat pouze zbývající ({item.amount} ks)
        </button>
        <button type="button" onClick={onCancel} className="w-full py-2 text-sm text-neutral-gray">
          Zrušit
        </button>
      </div>
    </div>
  );
};

export default OverdraftSheet;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/AmountEntrySheet.test.tsx src/components/terminal/box-fill/__tests__/OverdraftSheet.test.tsx`
Expected: PASS — 3 tests pass.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/box-fill/AmountEntrySheet.tsx frontend/src/components/terminal/box-fill/OverdraftSheet.tsx frontend/src/components/terminal/box-fill/__tests__/AmountEntrySheet.test.tsx frontend/src/components/terminal/box-fill/__tests__/OverdraftSheet.test.tsx
git commit -m "feat(terminal): add amount-entry and overdraft sheets"
```

---

## Task 7: Frontend — `AddItemsStep`

**Files:**
- Create: `frontend/src/components/terminal/box-fill/AddItemsStep.tsx`
- Test: `frontend/src/components/terminal/box-fill/__tests__/AddItemsStep.test.tsx`

Context: this step composes the inventory list, the box-contents list, the amount/overdraft sheets, and the proceed button. It reads stock from `useManufacturedProductInventoryQuery({ onlyWithStock: true })` and writes through `useAddBoxItem` / `useRemoveBoxItem`. Overdraft is detected client-side (`amount > item.amount`) before any API call.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/terminal/box-fill/__tests__/AddItemsStep.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import AddItemsStep from "../AddItemsStep";
import * as inventoryHook from "../../../../api/hooks/useManufacturedProductInventory";
import * as useBoxFill from "../../../../api/hooks/useBoxFill";

jest.mock("../../../../utils/errorHandler", () => ({ getErrorMessage: () => "Chyba" }));

const inventoryItem = {
  id: 7, productCode: "P-1", productName: "Krém", amount: 10, createdAt: "", createdBy: "", log: [],
};

const box: useBoxFill.TerminalBox = { id: 1, code: "B001", state: "Opened", itemCount: 0, items: [] };

const addMutateAsync = jest.fn();
const removeMutateAsync = jest.fn();

beforeEach(() => {
  addMutateAsync.mockReset();
  removeMutateAsync.mockReset();
  jest.spyOn(inventoryHook, "useManufacturedProductInventoryQuery").mockReturnValue({
    data: { items: [inventoryItem], totalCount: 1 }, isLoading: false, error: null,
  } as unknown as ReturnType<typeof inventoryHook.useManufacturedProductInventoryQuery>);
  jest.spyOn(useBoxFill, "useAddBoxItem").mockReturnValue({
    mutateAsync: addMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useAddBoxItem>);
  jest.spyOn(useBoxFill, "useRemoveBoxItem").mockReturnValue({
    mutateAsync: removeMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useRemoveBoxItem>);
});

describe("AddItemsStep", () => {
  it("disables the proceed button while the box is empty", () => {
    render(
      <AddItemsStep box={box} resumed={false} amountMemory={{}} onBoxUpdated={jest.fn()} onAmountUsed={jest.fn()} onProceed={jest.fn()} />,
    );
    expect(screen.getByTestId("proceed-to-transit")).toBeDisabled();
  });

  it("adds an in-stock item and reports the box update and used amount", async () => {
    const updatedBox = { ...box, itemCount: 1, items: [{ id: 5, productCode: "P-1", productName: "Krém", amount: 2 }] };
    addMutateAsync.mockResolvedValue({ success: true, transportBox: updatedBox });
    const onBoxUpdated = jest.fn();
    const onAmountUsed = jest.fn();

    render(
      <AddItemsStep box={box} resumed={false} amountMemory={{}} onBoxUpdated={onBoxUpdated} onAmountUsed={onAmountUsed} onProceed={jest.fn()} />,
    );

    fireEvent.click(screen.getByTestId("inventory-row-7"));
    fireEvent.change(screen.getByTestId("amount-entry-input"), { target: { value: "2" } });
    fireEvent.click(screen.getByTestId("amount-entry-confirm"));

    await waitFor(() => expect(onBoxUpdated).toHaveBeenCalledWith(updatedBox));
    expect(onAmountUsed).toHaveBeenCalledWith("P-1", 2);
    expect(addMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ boxId: 1, productCode: "P-1", amount: 2, sourceInventoryId: 7, allowNegativeStock: false }),
    );
  });

  it("opens the overdraft sheet when the amount exceeds stock", () => {
    render(
      <AddItemsStep box={box} resumed={false} amountMemory={{}} onBoxUpdated={jest.fn()} onAmountUsed={jest.fn()} onProceed={jest.fn()} />,
    );

    fireEvent.click(screen.getByTestId("inventory-row-7"));
    fireEvent.change(screen.getByTestId("amount-entry-input"), { target: { value: "25" } });
    fireEvent.click(screen.getByTestId("amount-entry-confirm"));

    expect(screen.getByTestId("overdraft-add-negative")).toBeInTheDocument();
    expect(addMutateAsync).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/AddItemsStep.test.tsx`
Expected: FAIL — cannot find module `../AddItemsStep`.

- [ ] **Step 3: Implement the component**

Create `frontend/src/components/terminal/box-fill/AddItemsStep.tsx`:

```tsx
import React, { useState } from "react";
import { AlertCircle, FlaskConical, Loader, Search, Trash2 } from "lucide-react";
import {
  useManufacturedProductInventoryQuery,
  type ManufacturedProductInventoryItem,
} from "../../../api/hooks/useManufacturedProductInventory";
import { useAddBoxItem, useRemoveBoxItem, type TerminalBox } from "../../../api/hooks/useBoxFill";
import { getErrorMessage } from "../../../utils/errorHandler";
import AmountEntrySheet from "./AmountEntrySheet";
import OverdraftSheet from "./OverdraftSheet";

interface AddItemsStepProps {
  box: TerminalBox;
  resumed: boolean;
  amountMemory: Record<string, number>;
  onBoxUpdated: (box: TerminalBox) => void;
  onAmountUsed: (productCode: string, amount: number) => void;
  onProceed: () => void;
}

const AddItemsStep: React.FC<AddItemsStepProps> = ({
  box,
  resumed,
  amountMemory,
  onBoxUpdated,
  onAmountUsed,
  onProceed,
}) => {
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<ManufacturedProductInventoryItem | null>(null);
  const [overdraft, setOverdraft] = useState<{ item: ManufacturedProductInventoryItem; amount: number } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const { data, isLoading, error: loadError } = useManufacturedProductInventoryQuery({ onlyWithStock: true });
  const addItem = useAddBoxItem();
  const removeItem = useRemoveBoxItem();

  const items = (data?.items ?? []).filter((it) => {
    if (!search.trim()) return true;
    const q = search.toLowerCase();
    return (
      it.productName.toLowerCase().includes(q) ||
      it.productCode.toLowerCase().includes(q) ||
      (it.lotNumber ?? "").toLowerCase().includes(q)
    );
  });

  const performAdd = async (
    item: ManufacturedProductInventoryItem,
    amount: number,
    allowNegativeStock: boolean,
  ) => {
    setError(null);
    const result = await addItem.mutateAsync({
      boxId: box.id,
      productCode: item.productCode,
      productName: item.productName,
      amount,
      sourceInventoryId: item.id,
      lotNumber: item.lotNumber,
      expirationDate: item.expirationDate,
      allowNegativeStock,
    });
    if (!result.success || !result.transportBox) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Položku se nepodařilo přidat");
      return;
    }
    onBoxUpdated(result.transportBox);
    onAmountUsed(item.productCode, amount);
    setSelected(null);
    setOverdraft(null);
  };

  const handleAmountConfirm = (amount: number) => {
    if (!selected) return;
    if (amount > selected.amount) {
      setOverdraft({ item: selected, amount });
      setSelected(null);
      return;
    }
    void performAdd(selected, amount, false);
  };

  const handleRemove = async (itemId: number) => {
    setError(null);
    const result = await removeItem.mutateAsync({ boxId: box.id, itemId });
    if (!result.success || !result.transportBox) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Položku se nepodařilo odebrat");
      return;
    }
    onBoxUpdated(result.transportBox);
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-bold text-neutral-slate">Box {box.code}</h1>
        <span className="text-sm text-neutral-gray">{box.items.length} pol.</span>
      </div>

      {resumed && box.items.length > 0 && (
        <div className="flex items-center gap-2 text-sm text-amber-700 bg-amber-50 border border-amber-200 rounded-lg px-3 py-2">
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          Pokračujete v rozpracovaném boxu ({box.items.length} položek).
        </div>
      )}

      {error && (
        <div
          role="alert"
          className="flex items-center gap-2 text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2"
        >
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          {error}
        </div>
      )}

      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-neutral-gray" />
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Hledat produkt, kód nebo šarži..."
          data-testid="add-items-search"
          className="w-full pl-9 pr-3 py-2 text-sm border border-border-light rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-blue"
        />
      </div>

      {isLoading && (
        <div className="flex items-center justify-center gap-2 py-6 text-sm text-neutral-gray">
          <Loader className="h-4 w-4 animate-spin" /> Načítám zásoby...
        </div>
      )}
      {loadError && (
        <div className="flex items-center gap-2 py-4 text-sm text-red-600">
          <AlertCircle className="h-4 w-4" /> Chyba při načítání zásob
        </div>
      )}
      {!isLoading && !loadError && items.length === 0 && (
        <p className="text-center py-6 text-sm text-neutral-gray">Žádné dostupné zásoby</p>
      )}
      {!isLoading && !loadError && items.length > 0 && (
        <div className="border border-border-light rounded-lg divide-y divide-border-light">
          {items.map((it) => (
            <button
              key={it.id}
              type="button"
              onClick={() => {
                setError(null);
                setSelected(it);
              }}
              data-testid={`inventory-row-${it.id}`}
              className="w-full text-left px-3 py-3 hover:bg-secondary-blue-pale active:bg-secondary-blue-pale flex items-center gap-3"
            >
              <FlaskConical className="h-4 w-4 text-primary-blue flex-shrink-0" />
              <div className="min-w-0 flex-1">
                <div className="text-sm font-medium text-neutral-slate truncate">{it.productName}</div>
                <div className="text-xs text-neutral-gray flex flex-wrap gap-x-3">
                  <span className="font-mono">{it.productCode}</span>
                  {it.lotNumber && <span>Šarže: {it.lotNumber}</span>}
                  <span className="font-semibold text-green-700">Sklad: {it.amount}</span>
                </div>
              </div>
            </button>
          ))}
        </div>
      )}

      {box.items.length > 0 && (
        <div>
          <h2 className="text-sm font-semibold text-neutral-slate mb-1">V boxu</h2>
          <div className="border border-border-light rounded-lg divide-y divide-border-light">
            {box.items.map((it) => (
              <div key={it.id} className="flex items-center gap-3 px-3 py-2" data-testid={`box-item-${it.id}`}>
                <div className="min-w-0 flex-1">
                  <div className="text-sm text-neutral-slate truncate">{it.productName}</div>
                  <div className="text-xs text-neutral-gray font-mono">
                    {it.productCode} • {it.amount}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => void handleRemove(it.id)}
                  disabled={removeItem.isPending}
                  aria-label="Odebrat položku"
                  data-testid={`remove-item-${it.id}`}
                  className="p-2 text-red-600 rounded-md hover:bg-red-50 disabled:opacity-50"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      <button
        type="button"
        onClick={onProceed}
        disabled={box.items.length === 0}
        data-testid="proceed-to-transit"
        className="w-full py-3 text-base font-semibold text-white bg-primary-blue rounded-xl disabled:opacity-50"
      >
        Odeslat do přepravy
      </button>

      {selected && (
        <AmountEntrySheet
          item={selected}
          initialAmount={amountMemory[selected.productCode]}
          isSubmitting={addItem.isPending}
          onConfirm={handleAmountConfirm}
          onCancel={() => setSelected(null)}
        />
      )}
      {overdraft && (
        <OverdraftSheet
          item={overdraft.item}
          requestedAmount={overdraft.amount}
          isSubmitting={addItem.isPending}
          onAddNegative={() => void performAdd(overdraft.item, overdraft.amount, true)}
          onAddRemaining={() => void performAdd(overdraft.item, overdraft.item.amount, false)}
          onCancel={() => setOverdraft(null)}
        />
      )}
    </div>
  );
};

export default AddItemsStep;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/AddItemsStep.test.tsx`
Expected: PASS — 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/box-fill/AddItemsStep.tsx frontend/src/components/terminal/box-fill/__tests__/AddItemsStep.test.tsx
git commit -m "feat(terminal): add box-fill add-items step"
```

---

## Task 8: Frontend — `ConfirmTransitStep`

**Files:**
- Create: `frontend/src/components/terminal/box-fill/ConfirmTransitStep.tsx`
- Test: `frontend/src/components/terminal/box-fill/__tests__/ConfirmTransitStep.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/terminal/box-fill/__tests__/ConfirmTransitStep.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import ConfirmTransitStep from "../ConfirmTransitStep";
import * as useBoxFill from "../../../../api/hooks/useBoxFill";

jest.mock("../../../../utils/errorHandler", () => ({ getErrorMessage: () => "Chyba" }));

const box: useBoxFill.TerminalBox = {
  id: 1, code: "B001", state: "Opened", itemCount: 1,
  items: [{ id: 5, productCode: "P-1", productName: "Krém", amount: 2 }],
};

const mockMutateAsync = jest.fn();

beforeEach(() => {
  mockMutateAsync.mockReset();
  jest.spyOn(useBoxFill, "useSendBoxToTransit").mockReturnValue({
    mutateAsync: mockMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useSendBoxToTransit>);
});

const scan = (code: string) => {
  fireEvent.change(screen.getByTestId("terminal-scan-input"), { target: { value: code } });
  fireEvent.click(screen.getByTestId("terminal-scan-submit"));
};

describe("ConfirmTransitStep", () => {
  it("rejects a scanned code that does not match the box", () => {
    render(<ConfirmTransitStep box={box} onConfirmed={jest.fn()} onBack={jest.fn()} />);
    scan("B999");
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  it("sends the box to transit when the scanned code matches", async () => {
    mockMutateAsync.mockResolvedValue({ success: true });
    const onConfirmed = jest.fn();
    render(<ConfirmTransitStep box={box} onConfirmed={onConfirmed} onBack={jest.fn()} />);
    scan("B001");
    await waitFor(() => expect(onConfirmed).toHaveBeenCalled());
    expect(mockMutateAsync).toHaveBeenCalledWith(1);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/ConfirmTransitStep.test.tsx`
Expected: FAIL — cannot find module `../ConfirmTransitStep`.

- [ ] **Step 3: Implement the component**

Create `frontend/src/components/terminal/box-fill/ConfirmTransitStep.tsx`:

```tsx
import React, { useState } from "react";
import { AlertCircle, Loader } from "lucide-react";
import TerminalScanInput from "../TerminalScanInput";
import { useSendBoxToTransit, type TerminalBox } from "../../../api/hooks/useBoxFill";
import { getErrorMessage } from "../../../utils/errorHandler";

interface ConfirmTransitStepProps {
  box: TerminalBox;
  onConfirmed: () => void;
  onBack: () => void;
}

const ConfirmTransitStep: React.FC<ConfirmTransitStepProps> = ({ box, onConfirmed, onBack }) => {
  const [error, setError] = useState<string | null>(null);
  const sendToTransit = useSendBoxToTransit();

  const handleScan = async (code: string) => {
    setError(null);
    if (code !== box.code) {
      setError(`Naskenovaný kód ${code} neodpovídá boxu ${box.code}.`);
      return;
    }
    const result = await sendToTransit.mutateAsync(box.id);
    if (!result.success) {
      setError(result.errorCode ? getErrorMessage(result.errorCode, result.params) : "Box se nepodařilo odeslat");
      return;
    }
    onConfirmed();
  };

  return (
    <div className="space-y-4 pt-2">
      <h1 className="text-xl font-bold text-neutral-slate">Potvrďte odeslání</h1>
      <p className="text-sm text-neutral-gray">
        Naskenujte box <strong className="font-mono">{box.code}</strong> ({box.items.length} položek) pro přesun do
        přepravy.
      </p>
      <TerminalScanInput
        label="Naskenujte box pro potvrzení"
        placeholder={box.code}
        onScan={(v) => void handleScan(v)}
        disabled={sendToTransit.isPending}
      />
      {sendToTransit.isPending && (
        <div className="flex items-center gap-2 text-sm text-neutral-gray">
          <Loader className="h-4 w-4 animate-spin" /> Odesílám do přepravy...
        </div>
      )}
      {error && (
        <div
          role="alert"
          className="flex items-center gap-2 text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2"
        >
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          {error}
        </div>
      )}
      <button type="button" onClick={onBack} className="w-full py-2 text-sm text-neutral-gray">
        Zpět na položky
      </button>
    </div>
  );
};

export default ConfirmTransitStep;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/ConfirmTransitStep.test.tsx`
Expected: PASS — 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/box-fill/ConfirmTransitStep.tsx frontend/src/components/terminal/box-fill/__tests__/ConfirmTransitStep.test.tsx
git commit -m "feat(terminal): add box-fill confirm-transit step"
```

---

## Task 9: Frontend — `BoxFillDoneStep` and `BoxFillWorkflow`

**Files:**
- Create: `frontend/src/components/terminal/box-fill/BoxFillDoneStep.tsx`
- Create: `frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx`
- Test: `frontend/src/components/terminal/box-fill/__tests__/BoxFillWorkflow.test.tsx`

Context: the workflow orchestrator holds the current step, the box, the `resumed` flag, and the amount-memory map. The test mocks the four step components so it only verifies orchestration (step transitions and prop wiring).

- [ ] **Step 1: Write the failing workflow test**

Create `frontend/src/components/terminal/box-fill/__tests__/BoxFillWorkflow.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import BoxFillWorkflow from "../BoxFillWorkflow";
import type { TerminalBox } from "../../../../api/hooks/useBoxFill";

// Must be prefixed with `mock` so jest allows it inside the hoisted jest.mock factory.
const mockBox: TerminalBox = {
  id: 1, code: "B001", state: "Opened", itemCount: 1,
  items: [{ id: 5, productCode: "P-1", productName: "Krém", amount: 2 }],
};

jest.mock("../ScanBoxStep", () => ({
  __esModule: true,
  default: ({ onBoxReady }: { onBoxReady: (b: TerminalBox, r: boolean) => void }) => (
    <button onClick={() => onBoxReady(mockBox, false)}>scan</button>
  ),
}));
jest.mock("../AddItemsStep", () => ({
  __esModule: true,
  default: ({ onProceed }: { onProceed: () => void }) => <button onClick={onProceed}>add</button>,
}));
jest.mock("../ConfirmTransitStep", () => ({
  __esModule: true,
  default: ({ onConfirmed }: { onConfirmed: () => void }) => <button onClick={onConfirmed}>confirm</button>,
}));
jest.mock("../BoxFillDoneStep", () => ({
  __esModule: true,
  default: ({ onNext }: { onNext: () => void }) => <button onClick={onNext}>done</button>,
}));

describe("BoxFillWorkflow", () => {
  it("advances scan -> add -> confirm -> done and loops back to scan", () => {
    render(<BoxFillWorkflow />);

    expect(screen.getByText("scan")).toBeInTheDocument();
    fireEvent.click(screen.getByText("scan"));
    fireEvent.click(screen.getByText("add"));
    fireEvent.click(screen.getByText("confirm"));
    expect(screen.getByText("done")).toBeInTheDocument();
    fireEvent.click(screen.getByText("done"));
    expect(screen.getByText("scan")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/BoxFillWorkflow.test.tsx`
Expected: FAIL — cannot find module `../BoxFillWorkflow`.

- [ ] **Step 3: Implement `BoxFillDoneStep`**

Create `frontend/src/components/terminal/box-fill/BoxFillDoneStep.tsx`:

```tsx
import React from "react";
import { CheckCircle2 } from "lucide-react";

interface BoxFillDoneStepProps {
  boxCode: string;
  itemCount: number;
  onNext: () => void;
}

const BoxFillDoneStep: React.FC<BoxFillDoneStepProps> = ({ boxCode, itemCount, onNext }) => (
  <div className="space-y-5 pt-8 text-center">
    <CheckCircle2 className="mx-auto h-16 w-16 text-green-600" />
    <div>
      <h1 className="text-xl font-bold text-neutral-slate">Box odeslán do přepravy</h1>
      <p className="text-sm text-neutral-gray mt-1">
        Box <strong className="font-mono">{boxCode}</strong> s {itemCount} položkami je nyní v přepravě.
      </p>
    </div>
    <button
      type="button"
      onClick={onNext}
      data-testid="box-fill-next"
      className="w-full py-3 text-base font-semibold text-white bg-primary-blue rounded-xl"
    >
      Další box
    </button>
  </div>
);

export default BoxFillDoneStep;
```

- [ ] **Step 4: Implement `BoxFillWorkflow`**

Create `frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx`:

```tsx
import React, { useState } from "react";
import ScanBoxStep from "./ScanBoxStep";
import AddItemsStep from "./AddItemsStep";
import ConfirmTransitStep from "./ConfirmTransitStep";
import BoxFillDoneStep from "./BoxFillDoneStep";
import type { TerminalBox } from "../../../api/hooks/useBoxFill";

type Step = "scan" | "add" | "confirm" | "done";

const BoxFillWorkflow: React.FC = () => {
  const [step, setStep] = useState<Step>("scan");
  const [box, setBox] = useState<TerminalBox | null>(null);
  const [resumed, setResumed] = useState(false);
  // Amount memory survives across boxes within this terminal session.
  const [amountMemory, setAmountMemory] = useState<Record<string, number>>({});

  const reset = () => {
    setBox(null);
    setResumed(false);
    setStep("scan");
  };

  if (step === "scan" || !box) {
    return (
      <ScanBoxStep
        onBoxReady={(openedBox, wasResumed) => {
          setBox(openedBox);
          setResumed(wasResumed);
          setStep("add");
        }}
      />
    );
  }

  if (step === "add") {
    return (
      <AddItemsStep
        box={box}
        resumed={resumed}
        amountMemory={amountMemory}
        onBoxUpdated={setBox}
        onAmountUsed={(productCode, amount) =>
          setAmountMemory((memory) => ({ ...memory, [productCode]: amount }))
        }
        onProceed={() => setStep("confirm")}
      />
    );
  }

  if (step === "confirm") {
    return <ConfirmTransitStep box={box} onConfirmed={() => setStep("done")} onBack={() => setStep("add")} />;
  }

  return <BoxFillDoneStep boxCode={box.code} itemCount={box.items.length} onNext={reset} />;
};

export default BoxFillWorkflow;
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/box-fill/__tests__/BoxFillWorkflow.test.tsx`
Expected: PASS — 1 test passes.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/box-fill/BoxFillDoneStep.tsx frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx frontend/src/components/terminal/box-fill/__tests__/BoxFillWorkflow.test.tsx
git commit -m "feat(terminal): add box-fill workflow orchestrator and done step"
```

---

## Task 10: Frontend — wire the workflow into the terminal

**Files:**
- Modify: `frontend/src/components/terminal/TerminalHome.tsx`
- Modify: `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Update the `TerminalHome` test**

In `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx`, add this test inside the `describe('TerminalHome', ...)` block, before its closing `});`:

```tsx
  it('renders an active tile for box filling', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-box-fill');
    expect(tile).toHaveAttribute('href', '/terminal/box-fill');
  });
```

The existing `shows coming-soon label on all tiles` test still expects exactly 3 `Brzy k dispozici` labels — the new tile is active and must NOT carry that label, so leave that test unchanged.

- [ ] **Step 2: Run the test to verify the new test fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/__tests__/TerminalHome.test.tsx`
Expected: FAIL — `workflow-tile-box-fill` not found. (The `coming-soon label` test still passes.)

- [ ] **Step 3: Add the box-fill tile to `TerminalHome`**

Replace the entire contents of `frontend/src/components/terminal/TerminalHome.tsx` with:

```tsx
import React from 'react';
import { Link } from 'react-router-dom';
import { Package, ClipboardList, Tag, PackagePlus, ChevronRight } from 'lucide-react';

interface WorkflowTile {
  id: string;
  title: string;
  description: string;
  href: string;
  icon: React.ElementType;
  comingSoon: boolean;
}

const WORKFLOWS: WorkflowTile[] = [
  {
    id: 'box-fill',
    title: 'Plnění boxu',
    description: 'Naskenujte box, přidejte vyrobené produkty a odešlete do přepravy',
    href: '/terminal/box-fill',
    icon: PackagePlus,
    comingSoon: false,
  },
  {
    id: 'receive',
    title: 'Příjem boxu',
    description: 'Naskenujte kód boxu a potvrďte příjem zásilky do skladu',
    href: '/terminal/receive',
    icon: Package,
    comingSoon: true,
  },
  {
    id: 'stocktake',
    title: 'Inventura',
    description: 'Inventarizace materiálu a surovin po šaržích',
    href: '/terminal/stocktake',
    icon: ClipboardList,
    comingSoon: true,
  },
  {
    id: 'lot-identification',
    title: 'Identifikace šarže',
    description: 'Evidujte šarže při příjmu a sledujte spotřebu ve výrobě',
    href: '/terminal/lot-identification',
    icon: Tag,
    comingSoon: true,
  },
];

const TerminalHome: React.FC = () => (
  <div className="space-y-3 pt-2">
    <h1 className="text-xl font-bold text-neutral-slate">Vyberte operaci</h1>
    {WORKFLOWS.map(({ id, title, description, href, icon: Icon, comingSoon }) => (
      <Link
        key={id}
        to={href}
        data-testid={`workflow-tile-${id}`}
        className="flex items-center gap-4 bg-white border border-border-light rounded-xl p-4 shadow-soft hover:border-primary-blue hover:shadow-hover transition-all min-h-[72px]"
      >
        <div className="flex-shrink-0 w-12 h-12 bg-secondary-blue-pale rounded-xl flex items-center justify-center">
          <Icon className="h-6 w-6 text-primary-blue" />
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-base font-semibold text-neutral-slate">{title}</p>
          <p className="text-sm text-neutral-gray mt-0.5">{description}</p>
          {comingSoon && (
            <span className="text-xs text-neutral-gray italic">Brzy k dispozici</span>
          )}
        </div>
        <ChevronRight className="h-5 w-5 text-neutral-gray flex-shrink-0" />
      </Link>
    ))}
  </div>
);

export default TerminalHome;
```

- [ ] **Step 4: Wire the route in `App.tsx`**

In `frontend/src/App.tsx`, add this import next to the other terminal imports (near `import TerminalHome from "./components/terminal/TerminalHome";`):

```tsx
import BoxFillWorkflow from "./components/terminal/box-fill/BoxFillWorkflow";
```

Then, inside the `<Route path="/terminal" element={<TerminalLayout />}>` block, add the box-fill route directly after the `index` route:

```tsx
                        <Route path="box-fill" element={<BoxFillWorkflow />} />
```

- [ ] **Step 5: Run the `TerminalHome` test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal/__tests__/TerminalHome.test.tsx`
Expected: PASS — all tests pass (including the unchanged `coming-soon label` test, which still finds exactly 3 labels).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/TerminalHome.tsx frontend/src/components/terminal/__tests__/TerminalHome.test.tsx frontend/src/App.tsx
git commit -m "feat(terminal): wire box-fill workflow into terminal home and routing"
```

---

## Task 11: Full validation

**Files:** none (verification only)

- [ ] **Step 1: Backend build, format check, and tests**

Run: `cd backend && dotnet build`
Expected: build succeeds with no errors.

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting changes needed. If it reports changes, run `dotnet format` and commit them.

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Logistics"`
Expected: all Logistics tests pass.

- [ ] **Step 2: Frontend build and lint**

Run: `cd frontend && npm run build`
Expected: build succeeds (this also regenerates the OpenAPI client, picking up the new `open-by-code` endpoint).

Run: `cd frontend && npm run lint`
Expected: no lint errors.

- [ ] **Step 3: Frontend test suite for the new feature**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/terminal src/api/hooks/__tests__/useBoxFill.test.ts`
Expected: all terminal and box-fill tests pass.

- [ ] **Step 4: Commit any formatting fixes**

If steps 1-3 produced changes (e.g. `dotnet format` or a regenerated `api-client.ts`):

```bash
git add -A
git commit -m "chore(terminal): apply formatting and regenerated API client"
```

If there were no changes, skip this commit.

- [ ] **Step 5: Push the branch**

```bash
git push -u origin claude/add-items-to-box-JYDhK
```

---

## Notes

- **E2E test (optional, not blocking):** the spec lists an optional Playwright spec under `frontend/test/e2e/` in the transport/logistics module. The E2E suite runs nightly, not in PR CI, so it is intentionally not part of the TDD task flow above. Add it as a follow-up if desired.
- **Enum serialization:** the API uses string enum serialization (`TransportBoxState` is a string enum in the generated client), so `useSendBoxToTransit` sending `newState: "InTransit"` as a JSON string is correct.
- **Amount memory** is in-memory React state — it intentionally resets on a full page reload, matching the spec.
