# Architecture Review: Convert `ConsumeInventoryResult` from record to class

## Skip Design: true

## Architectural Fit Assessment
This is a direct application of an existing, unambiguous project rule (CLAUDE.md: "DTOs are classes, never C# records"; `docs/architecture/development_guidelines.md` §Contracts and DTOs Rules reinforces that each module owns its `Contracts/` types). `ConsumeInventoryResult` is the return type of `IInventoryReservationService.TryConsumeAsync`, a Logistics-owned, module-boundary contract implemented by `ManufactureInventoryReservationAdapter` — exactly the cross-module adapter pattern described in `development_guidelines.md` §"Provider implements the contract via an adapter." As an Application-layer contract (not a Domain entity/value object), it falls outside the record exception, which is scoped to internal Domain types. There is no architectural question to resolve here: the type's shape, its home in `Features/Logistics/Contracts/`, its consumers, and the module boundary it crosses are all unaffected. This is a mechanical type-declaration change, not a design decision.

I verified the spec's call-site inventory directly against source:
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs:18` — confirmed `public sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome);`
- `ManufactureInventoryReservationAdapter.cs:42,57,61` — three positional constructions, confirmed.
- `AddItemToBoxHandlerTests.cs:175,222,260` — three positional constructions in `.ReturnsAsync(...)`, confirmed.
- `AddItemToBoxHandler.cs:68,70,77,84` — only reads `consumeResult.Outcome` via `switch`; confirmed unaffected (property access is identical on a class).
- `ManufactureInventoryReservationAdapterTests.cs:43,65,88,111` — only reads `result.Outcome.Should().Be(...)`; confirmed unaffected.
- `IInventoryReservationService.cs:20` — return type declaration `Task<ConsumeInventoryResult>`; confirmed unaffected by the record→class change.
- A repo-wide grep for `new ConsumeInventoryResult(` returns exactly these 6 hits (3 production, 3 test) and no others — the spec's inventory is exhaustive.

No other risk surface: the type is never returned from a controller, never touches NSwag/OpenAPI generation, and is never compared with `==`/`Equals` or deconstructed anywhere in the codebase (confirmed by the same grep sweep), so losing record value-equality and deconstruction is a no-op.

## Proposed Architecture

### Component Overview
No components move, are added, or are removed. The existing shape is preserved exactly:

```
Logistics module                         Manufacture module
┌─────────────────────────────┐          ┌──────────────────────────────────────┐
│ Contracts/                   │          │ Infrastructure/                      │
│  IInventoryReservationService│◄─────────│ ManufactureInventoryReservationAdapter│
│  ConsumeInventoryResult      │  implements  (constructs the result via         │
│   (record → class)           │          │   factory methods after this change) │
└──────────────┬───────────────┘          └──────────────────────────────────────┘
               │ consumed by (reads .Outcome only)
               ▼
UseCases/AddItemToBox/AddItemToBoxHandler
```

The only change is internal to the `ConsumeInventoryResult` box: its declaration form. Every arrow and every consumer above is untouched.

### Key Design Decisions

#### Decision 1: Class shape — factory methods vs. public settable properties
**Options considered:**
- (a) Plain class with a public settable `Outcome` property and a public constructor taking the enum.
- (b) Class with `init`-only `Outcome` and three static factory methods (`Success()`, `InventoryNotFound()`, `InsufficientStock()`), as the spec/finding propose.

**Chosen approach:** (b), exactly as specified in `spec.r1.md` FR-1.

**Rationale:** Factory methods preserve the one-call-site-per-outcome ergonomics the record's positional constructor gave (`new ConsumeInventoryResult(ConsumeInventoryOutcome.Success)` → `ConsumeInventoryResult.Success()`), read at the call site without needing to know the enum's exact name, and match the pattern already suggested by the finding. `init`-only prevents accidental mutation after construction. This is not a novel pattern for the codebase — it is a minimal, idiomatic C# class replacement for a single-property record, with no other viable alternative worth presenting as a trade-off.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Single file edit:
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs` — replace the `sealed record` declaration with the `sealed class` shown in spec FR-1. Update the XML doc comment on the type to drop "Sealed record" wording (e.g. "Sealed class with an outcome discriminator..."). Leave `ConsumeInventoryOutcome` untouched.

Two existing files get call-site edits only (no structural change):
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs` — lines 42, 57, 61: replace `new ConsumeInventoryResult(ConsumeInventoryOutcome.X)` with `ConsumeInventoryResult.X()`.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs` — lines 175, 222, 260: same substitution inside `.ReturnsAsync(...)`.

No edits to:
- `IInventoryReservationService.cs` (interface signature unchanged — return type name is identical).
- `AddItemToBoxHandler.cs` (only reads `.Outcome`).
- `ManufactureInventoryReservationAdapterTests.cs` (only reads `.Outcome`).

### Interfaces and Contracts
`IInventoryReservationService.TryConsumeAsync(...)` signature is unchanged — same parameter list, same `Task<ConsumeInventoryResult>` return type. The new class's public surface:

```csharp
public sealed class ConsumeInventoryResult
{
    public ConsumeInventoryOutcome Outcome { get; init; }

    public static ConsumeInventoryResult Success() => new() { Outcome = ConsumeInventoryOutcome.Success };
    public static ConsumeInventoryResult InventoryNotFound() => new() { Outcome = ConsumeInventoryOutcome.InventoryNotFound };
    public static ConsumeInventoryResult InsufficientStock() => new() { Outcome = ConsumeInventoryOutcome.InsufficientStock };
}
```

This is the full contract developers implement/consume against — nothing else changes.

### Data Flow
Unchanged. `AddItemToBoxHandler` calls `IInventoryReservationService.TryConsumeAsync` → `ManufactureInventoryReservationAdapter` runs the domain consume logic and returns one of the three factory-constructed results → the handler switches on `.Outcome` to decide between the two failure `AddItemToBoxResponse` shapes or proceeding to `transportBox.AddItem(...)`. The record→class swap is invisible at every point in this flow because only property access (never construction, equality, or deconstruction) crosses those boundaries outside the adapter and its tests.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A missed call site elsewhere in the repo still uses positional `new ConsumeInventoryResult(...)` and fails to compile | Low | Repo-wide grep for `new ConsumeInventoryResult(` performed during this review found exactly the 6 sites the spec lists (3 production, 3 test); `dotnet build` after the change will catch any survivor immediately since the record's positional constructor no longer exists. |
| Losing record value-equality (`==`, `Equals`) or deconstruction breaks a hidden comparison | Negligible | Grep confirms `ConsumeInventoryResult` is never compared with `==`/`Equals`/pattern-deconstructed anywhere in `backend/`; only `.Outcome` is ever read. |
| XML doc comment on the type still says "Sealed record" after the edit, causing minor doc drift | Low | Explicitly called out in FR-1; update the comment text as part of the same edit. |

## Specification Amendments
None. `spec.r1.md` is accurate and complete against the current source — the call-site inventory (FR-2/FR-3/FR-4) matches what is actually in the repository line-for-line, and no additional call sites exist. Proceed with the spec as written.

## Prerequisites
None. No migrations, no config, no infrastructure changes, no NSwag/OpenAPI regeneration — the type never crosses the controller/HTTP boundary. Implementation can start immediately.
