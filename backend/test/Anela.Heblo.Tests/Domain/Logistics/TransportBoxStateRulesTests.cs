using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Logistics;

public class TransportBoxStateRulesTests
{
    private static readonly IReadOnlyDictionary<TransportBoxState, bool> ExpectedOccupancy =
        new Dictionary<TransportBoxState, bool>
        {
            [TransportBoxState.New] = true,
            [TransportBoxState.Opened] = true,
            [TransportBoxState.InTransit] = true,
            [TransportBoxState.Received] = true,
            [TransportBoxState.InSwap] = true,
            [TransportBoxState.Stocked] = false,
            [TransportBoxState.Closed] = false,
            [TransportBoxState.Error] = true,
            [TransportBoxState.Reserve] = true,
            [TransportBoxState.Quarantine] = true,
        };

    [Fact]
    public void EveryTransportBoxState_IsClassifiedByOccupiesCode()
    {
        foreach (var state in Enum.GetValues<TransportBoxState>())
        {
            ExpectedOccupancy.Should().ContainKey(state,
                "TransportBoxState.{0} is new. Do not just add it to this map — decide whether it " +
                "releases the transport box code and classify it in " +
                "TransportBoxStateRules.CodeReleasingStates first. The deny-list default is that a " +
                "new state OCCUPIES its code (issue #3887).", state);

            TransportBoxStateRules.OccupiesCode(state).Should().Be(ExpectedOccupancy[state],
                "TransportBoxStateRules.CodeReleasingStates must classify {0} as {1}",
                state, ExpectedOccupancy[state] ? "code-occupying" : "code-releasing");
        }
    }

    [Fact]
    public void ReleasingSet_IsExactlyClosedAndStocked()
    {
        TransportBoxStateRules.OccupiesCode(TransportBoxState.Closed).Should().BeFalse();
        TransportBoxStateRules.OccupiesCode(TransportBoxState.Stocked).Should().BeFalse();

        foreach (var state in Enum.GetValues<TransportBoxState>())
        {
            if (state is TransportBoxState.Closed or TransportBoxState.Stocked)
            {
                continue;
            }

            TransportBoxStateRules.OccupiesCode(state).Should().BeTrue(
                "only Closed and Stocked release the code; {0} must occupy it", state);
        }
    }

    [Fact]
    public void OccupiesCodePredicate_AgreesWithOccupiesCode_ForEveryState()
    {
        var compiledPredicate = TransportBoxStateRules.OccupiesCodePredicate.Compile();

        foreach (var state in Enum.GetValues<TransportBoxState>())
        {
            var box = BoxInState(state);

            compiledPredicate(box).Should().Be(TransportBoxStateRules.OccupiesCode(state),
                "OccupiesCodePredicate must agree with OccupiesCode for state {0}", state);
        }
    }

    private static TransportBox BoxInState(TransportBoxState state)
    {
        var box = new TransportBox();
        typeof(TransportBox)
            .GetProperty(nameof(TransportBox.State))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(box, new object[] { state });
        return box;
    }
}
