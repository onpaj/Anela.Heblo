### task: add-dispatch-uniqueness-test

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxTransitionSideEffectDispatchTests.cs`

Guards against the risk flagged in arch-review.r1.md: two registered side effects both
claiming the same `(from, to)` pair, which would make dispatch order silently significant.

- [ ] **Step 1: Write the test**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class TransportBoxTransitionSideEffectDispatchTests
{
    private static readonly (TransportBoxState From, TransportBoxState To)[] KnownPairs =
    {
        (TransportBoxState.New, TransportBoxState.Opened),
        (TransportBoxState.Opened, TransportBoxState.Reserve),
        (TransportBoxState.Opened, TransportBoxState.Quarantine),
        (TransportBoxState.InTransit, TransportBoxState.Received),
        (TransportBoxState.Reserve, TransportBoxState.Received),
        (TransportBoxState.Quarantine, TransportBoxState.Received),
    };

    private static IReadOnlyList<ITransportBoxTransitionSideEffect> AllSideEffects() => new ITransportBoxTransitionSideEffect[]
    {
        new NewToOpenedSideEffect(Mock.Of<ITransportBoxRepository>(), Mock.Of<ICurrentUserService>(), Mock.Of<TimeProvider>()),
        new OpenToReserveSideEffect(),
        new OpenToQuarantineSideEffect(),
        new ReceivedSideEffect(Mock.Of<ILogisticsStockOperationService>(), NullLogger<ReceivedSideEffect>.Instance),
    };

    [Theory]
    [MemberData(nameof(KnownPairsData))]
    public void ExactlyOneSideEffectSupports_EachKnownTransitionPair(TransportBoxState from, TransportBoxState to)
    {
        var matches = AllSideEffects().Count(s => s.Supports(from, to));
        matches.Should().Be(1, $"exactly one side effect should handle ({from} -> {to})");
    }

    public static IEnumerable<object[]> KnownPairsData() => KnownPairs.Select(p => new object[] { p.From, p.To });
}
```

(Add `using Anela.Heblo.Application.Features.Logistics.Contracts;` and
`using Anela.Heblo.Domain.Features.Users;` if `ICurrentUserService` / other referenced types
require them per their actual namespaces.)

- [ ] **Step 2: Run to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransportBoxTransitionSideEffectDispatchTests"`
Expected: PASS — 6 known pairs, each matched by exactly one side effect.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxTransitionSideEffectDispatchTests.cs
git commit -m "test(logistics): guard against overlapping transition side-effect dispatch"
```

---
