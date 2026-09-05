### task: extract-new-to-opened-side-effect

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/NewToOpenedSideEffect.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/NewToOpenedSideEffectTests.cs`

This is a mechanical move of `HandleNewToOpened`'s body (lines 214–248 of the current
`ChangeTransportBoxStateHandler.cs`) into its own class, unchanged.

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class NewToOpenedSideEffectTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly NewToOpenedSideEffect _sut;

    public NewToOpenedSideEffectTests()
    {
        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("tester", "Tester", "tester@test.com", true));
        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _sut = new NewToOpenedSideEffect(
            _repositoryMock.Object, _currentUserServiceMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public void Supports_NewToOpened_ReturnsTrue()
    {
        _sut.Supports(TransportBoxState.New, TransportBoxState.Opened).Should().BeTrue();
    }

    [Theory]
    [InlineData(TransportBoxState.Opened, TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.New, TransportBoxState.Quarantine)]
    public void Supports_AnyOtherPair_ReturnsFalse(TransportBoxState from, TransportBoxState to)
    {
        _sut.Supports(from, to).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MissingBoxCode_ReturnsRequiredFieldMissing()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Opened };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().Contain("field", "BoxCode");
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateActiveCode_ReturnsDuplicateActiveBoxFound()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1, NewState = TransportBoxState.Opened, BoxCode = "b999"
        };
        _repositoryMock.Setup(x => x.IsBoxCodeActiveAsync("B999")).ReturnsAsync(true);

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxDuplicateActiveBoxFound);
        result.Params.Should().Contain("code", "B999");
    }

    [Fact]
    public async Task ExecuteAsync_ValidCode_ClosesStaleStockedBoxesWithSameCode_ReturnsNull()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1, NewState = TransportBoxState.Opened, BoxCode = "B999"
        };
        _repositoryMock.Setup(x => x.IsBoxCodeActiveAsync("B999")).ReturnsAsync(false);

        var staleBox = new TransportBox();
        _repositoryMock
            .Setup(x => x.GetPagedListAsync(0, 0, null, null, "B999", TransportBoxState.Stocked, null, null, null))
            .ReturnsAsync((new List<TransportBox> { staleBox }, 1));

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
        _repositoryMock.Verify(x => x.UpdateAsync(staleBox, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile (NewToOpenedSideEffect does not exist yet)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~NewToOpenedSideEffectTests"`
Expected: Build error — `NewToOpenedSideEffect` does not exist.

> Note: confirm the exact `GetPagedListAsync` overload/parameter order against
> `ITransportBoxRepository.cs` before finalizing this test — match its real signature rather
> than the illustrative call above if they differ (e.g. named vs. positional optional args).

- [ ] **Step 3: Implement `NewToOpenedSideEffect`**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class NewToOpenedSideEffect : ITransportBoxTransitionSideEffect
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public NewToOpenedSideEffect(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.New && to == TransportBoxState.Opened;

    public async Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.BoxCode))
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string> { { "field", "BoxCode" } }
            };
        }

        // Check if another active box with the same code already exists
        var normalizedCode = request.BoxCode.ToUpper();
        var isCodeActive = await _repository.IsBoxCodeActiveAsync(normalizedCode);
        if (isCodeActive)
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                Params = new Dictionary<string, string> { { "code", normalizedCode } }
            };
        }

        // Close all stocked boxes
        var (stocked, _) = await _repository.GetPagedListAsync(skip: 0, take: 0, code: request.BoxCode, state: TransportBoxState.Stocked);
        foreach (var s in stocked)
        {
            s.Close(_timeProvider.GetUtcNow().UtcDateTime, _currentUserService.GetCurrentUser().Name ?? "System");
            await _repository.UpdateAsync(s, cancellationToken);
        }

        return null;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~NewToOpenedSideEffectTests"`
Expected: PASS (adjust the `GetPagedListAsync` mock setup to the real signature if step 3's copy differs).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/NewToOpenedSideEffect.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/NewToOpenedSideEffectTests.cs
git commit -m "feat(logistics): extract NewToOpenedSideEffect from ChangeTransportBoxStateHandler"
```

---
