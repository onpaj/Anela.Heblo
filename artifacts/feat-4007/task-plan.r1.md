# UpdateTransportBoxDescriptionHandler Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cover the two untested error-return branches (box-not-found, exception-caught) and complete the happy-path assertions of `UpdateTransportBoxDescriptionHandler.Handle`, raising line coverage above the 60% filter threshold, with zero production-code changes.

**Architecture:** Single new xUnit test file, `UpdateTransportBoxDescriptionHandlerTests.cs`, mocking `ITransportBoxRepository` and `IMediator` with Moq, asserting with FluentAssertions — matching the exact conventions already used by sibling handler test files in the same directory (`RemoveItemFromBoxHandlerTests.cs`, `GetTransportBoxByIdHandlerTests.cs`).

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, MediatR.

Full plan also saved to `docs/superpowers/plans/2026-08-31-update-transport-box-description-handler-tests.md`.

---

### task: add-update-transport-box-description-handler-tests

## Goal
Create `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/UpdateTransportBoxDescriptionHandlerTests.cs` with three `[Fact]` tests covering not-found, exception, and full happy-path branches of `UpdateTransportBoxDescriptionHandler.Handle`. No production code changes.

## Files to change

**Create:**
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/UpdateTransportBoxDescriptionHandlerTests.cs`

**Do not touch:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/UpdateTransportBoxDescription/UpdateTransportBoxDescriptionHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/UpdateTransportBoxDescription/UpdateTransportBoxDescriptionRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/UpdateTransportBoxDescription/UpdateTransportBoxDescriptionResponse.cs`
- Any other file in `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/`

## Reference — production code under test (read-only, do not modify)

`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/UpdateTransportBoxDescription/UpdateTransportBoxDescriptionHandler.cs`:

```csharp
public class UpdateTransportBoxDescriptionHandler : IRequestHandler<UpdateTransportBoxDescriptionRequest, UpdateTransportBoxDescriptionResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IMediator _mediator;
    private readonly ILogger<UpdateTransportBoxDescriptionHandler> _logger;

    public UpdateTransportBoxDescriptionHandler(
        ITransportBoxRepository repository,
        IMediator mediator,
        ILogger<UpdateTransportBoxDescriptionHandler> logger)
    {
        _repository = repository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<UpdateTransportBoxDescriptionResponse> Handle(UpdateTransportBoxDescriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var box = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (box == null)
            {
                return new UpdateTransportBoxDescriptionResponse(
                    ErrorCodes.TransportBoxNotFound,
                    new Dictionary<string, string>() { { nameof(request.BoxId), request.BoxId.ToString() } }
                );
            }

            box.Description = request.Description;

            await _repository.UpdateAsync(box, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            var updatedBoxRequest = new GetTransportBoxByIdRequest { Id = request.BoxId };
            var updatedBox = await _mediator.Send(updatedBoxRequest, cancellationToken);

            _logger.LogInformation("Transport box {BoxId} description updated", request.BoxId);

            return new UpdateTransportBoxDescriptionResponse
            {
                UpdatedBox = updatedBox
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating description for transport box {BoxId}", request.BoxId);
            return new UpdateTransportBoxDescriptionResponse(
                ErrorCodes.TransportBoxStateChangeError,
                new Dictionary<string, string> { { "boxId", request.BoxId.ToString() } }
            );
        }
    }
}
```

Note the two `Params` keys are asymmetric on purpose in the current code: the not-found branch uses `"BoxId"` (via `nameof(request.BoxId)`), the exception branch uses a hardcoded lowercase `"boxId"`. Assert both exactly as-is — this is existing behavior, not something this task fixes.

