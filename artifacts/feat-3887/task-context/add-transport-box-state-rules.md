### task: add-transport-box-state-rules

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs:19`
- Create (test): `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs`

#### Goal

Introduce the single, domain-level definition of transport-box code occupancy (FR-1) and the drift guard that makes adding a new `TransportBoxState` a deliberate decision (FR-6). No behaviour changes yet — this task adds the type and its tests only; the three call sites are rewired in the two tasks that follow.

#### Context

- `TransportBoxState` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxState.cs`) has exactly ten members, in this declaration order: `New`, `Opened`, `InTransit`, `Received`, `InSwap`, `Stocked`, `Closed`, `Error`, `Reserve`, `Quarantine`.
- The canonical rule is a **deny-list**: only `Closed` and `Stocked` release a code; every other state — present or future — occupies it. This is the fail-safe direction, and its absence is what caused the bug.
- `TransportBox.cs` already carries `using System.Linq.Expressions;` and hosts three sibling static predicates (`TransportBox.cs:39-50`), so this is an established convention in this file's namespace, not a new pattern.
- **Amendment A1 is binding:** the backing array is `private`. `public static readonly T[]` is only shallowly readonly — any assembly in the solution could overwrite an element and silently reopen this bug. The public surface is exactly `OccupiesCode` + `OccupiesCodePredicate`, and both have a real consumer by the end of this feature.
- **Amendment A5 is binding:** the spec's stated reason for using an array ("so EF Core translates `Contains`") is **false** for EF Core 8 — `List<T>`, `HashSet<T>` and `IEnumerable<T>` all translate. Keep the array, but for the real reason: consistency with the current implementation and with the `private static readonly int[] ActiveStates` precedent in `memory/gotchas/postgres-partial-index-active-states.md`. Do not carry the false constraint into a comment.
- `Anela.Heblo.Domain.csproj` carries `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, so the test *could* reach an `internal` array. It must not — assert only through the public surface (Decision 3).
- `ITransportBoxRepository` currently has **no XML documentation at all**. The name `IsBoxCodeActiveAsync` is deliberately kept (renaming would churn the interface plus four test files for zero behavioural gain), but it now actively misleads: `Error` and `Quarantine` are not colloquially "active" yet do occupy the code. The doc therefore goes on the **interface** — what every caller sees — not only on the implementation (Decision 4).

#### Implementation steps

- [ ] **Step 1: Create `TransportBoxStateRules.cs`**

```csharp
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
```

Declare `CodeReleasingStates` textually **before** `OccupiesCodePredicate`. The expression tree captures the field by member access rather than by value so ordering is not strictly load-bearing, but relying on that is unnecessary subtlety.

- [ ] **Step 2: Add the XML doc to `ITransportBoxRepository.IsBoxCodeActiveAsync`**

In `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs`, replace line 19 (`Task<bool> IsBoxCodeActiveAsync(string boxCode);`) with:

```csharp
    /// <summary>
    /// True when any box currently occupies <paramref name="boxCode"/> — i.e. holds it in a
    /// state for which <see cref="TransportBoxStateRules.OccupiesCode"/> is true. Matching is
    /// case-insensitive. The name predates the rule; "active" here means "occupying the code",
    /// which includes Error and Quarantine.
    /// </summary>
    Task<bool> IsBoxCodeActiveAsync(string boxCode);
```

The signature is unchanged. Do not touch any other member of the interface.

- [ ] **Step 3: Create `TransportBoxStateRulesTests.cs`**

New file `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs`, namespace `Anela.Heblo.Tests.Domain.Logistics` (matches the sibling `TransportBoxUniquenessTests.cs` / `TransportBoxCodeCaseHandlingTests.cs` in that folder).

Three tests, all asserting **only** through the public surface — never against `CodeReleasingStates`, which is private by design:

1. **Drift guard (FR-6).** An exhaustive hard-coded expected map keyed by every current enum member, then iterate `Enum.GetValues<TransportBoxState>()` and look each value up in the map. An eleventh member must fail on a **missing key**, not be silently skipped. Sketch:

```csharp
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
```

2. **Releasing set is exactly `{Closed, Stocked}`.** Assert `OccupiesCode(Closed)` and `OccupiesCode(Stocked)` are `false` and that every other enum value is `true` — derived from `Enum.GetValues<TransportBoxState>()`, not from a hard-coded list.

3. **Predicate/function agreement (FR-1).** For every value of `Enum.GetValues<TransportBoxState>()`, `OccupiesCodePredicate.Compile()(box)` must equal `OccupiesCode(state)` for a `TransportBox` whose `State` is that value.

   `TransportBox.State` has a **private setter** and several enum values (notably `InSwap`) are unreachable through the aggregate's guarded transitions, so build the probe by forcing the property via reflection — `PropertyInfo.SetValue` alone throws for a private setter, so go through the non-public set method:

```csharp
private static TransportBox BoxInState(TransportBoxState state)
{
    var box = new TransportBox();
    typeof(TransportBox)
        .GetProperty(nameof(TransportBox.State))!
        .GetSetMethod(nonPublic: true)!
        .Invoke(box, new object[] { state });
    return box;
}
```

   Compile the predicate **once** into a static/local `Func<TransportBox, bool>` and reuse it across the loop; do not call `.Compile()` inside the loop body.

- [ ] **Step 4: Run the new tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxStateRulesTests"
```

Expected: all PASS.

- [ ] **Step 5: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

Expected: build succeeds with no new warnings; `dotnet format` makes no changes beyond whitespace in the new files.

#### Acceptance criteria

- `TransportBoxStateRules.OccupiesCode` returns `false` for `Closed` and `Stocked`, `true` for `New`, `Opened`, `InTransit`, `Received`, `InSwap`, `Error`, `Reserve`, `Quarantine`.
- `OccupiesCodePredicate.Compile()` agrees with `OccupiesCode` for every one of the ten enum values.
- `CodeReleasingStates` is `private` — `grep -n "CodeReleasingStates" backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` shows it declared `private static readonly` and referenced only inside that file.
- No test references `CodeReleasingStates` as a member; the drift guard asserts through `OccupiesCode` only.
- The type lives in `Anela.Heblo.Domain` and takes no dependency on Application or Persistence — no new `using` beyond `System.Linq.Expressions`, no new `ProjectReference` in `Anela.Heblo.Domain.csproj`.
- `ITransportBoxRepository.IsBoxCodeActiveAsync` carries the XML summary above; its signature is byte-identical to before.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxStateRulesTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox&Category!=Integration"
```

The second command is a regression sweep — every existing transport-box test must still pass, since this task changes no behaviour.

---

