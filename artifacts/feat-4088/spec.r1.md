# Specification: Convert `ConsumeInventoryResult` from a record to a class

## Summary
`ConsumeInventoryResult`, the Application-layer contract type returned by `IInventoryReservationService.TryConsumeAsync`, is currently declared as a `sealed record`. This violates the project's CLAUDE.md rule that DTOs/contracts must be classes, never records (the NSwag OpenAPI generator mishandles record parameter order). This change converts it to a `sealed class` with static factory methods, preserving identical call-site syntax and behavior at every existing usage.

## Background
An arch-review finding flagged `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs:18`. `ConsumeInventoryResult` lives in the Logistics module's `Contracts/` folder and is the return type of the module-boundary interface `IInventoryReservationService.TryConsumeAsync`. It is not currently serialized by NSwag (it never appears in a controller response DTO), but as an Application-layer contract type it falls squarely under the "DTOs are classes, never records" rule — the record exception in CLAUDE.md is intended only for internal Domain-layer types. Fixing it now prevents the inconsistency from being copied into future contract types and removes any risk if this type is ever later exposed through an API response.

This is a mechanical, behavior-preserving refactor confined to a single file's type declaration plus updating its call sites to match. No functional, external-behavior, or API changes are involved.

## Functional Requirements

### FR-1: Convert `ConsumeInventoryResult` to a sealed class
Replace the `sealed record` declaration in `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs` with a `sealed class` exposing an `Outcome` property and three static factory methods, one per `ConsumeInventoryOutcome` enum value:

```csharp
public sealed class ConsumeInventoryResult
{
    public ConsumeInventoryOutcome Outcome { get; init; }

    public static ConsumeInventoryResult Success() => new() { Outcome = ConsumeInventoryOutcome.Success };
    public static ConsumeInventoryResult InventoryNotFound() => new() { Outcome = ConsumeInventoryOutcome.InventoryNotFound };
    public static ConsumeInventoryResult InsufficientStock() => new() { Outcome = ConsumeInventoryOutcome.InsufficientStock };
}
```

The `ConsumeInventoryOutcome` enum in the same file is unchanged. The existing XML doc comments on the enum and the type are preserved (updated only to drop the now-inaccurate "Sealed record" wording, e.g. "Sealed class with an outcome discriminator...").