`ITransportBoxRepository` relevant members (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs`):
```csharp
Task<TransportBox?> GetByIdWithDetailsAsync(int id);
Task UpdateAsync(TransportBox box, CancellationToken cancellationToken);
Task<int> SaveChangesAsync(CancellationToken cancellationToken);
```

- [ ] **Step 1: Write the test file with all three failing/pending tests**

Create `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/UpdateTransportBoxDescriptionHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Features.Logistics.UseCases.UpdateTransportBoxDescription;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class UpdateTransportBoxDescriptionHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<UpdateTransportBoxDescriptionHandler>> _loggerMock;
    private readonly UpdateTransportBoxDescriptionHandler _handler;

    public UpdateTransportBoxDescriptionHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<UpdateTransportBoxDescriptionHandler>>();

        _handler = new UpdateTransportBoxDescriptionHandler(
            _repositoryMock.Object,
            _mediatorMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_BoxNotFound_ReturnsTransportBoxNotFoundError()
    {
        // Arrange
        var request = new UpdateTransportBoxDescriptionRequest { BoxId = 999, Description = "New description" };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((TransportBox?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxNotFound);
        result.Params.Should().ContainKey("BoxId").WhoseValue.Should().Be("999");
        result.UpdatedBox.Should().BeNull();

        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _mediatorMock.Verify(x => x.Send(It.IsAny<IRequest<GetTransportBoxByIdResponse>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RepositoryThrows_ReturnsTransportBoxStateChangeError()
    {
        // Arrange
        var request = new UpdateTransportBoxDescriptionRequest { BoxId = 42, Description = "New description" };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(42))
            .ThrowsAsync(new InvalidOperationException("simulated repository failure"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxStateChangeError);
        result.Params.Should().ContainKey("boxId").WhoseValue.Should().Be("42");
        result.UpdatedBox.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_UpdatesDescriptionAndReturnsUpdatedBox()
    {
        // Arrange
        var box = CreateBox(id: 7, description: "Old description");
        var request = new UpdateTransportBoxDescriptionRequest { BoxId = 7, Description = "New description" };
        var mediatorResponse = new GetTransportBoxByIdResponse();

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(7))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mediatorMock
            .Setup(x => x.Send(It.Is<GetTransportBoxByIdRequest>(r => r.Id == 7), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediatorResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.UpdatedBox.Should().BeSameAs(mediatorResponse);
        box.Description.Should().Be("New description");

        _repositoryMock.Verify(x => x.UpdateAsync(box, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mediatorMock.Verify(
            x => x.Send(It.Is<GetTransportBoxByIdRequest>(r => r.Id == 7), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static TransportBox CreateBox(int id, string? description = null)
    {
        var box = new TransportBox
        {
            Id = id,
            Description = description
        };

        return box;
    }
}
```

- [ ] **Step 2: Run the build to catch compile errors before running tests**

Run: `cd backend && dotnet build Anela.Heblo.sln`
Expected: `Build succeeded.` — if `TransportBox.Id` or `TransportBox.Description` are not plain settable properties (verify against `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs` if this fails), adjust `CreateBox` to use the same reflection-based field-set pattern as `RemoveItemFromBoxHandlerTests.CreateOpenBox()` instead of object-initializer syntax.

- [ ] **Step 3: Run the three new tests and confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~UpdateTransportBoxDescriptionHandlerTests"`
Expected: 3 tests passed — `Handle_BoxNotFound_ReturnsTransportBoxNotFoundError`, `Handle_RepositoryThrows_ReturnsTransportBoxStateChangeError`, `Handle_ValidRequest_UpdatesDescriptionAndReturnsUpdatedBox`.

If `Handle_ValidRequest_UpdatesDescriptionAndReturnsUpdatedBox` fails on the `_mediatorMock.Verify` line with a "no matching setup" or type-mismatch error, confirm `IMediator.Send`'s actual generic signature used by the installed MediatR version by checking how `_mediatorMock` is stubbed in any other passing test in this repo that mocks `IMediator` directly (grep `Mock<IMediator>` under `backend/test/`), and align the `Setup`/`Verify` expression's generic arguments to match — do not change the production handler to work around a mock signature mismatch.

- [ ] **Step 4: Run the full Logistics/Transport test folder to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Logistics.Transport"`
Expected: all tests in the folder pass, including the pre-existing `RemoveItemFromBoxHandlerTests`, `GetTransportBoxByIdHandlerTests`, `GetTransportBoxByCodeHandlerTests`, `OpenOrResumeBoxByCodeHandlerTests`, `AddItemToBoxHandlerTests`, `GetTransportBoxesHandlerTests`, `ChangeTransportBoxStateHandlerTests`, `GetTransportBoxSummaryHandlerTests`, and the new `UpdateTransportBoxDescriptionHandlerTests`.

- [ ] **Step 5: Run dotnet format**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting changes needed. If it reports changes, run `dotnet format` (without `--verify-no-changes`) and re-stage the new test file.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/UpdateTransportBoxDescriptionHandlerTests.cs
git commit -m "test(logistics): cover not-found and exception error paths in UpdateTransportBoxDescriptionHandler

Adds unit tests for the two previously-untested error-return branches
(repository returns null -> ErrorCodes.TransportBoxNotFound; repository
or mediator throws -> ErrorCodes.TransportBoxStateChangeError) plus a
full happy-path test asserting UpdateAsync/SaveChangesAsync are called
and the mediator-fetched updated box flows through to the response.

Fixes #4007"
```

## Acceptance criteria
- `UpdateTransportBoxDescriptionHandlerTests.cs` exists with exactly three `[Fact]` tests: not-found, repository-throws, happy-path (FR-1, FR-2, FR-3 of `spec.r1.md`).
- Not-found test asserts `ErrorCode == ErrorCodes.TransportBoxNotFound`, `Params["BoxId"] == "999"`, and that `UpdateAsync`/`SaveChangesAsync`/`mediator.Send` are never invoked.
- Exception test asserts `ErrorCode == ErrorCodes.TransportBoxStateChangeError` and `Params["boxId"] == "42"`.
- Happy-path test asserts the box's `Description` is mutated, `UpdateAsync` and `SaveChangesAsync` are each called exactly once, `mediator.Send` is called exactly once with a `GetTransportBoxByIdRequest` whose `Id` matches the request's `BoxId`, and `UpdatedBox` in the response is the exact object returned by the mocked mediator call.
- No file other than the new test file is created or modified.
- `dotnet build Anela.Heblo.sln` succeeds with no errors.
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Logistics.Transport"` passes in full, including all pre-existing tests in the folder.
- `dotnet format --verify-no-changes` reports no changes needed.
