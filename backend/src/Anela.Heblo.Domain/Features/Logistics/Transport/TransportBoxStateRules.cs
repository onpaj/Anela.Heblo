using System.Linq.Expressions;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

/// <summary>
/// The single definition of transport-box code occupancy: whether a box in a given state
/// still holds its <see cref="TransportBox.Code"/> and therefore blocks that code from being
/// assigned to another box.
///
/// This rule must never be restated. Every consumer — today
/// <see cref="ITransportBoxRepository.IsBoxCodeActiveAsync"/>,
/// <see cref="ITransportBoxRepository.GetByCodeAsync"/> and OpenOrResumeBoxByCodeHandler —
/// calls into this type. Comparing against TransportBoxState.Closed/Stocked directly for
/// code-uniqueness purposes is a bug: that duplication is what allowed a Quarantine box's
/// code to be reassigned (issue #3887).
///
/// The rule is a deny-list on purpose. A newly added TransportBoxState occupies its code
/// until someone deliberately adds it to the releasing set, so the failure mode of
/// forgetting about this type is a false rejection, never a silent duplicate.
/// </summary>
public static class TransportBoxStateRules
{
    // Private: the array is an implementation detail, and `public static readonly T[]` is
    // only shallowly readonly — a public array would let any assembly overwrite an element
    // and silently reopen this bug. Kept as an array for consistency with the previous
    // implementation and with memory/gotchas/postgres-partial-index-active-states.md's
    // `private static readonly int[] ActiveStates` precedent.
    private static readonly TransportBoxState[] CodeReleasingStates =
    {
        TransportBoxState.Closed,
        TransportBoxState.Stocked,
    };

    /// <summary>In-memory check, for handlers that already hold a state value.</summary>
    public static bool OccupiesCode(TransportBoxState state) =>
        !CodeReleasingStates.Contains(state);

    /// <summary>
    /// EF-composable form of <see cref="OccupiesCode"/>. Translates to a negated set
    /// membership over the HasConversion&lt;string&gt; "State" column.
    /// </summary>
    public static readonly Expression<Func<TransportBox, bool>> OccupiesCodePredicate =
        b => !CodeReleasingStates.Contains(b.State);
}