The private/`init`-only setter pattern must make construction impossible except through the three factory methods (mirrors the record's single-constructor-shape ergonomics: exactly one way to produce each outcome).

**Acceptance criteria:**
- `ConsumeInventoryResult` is declared as `public sealed class`, not `record`.
- `Outcome` is an `init`-only auto-property of type `ConsumeInventoryOutcome`.
- Three public static factory methods exist: `Success()`, `InventoryNotFound()`, `InsufficientStock()`, each returning a `ConsumeInventoryResult` with the matching `Outcome`.
- No public constructor is exposed (the implicit parameterless constructor from the record's positional syntax must not survive as a publicly usable no-arg `new ConsumeInventoryResult()` — since `init` properties still permit `new ConsumeInventoryResult { Outcome = ... }` object-initializer syntax, and the class has no explicit constructor limiting this, this is acceptable per FR-2 below; the goal is eliminating the record's positional-constructor call shape, not sealing off every alternate construction path).

### FR-2: Update the sole production call site to use the factory methods
`ManufactureInventoryReservationAdapter.TryConsumeAsync` (`backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs`) constructs `ConsumeInventoryResult` via the record's positional constructor in three places. Update each to the corresponding factory method:

| Line | Current | New |
|---|---|---|
| 42 | `return new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound);` | `return ConsumeInventoryResult.InventoryNotFound();` |
| 57 | `return new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock);` | `return ConsumeInventoryResult.InsufficientStock();` |
| 61 | `return new ConsumeInventoryResult(ConsumeInventoryOutcome.Success);` | `return ConsumeInventoryResult.Success();` |

**Acceptance criteria:**
- No `new ConsumeInventoryResult(...)` positional-constructor call remains anywhere in `backend/`.
- All three outcomes are still produced by the same logical branches (not-found item, `InvalidOperationException` from `item.Consume`, and the success path), unchanged.

### FR-3: Update test call sites that construct `ConsumeInventoryResult` directly
`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs` mocks `IInventoryReservationService.TryConsumeAsync` and constructs `ConsumeInventoryResult` via the record's positional constructor in three `.ReturnsAsync(...)` setups. Update each to the corresponding factory method:

| Line | Current | New |
|---|---|---|
| 175 | `.ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.Success));` | `.ReturnsAsync(ConsumeInventoryResult.Success());` |
| 222 | `.ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock));` | `.ReturnsAsync(ConsumeInventoryResult.InsufficientStock());` |
| 260 | `.ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound));` | `.ReturnsAsync(ConsumeInventoryResult.InventoryNotFound());` |

**Acceptance criteria:**
- No `new ConsumeInventoryResult(...)` positional-constructor call remains in any test file.
- All existing test assertions in `AddItemToBoxHandlerTests.cs` continue to pass unmodified — only the construction expression changes.

### FR-4: No other call sites require changes
The following usages read `.Outcome` or use `ConsumeInventoryOutcome` directly and are unaffected by the record→class conversion (property access syntax is identical on a class):
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs` (line 68 `switch (consumeResult.Outcome)` and the three `case` branches at lines 70, 77, 84) — no change needed.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapterTests.cs` (lines 43, 65, 88, 111 — `result.Outcome.Should().Be(...)`) — no change needed; these tests exercise the adapter and only read the returned result's `Outcome`, never construct `ConsumeInventoryResult` directly.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs` (line 20, `Task<ConsumeInventoryResult> TryConsumeAsync(...)`) — the return type declaration is unaffected by the record→class change and requires no edit.

**Acceptance criteria:**
- These files remain textually unchanged by this task (aside from any incidental formatting `dotnet format` might apply, which must not alter behavior).

## Non-Functional Requirements

### NFR-1: Behavior preservation
The change must be functionally a no-op: identical outcomes, identical branching, identical exception handling, for every existing code path. No new equality semantics are relied upon anywhere in the codebase — a grep of all usages (above) confirms `ConsumeInventoryResult` is never compared with `==`/`Equals` or deconstructed, so losing record value-equality and deconstruction has no observable effect.

### NFR-2: Security
Not applicable — this is an internal contract type change with no auth, data-sensitivity, or serialization surface impact.

## Data Model
No data model changes. `ConsumeInventoryResult` is an in-memory Application-layer contract, not a persisted or serialized entity:
- `ConsumeInventoryOutcome` — existing enum, unchanged: `Success`, `InventoryNotFound`, `InsufficientStock`.
- `ConsumeInventoryResult` — changes from `sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome)` to a `sealed class` with an `init`-only `Outcome` property and three static factory methods (`Success()`, `InventoryNotFound()`, `InsufficientStock()`).

## API / Interface Design
`IInventoryReservationService.TryConsumeAsync(...)` keeps its exact signature: `Task<ConsumeInventoryResult> TryConsumeAsync(int inventoryId, decimal amount, string userName, DateTime timestamp, int boxId, string? boxCode, bool allowNegativeStock, CancellationToken cancellationToken)`. Only the return type's internal declaration (record → class) and its construction call sites change. No controller, no NSwag-generated client, and no HTTP contract is affected — `ConsumeInventoryResult` is never returned from a controller action.

## Dependencies
None beyond the existing codebase. No new packages, no NSwag regeneration needed (this type is not part of the generated OpenAPI surface).

## Out of Scope
- Any other `record` types elsewhere in the codebase flagged by the same or future arch-review passes — this task is scoped to `ConsumeInventoryResult` only.
- Adding an optional "available amount" field to `ConsumeInventoryResult` (mentioned as a future extensibility note in the existing doc comment) — not requested and not part of this fix.
- Changing `ConsumeInventoryOutcome` from an enum to any other shape.
- Amending the CLAUDE.md record-exception wording itself (e.g., clarifying that the exception applies to Domain layer only) — this task fixes the one flagged violation, not the rule's documentation.

## Open Questions
None.

## Status: COMPLETE
