### task: inject-timeprovider

Add the `TimeProvider` dependency. TDD in C# means the "failing test" is a compile failure: the test class asks for a four-argument constructor that does not exist yet.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs:1-28`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs:10-22`

- [ ] **Step 1: Write the failing test — add the frozen clock to the test class**

In `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`, add the `using` (keep the existing alphabetical-ish grouping — insert after the `Microsoft.Extensions.Logging` line) and replace the field block plus constructor.

Add this `using` line after `using Microsoft.Extensions.Logging;`:

```csharp
using Microsoft.Extensions.Time.Testing;
```

Replace lines 14-28 (the field declarations and the constructor) with:

```csharp
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ILogger<TransportBoxCompletionService>> _loggerMock;
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock;
    private readonly Mock<ILogisticsStockOperationQueryService> _stockOperationQueryServiceMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly TransportBoxCompletionService _service;

    public TransportBoxCompletionServiceTests()
    {
        _loggerMock = new Mock<ILogger<TransportBoxCompletionService>>();
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _stockOperationQueryServiceMock = new Mock<ILogisticsStockOperationQueryService>();
        _timeProvider = new FakeTimeProvider(FrozenNow);
        _service = new TransportBoxCompletionService(
            _loggerMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockOperationQueryServiceMock.Object,
            _timeProvider);
    }
```

The provider is held in a **field**, not passed inline, because the clock-advance test later in this plan needs `Advance(...)` on the same instance the service holds.

- [ ] **Step 2: Run the build to verify it fails**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

Expected: FAIL with `error CS1729: 'TransportBoxCompletionService' does not contain a constructor that takes 4 arguments` pointing at `TransportBoxCompletionServiceTests.cs`.

- [ ] **Step 3: Write the minimal implementation — add the field and constructor parameter**

In `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`, replace lines 10-22 with:

```csharp
    private readonly ILogger<TransportBoxCompletionService> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly ILogisticsStockOperationQueryService _stockOperationQueryService;
    private readonly TimeProvider _timeProvider;

    public TransportBoxCompletionService(
        ILogger<TransportBoxCompletionService> logger,
        ITransportBoxRepository transportBoxRepository,
        ILogisticsStockOperationQueryService stockOperationQueryService,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _transportBoxRepository = transportBoxRepository;
        _stockOperationQueryService = stockOperationQueryService;
        _timeProvider = timeProvider;
    }
```

`timeProvider` is the **last** parameter, matching all five sibling handlers in the part. Plain assignment — do **not** add `ArgumentNullException.ThrowIfNull`; the existing constructor and the siblings use plain assignments. Do not add a `using System;` — `TimeProvider` resolves through the project's implicit usings, exactly as `DateTime` already does in this file.

- [ ] **Step 4: Run the build and the test suite to verify they pass**

Run: `dotnet build Anela.Heblo.sln`

Expected: `Build succeeded.` with 0 errors.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`

Expected: PASS — `Passed! - Failed: 0, Passed: 7`. All seven pre-existing tests still pass; the service still reads the wall clock, which nothing asserts yet.

- [ ] **Step 5: Verify DI still resolves the service graph**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationStartupTests"`

Expected: PASS, `Failed: 0`. This is the guard that `TimeProvider` is resolvable in the real host (registered as a singleton at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`). A failure here would surface as `InvalidOperationException: Unable to resolve service for type 'System.TimeProvider'`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "refactor(logistics): inject TimeProvider into TransportBoxCompletionService"
```

---

