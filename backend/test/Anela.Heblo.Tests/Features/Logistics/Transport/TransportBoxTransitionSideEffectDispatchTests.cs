using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
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
