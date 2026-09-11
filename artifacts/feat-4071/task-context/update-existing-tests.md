### task: update-existing-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`

The handler's constructor shape changed (task `refactor-handler-orchestration`). Both test
files construct it directly and must be updated to match — **without changing any existing
assertion**, per spec NFR-4. Since the handler no longer talks to
`IInventoryReservationService`/`ILogisticsStockOperationService` directly, and side effects are
now resolved via `IEnumerable<ITransportBoxTransitionSideEffect>`, real (non-mocked) instances
of the four side effects and the restorer are wired up in the test constructors so that
existing behavior-level assertions (e.g. `HandleReceived`'s staging behavior, the code-required
error, etc.) keep passing unmodified.

- [ ] **Step 1: Update `ChangeTransportBoxStateHandlerTests` constructor**

Replace the `_handler = new ChangeTransportBoxStateHandler(...)` block (and the fields it
depends on) with real side-effect instances built from the existing mocks, so every existing
`[Fact]`/`[Theory]` in this file keeps exercising the same mocked dependencies it did before:

```csharp
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IInventoryReservationService> _inventoryReservationServiceMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<ChangeTransportBoxStateHandler>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogisticsStockOperationService> _stockUpProcessingServiceMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly ChangeTransportBoxStateHandler _handler;

    public ChangeTransportBoxStateHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _inventoryReservationServiceMock = new Mock<IInventoryReservationService>();
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<ChangeTransportBoxStateHandler>>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _stockUpProcessingServiceMock = new Mock<ILogisticsStockOperationService>();
        _timeProviderMock = new Mock<TimeProvider>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user", "Test User", "test@example.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _stockUpProcessingServiceMock
            .Setup(x => x.StageOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sideEffects = new ITransportBoxTransitionSideEffect[]
        {
            new NewToOpenedSideEffect(_repositoryMock.Object, _currentUserServiceMock.Object, _timeProviderMock.Object),
            new OpenToReserveSideEffect(),
            new OpenToQuarantineSideEffect(),
            new ReceivedSideEffect(_stockUpProcessingServiceMock.Object, NullLogger<ReceivedSideEffect>.Instance),
        };
        var inventoryRestorer = new TransportBoxInventoryRestorer(_inventoryReservationServiceMock.Object);

        _handler = new ChangeTransportBoxStateHandler(
            _repositoryMock.Object,
            _mediatorMock.Object,
            _loggerMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object,
            sideEffects,
            inventoryRestorer);
    }
```

Add `using Microsoft.Extensions.Logging.Abstractions;` to this file's `using` block for
`NullLogger<T>` if not already present.

- [ ] **Step 2: Update `ChangeTransportBoxStateReceiveAtomicityIntegrationTests.CreateHandler`**

Replace the `return new ChangeTransportBoxStateHandler(...)` block with:

```csharp
        var sideEffects = new ITransportBoxTransitionSideEffect[]
        {
            new NewToOpenedSideEffect(transportBoxRepository, currentUserService.Object, TimeProvider.System),
            new OpenToReserveSideEffect(),
            new OpenToQuarantineSideEffect(),
            new ReceivedSideEffect(adapter, NullLogger<ReceivedSideEffect>.Instance),
        };
        var inventoryRestorer = new TransportBoxInventoryRestorer(Mock.Of<IInventoryReservationService>());

        return new ChangeTransportBoxStateHandler(
            transportBoxRepository,
            mediator.Object,
            NullLogger<ChangeTransportBoxStateHandler>.Instance,
            currentUserService.Object,
            TimeProvider.System,
            sideEffects,
            inventoryRestorer);
```

- [ ] **Step 3: Build and run both test files**

Run: `cd backend && dotnet build test/Anela.Heblo.Tests`
Expected: Build succeeded.

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ChangeTransportBoxState"`
Expected: All existing tests in both files PASS, unmodified in their assertions (the
integration test file requires the shared Postgres test container — see
`docs/testing/testing-strategy.md` / `PostgresSharedContainerFixture` for how it's normally
run in this repo; if the container isn't available in the current environment, at minimum
confirm the file builds and skip execution, noting this in the task's completion note).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs
git commit -m "test(logistics): update ChangeTransportBoxStateHandler constructor call sites"
```

---
