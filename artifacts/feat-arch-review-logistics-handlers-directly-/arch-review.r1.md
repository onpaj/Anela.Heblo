# Architecture Review: Decouple Logistics handlers from Manufacture inventory via consumer-owned contract

## Skip Design: true

Pure backend refactor. No new HTTP surface, no DTO shape changes, no UI components — error codes, status codes, and JSON payloads are preserved (NFR-1). Frontend and OpenAPI clients regenerate identically.

## Architectural Fit Assessment

The proposal fits the codebase **precisely** — it is a literal application of the documented Leaflet/KnowledgeBase precedent to a second pair of modules. All preconditions are in place:

- `docs/architecture/development_guidelines.md` §"Cross-Module Communication Example: ILeafletKnowledgeSource" specifies the consumer-owns-contract / provider-owns-adapter / provider-registers-binding triad. The spec applies it verbatim.
- `Anela.Heblo.Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs` (13 lines) and `KnowledgeBaseLeafletSourceAdapter.cs` (sealed, internal, single repository dependency) are direct templates for the new types.
- `KnowledgeBaseModule.AddKnowledgeBaseModule` (line 38) registers `services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();` — same line shape needed in `ManufactureModule.AddManufactureModule`.
- `Anela.Heblo.Application/Features/Logistics/Contracts/` already exists (4 DTO files). Dropping a new interface there does not introduce a new folder convention.
- `ManufacturedProductInventoryItem.Consume` is the only place that throws `InvalidOperationException` on insufficient stock today (Domain/Features/Manufacture/Inventory/ManufacturedProductInventoryItem.cs:55-57); the adapter can safely translate it.
- `ErrorCodes.ManufacturedInventoryItemNotFound` (1215) and `ManufacturedInventoryInsufficientStock` (1216) already exist in `Application/Shared/ErrorCodes.cs`; no code-table churn.

Integration points (must remain stable):
1. **Shared `ApplicationDbContext` (ADR-001 Phase 1).** Inventory writes and box writes are tracked together; the handler's `_repository.SaveChangesAsync(cancellationToken)` commits both. The adapter must remain `SaveChanges`-free.
2. **`ITransportBoxRepository.SaveChangesAsync` is the unit-of-work boundary.** The adapter's `UpdateAsync` call only sets EF state (`Modified`) on the inventory entity; the commit is owned by the handler.
3. **`IManufacturedProductInventoryRepository`** stays Manufacture-owned and unchanged. Only the cross-module call path moves.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application/Features/Logistics                                   │
│                                                                              │
│  UseCases/AddItemToBox/AddItemToBoxHandler                                   │
│  UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler             │
│           │                                                                  │
│           │ ctor inject                                                      │
│           ▼                                                                  │
│  Contracts/IInventoryReservationService   ◀── Logistics-OWNED contract       │
│  Contracts/ConsumeInventoryResult         ◀── Logistics-owned result type    │
└───────────────────────────────│──────────────────────────────────────────────┘
                                │ implemented by
                                ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application/Features/Manufacture                                 │
│                                                                              │
│  Infrastructure/ManufactureInventoryReservationAdapter (internal sealed)     │
│           │                                                                  │
│           ▼                                                                  │
│  IManufacturedProductInventoryRepository (Manufacture-owned, unchanged)      │
│           │                                                                  │
│           ▼                                                                  │
│  ManufacturedProductInventoryItem.Consume / .Restore (domain methods)        │
│                                                                              │
│  ManufactureModule.AddManufactureModule:                                     │
│    services.AddScoped<IInventoryReservationService,                          │
│                       ManufactureInventoryReservationAdapter>();             │
└──────────────────────────────────────────────────────────────────────────────┘

Reflection-based CI guard: backend/test/.../Architecture/ModuleBoundariesTests.cs
  ├─ existing fact: Leaflet → KnowledgeBase
  └─ NEW fact (FR-7): Logistics → Manufacture (shares helpers, no allowlist entries)
```

### Key Design Decisions

#### Decision 1: Shape of the consume result type

**Options considered:**
- (A) `bool` return + out-parameter — opaque, hard to extend, doesn't carry the missing-vs-insufficient distinction.
- (B) `enum ConsumeOutcome { Success, NotFound, InsufficientStock }` only — adequate today; if a future error needs to carry a payload (e.g. "current available amount"), the API has to break.
- (C) Sealed `record` discriminated result (e.g. `ConsumeInventoryResult` with a `ConsumeOutcome` enum field and no payload today) — extensible without breaking the contract; still trivially mappable in the handler.
- (D) Exception-based — explicitly forbidden by FR-1 (no Manufacture exceptions across the boundary) and FR-2 acceptance criteria.

**Chosen approach:** **Option C**. Define:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public enum ConsumeInventoryOutcome
{
    Success,
    InventoryNotFound,
    InsufficientStock,
}

public sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome);
```

`TryConsumeAsync` returns `Task<ConsumeInventoryResult>`. The handler maps:
- `InventoryNotFound` → `ErrorCodes.ManufacturedInventoryItemNotFound`
- `InsufficientStock` → `ErrorCodes.ManufacturedInventoryInsufficientStock`
- `Success` → proceed.

**Rationale:** A `record` type leaves room for a future `decimal? AvailableAmount` field (e.g. for richer UI messaging) without rewriting every call site. It also reads more clearly than a raw enum at the call site (`result.Outcome switch { ... }`). Matches `csharp-coding-style.md`: "Prefer `record` for immutable value-like models." The spec leaves the shape open (its only constraint is "no Manufacture types in the surface, no try/catch in the handler"); we lock it down here so the implementer doesn't reopen the question.

#### Decision 2: How narrowly the adapter catches `InvalidOperationException`

**Options considered:**
- (A) Catch `InvalidOperationException` and unconditionally translate to `InsufficientStock` — what the spec implies and what `AddItemToBoxHandler` does today (line 74).
- (B) Inspect the exception message text — brittle.
- (C) Introduce a typed `InsufficientInventoryException : InvalidOperationException` in the Manufacture domain and have the adapter catch the typed one. Out of scope per the spec ("Renaming or relocating `ManufacturedProductInventoryItem` ... still owned by Manufacture; only the cross-module call path changes").

**Chosen approach:** **Option A**, with a single-line code comment on the catch block noting that `ManufacturedProductInventoryItem.Consume` is the only producer of `InvalidOperationException` in this call path and that broadening the surface (adding a second `throw new InvalidOperationException(...)` inside `Consume` for a different reason) would constitute a behavior change in the public adapter contract.

**Rationale:** Preserves NFR-1 (zero behavioral drift) and stays within scope. The comment documents the implicit coupling so a future change to `Consume` doesn't silently miscategorize a different error. Strengthening to Option C is a worthwhile follow-up but explicitly listed as Out of Scope.

#### Decision 3: Adapter location and visibility

**Chosen approach:** `internal sealed class ManufactureInventoryReservationAdapter` in `Anela.Heblo.Application/Features/Manufacture/Infrastructure/`, mirroring `KnowledgeBaseLeafletSourceAdapter` exactly (which is also `internal sealed`).

**Rationale:** `internal` prevents accidental cross-assembly references; `sealed` prevents subclass-based test doubles (consumers mock the interface). The `Infrastructure/` folder under the provider module is the documented home (guideline §"Concrete example", "The adapter lives in module B's `Infrastructure/`").

#### Decision 4: Test-guard refactor strategy (FR-7)

**Options considered:**
- (A) Copy-paste the existing `[Fact]` and swap namespace constants.
- (B) Refactor the existing test into a parameterized fact (`[Theory]` with `MemberData` per rule) where each rule is `(inspectedNamespace, forbiddenPrefixes[], allowlist)`.
- (C) Extract `EnumerateReferencedTypes`/`IsForbidden`/`ExpandGenerics` into a static helper class and keep two separate `[Fact]`s that call it.

**Chosen approach:** **Option B**. Make the test data-driven so adding a third module pair later is a one-line `MemberData` row, not another copy. Each row carries its own `Allowlist` (Leaflet's existing entries stay; Logistics gets an empty set).

**Rationale:** FR-7 explicitly says "no copy-paste" and "refactor helpers to accept parameters if needed." A `[Theory]` parameterization is the minimum-churn way to honor that. Keeps the existing helpers as `static` methods — they already take a `Type` and a forbidden-prefix list, so they generalize without changes.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/
├── Logistics/
│   ├── Contracts/
│   │   ├── IInventoryReservationService.cs        ← NEW
│   │   ├── ConsumeInventoryResult.cs              ← NEW (record + enum in one file is fine)
│   │   ├── TransportBoxDto.cs                     (existing)
│   │   ├── TransportBoxItemDto.cs                 (existing)
│   │   ├── TransportBoxStateLogDto.cs             (existing)
│   │   └── TransportBoxTransitionDto.cs           (existing)
│   └── UseCases/
│       ├── AddItemToBox/AddItemToBoxHandler.cs                       ← MODIFY
│       └── ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs ← MODIFY
└── Manufacture/
    ├── Infrastructure/                            ← NEW folder
    │   └── ManufactureInventoryReservationAdapter.cs ← NEW
    └── ManufactureModule.cs                       ← MODIFY (one AddScoped line)

backend/test/Anela.Heblo.Tests/
├── Architecture/
│   └── ModuleBoundariesTests.cs                   ← MODIFY (refactor to Theory)
└── Features/
    ├── Logistics/Transport/
    │   ├── AddItemToBoxHandlerTests.cs            ← MODIFY (swap mock target)
    │   └── ChangeTransportBoxStateHandlerTests.cs ← MODIFY (swap mock target)
    └── Manufacture/Infrastructure/                ← NEW folder
        └── ManufactureInventoryReservationAdapterTests.cs ← NEW
```

Note: `Manufacture/Infrastructure/` does not exist today in the Application project (Persistence has its own `Anela.Heblo.Persistence.Manufacture.Inventory` namespace, which is unrelated). Creating it is consistent with the guideline's prescribed layout and matches `KnowledgeBase/Infrastructure/`.

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

/// <summary>
/// Logistics-owned abstraction over inventory reservation for transport-box operations.
/// Implemented by the Manufacture module via an adapter. Operations must remain in the
/// caller's unit of work — implementations MUST NOT call SaveChangesAsync.
/// </summary>
public interface IInventoryReservationService
{
    Task<ConsumeInventoryResult> TryConsumeAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        bool allowNegativeStock,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores inventory amount. If the inventory id does not exist, the call is a
    /// no-op (logs a warning) and completes successfully — matching the original
    /// "log and skip" recovery semantics in ChangeTransportBoxStateHandler.
    /// </summary>
    Task RestoreAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken);
}

// Same folder — co-located result type
public enum ConsumeInventoryOutcome { Success, InventoryNotFound, InsufficientStock }

public sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome);
```

Adapter skeleton:

```csharp
// Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs
namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

internal sealed class ManufactureInventoryReservationAdapter : IInventoryReservationService
{
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;
    private readonly ILogger<ManufactureInventoryReservationAdapter> _logger;

    public ManufactureInventoryReservationAdapter(
        IManufacturedProductInventoryRepository inventoryRepository,
        ILogger<ManufactureInventoryReservationAdapter> logger)
    {
        _inventoryRepository = inventoryRepository;
        _logger = logger;
    }

    public async Task<ConsumeInventoryResult> TryConsumeAsync(
        int inventoryId, decimal amount, string userName, DateTime timestamp,
        int boxId, string? boxCode, bool allowNegativeStock,
        CancellationToken cancellationToken)
    {
        var item = await _inventoryRepository.GetByIdAsync(inventoryId, cancellationToken);
        if (item is null)
            return new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound);

        try
        {
            // ManufacturedProductInventoryItem.Consume is the sole producer of
            // InvalidOperationException on this code path (insufficient stock).
            item.Consume(amount, userName, timestamp, boxId, boxCode, allowNegativeStock);
        }
        catch (InvalidOperationException)
        {
            return new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock);
        }

        await _inventoryRepository.UpdateAsync(item, cancellationToken);
        return new ConsumeInventoryResult(ConsumeInventoryOutcome.Success);
    }

    public async Task RestoreAsync(
        int inventoryId, decimal amount, string userName, DateTime timestamp,
        int boxId, string? boxCode, CancellationToken cancellationToken)
    {
        var item = await _inventoryRepository.GetByIdAsync(inventoryId, cancellationToken);
        if (item is null)
        {
            _logger.LogWarning(
                "InventoryItem {InventoryId} not found during restore for transport box {BoxId} — skipping restore",
                inventoryId, boxId);
            return;
        }

        item.Restore(amount, userName, timestamp, boxId, boxCode);
        await _inventoryRepository.UpdateAsync(item, cancellationToken);
    }
}
```

DI binding in `ManufactureModule.cs` (one new line, grouped with the other repository registrations on lines 47-48):

```csharp
// Cross-module contract: Manufacture implements Logistics's IInventoryReservationService via adapter.
// DI registration owned by provider (Manufacture), not consumer (Logistics).
services.AddScoped<IInventoryReservationService, ManufactureInventoryReservationAdapter>();
```

### Data Flow

**`AddItemToBox` (success path):**
```
Controller → MediatR → AddItemToBoxHandler.Handle
  ├─ _repository.GetByIdWithDetailsAsync(boxId)         (Logistics)
  ├─ if SourceInventoryId != null:
  │    _inventoryReservationService.TryConsumeAsync(...)   ▶─┐
  │      └─ adapter loads ManufacturedProductInventoryItem  │
  │      └─ item.Consume(...) mutates entity (tracked)      │
  │      └─ inventoryRepository.UpdateAsync (EF state only) │
  │    ◀── returns Success                                ◀─┘
  ├─ transportBox.AddItem(...)
  └─ _repository.SaveChangesAsync(cancellationToken)    ← ONE commit covers both
                                                          inventory + box mutations
```

**`AddItemToBox` (insufficient-stock path):**
```
... TryConsumeAsync → adapter catches InvalidOperationException → returns InsufficientStock
... handler maps to ErrorCodes.ManufacturedInventoryInsufficientStock
... NO SaveChangesAsync invoked → tracked mutations from item.Consume are discarded
                                  when the DbContext is disposed at scope end.
```
This is critical for NFR-3: the adapter must not call `SaveChangesAsync`, otherwise the in-flight `item.Consume` mutation would commit on failure and leak negative inventory.

**`ChangeTransportBoxState` (Opened → New restore path):**
```
ChangeTransportBoxStateHandler.Handle
  ├─ box = _repository.GetByIdWithDetailsAsync(boxId)
  ├─ capture itemsToRestore = box.Items.Where(SourceInventoryId != null).ToList()
  ├─ transition.ChangeStateAsync(box, ...)               ← clears items
  ├─ foreach (item) _inventoryReservationService.RestoreAsync(...)   ▶─┐
  │    └─ adapter loads inventory; null → log warning + return         │
  │    └─ else item.Restore(...); UpdateAsync                          │
  │    ◀── returns                                                   ◀─┘
  ├─ _repository.UpdateAsync(box)
  └─ _repository.SaveChangesAsync(cancellationToken)    ← ONE commit
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Adapter accidentally calls `SaveChangesAsync` on the inventory repository, breaking atomicity with the box mutation in failure scenarios | HIGH | Explicit XML doc on `IInventoryReservationService` stating implementations must not commit; adapter unit tests assert `SaveChangesAsync` is never invoked on the mock (`_inventoryRepoMock.Verify(x => x.SaveChangesAsync(...), Times.Never)`); code-review checklist item. |
| `catch (InvalidOperationException)` in the adapter masks a future, unrelated `InvalidOperationException` thrown from inside `ManufacturedProductInventoryItem.Consume` (e.g. an added invariant check) and miscategorizes it as "insufficient stock" | MEDIUM | Inline code comment on the catch block naming the assumption; adapter unit test asserts the InsufficientStock branch is hit only when amount > current available; track promotion to typed `InsufficientInventoryException` as a follow-up. |
| Spec calls for an `internal sealed` adapter, but MediatR/test code in other assemblies may need to resolve it — same risk applied to `KnowledgeBaseLeafletSourceAdapter` and was a non-issue (DI resolves by interface). | LOW | None needed — keep `internal sealed`. Add `[InternalsVisibleTo("Anela.Heblo.Tests")]` only if a direct-instantiation test requires it; the existing pattern doesn't, since adapter tests construct via the public constructor (which is `internal` but accessible from a test project with `InternalsVisibleTo` — verify the Manufacture project already grants this to `Anela.Heblo.Tests`). |
| Restore no-op on missing inventory silently loses the user's reservation if the source row was deleted between consume and restore | LOW (pre-existing) | Out of scope per spec NFR-1; preserve today's "log warning and skip" behavior; document in adapter XML comment so it's not mistaken for a bug. |
| `ModuleBoundariesTests` `[Theory]` refactor breaks the existing Leaflet allowlist by changing how entries are matched | MEDIUM | Keep the allowlist as a `HashSet<string>` per row in `MemberData`; preserve the compiler-generated-type fallback logic (the `baseType.DeclaringType` check on lines 73-79); run the refactored test against `main` to confirm Leaflet still passes before adding the Logistics row. |
| Test must fail on `main` (FR-7 acceptance) — easy to forget to verify | LOW | Implement the Logistics `MemberData` row in a separate first commit, observe the red, then land FR-5/FR-6 to make it green. |
| `IManufacturedProductInventoryRepository.UpdateAsync` semantics — whether it just marks `Modified` or does any extra work — could affect adapter correctness if it (e.g.) calls `SaveChangesAsync` internally | MEDIUM | Read `Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs` (or its base) before implementing the adapter; if `UpdateAsync` commits internally, the design needs adjustment (or the adapter must not call it and rely solely on EF change tracking). This is a prerequisite verification step (see Prerequisites). |

## Specification Amendments

1. **FR-1/FR-2 — lock down the result-type shape.** The spec leaves the result type open. To prevent litigation during code review, amend FR-2 to: "The result type is a sealed `record` named `ConsumeInventoryResult` with a single `ConsumeInventoryOutcome` enum property (`Success | InventoryNotFound | InsufficientStock`). Both types live in `Anela.Heblo.Application.Features.Logistics.Contracts`." Rationale: design Decision 1 above.

2. **FR-1 — add an explicit contract clause that implementations must not commit.** Append to the FR-1 XML doc: "Implementations MUST NOT call `SaveChangesAsync` on any repository. The unit of work is owned by the caller." Rationale: NFR-3 is restated as a runtime test concern but is not surfaced in the contract itself; a comment on the interface makes the constraint discoverable at the API surface.

3. **FR-3 — verify `IManufacturedProductInventoryRepository.UpdateAsync` does not internally commit.** Add a small acceptance criterion: "Before implementing the adapter, confirm that `ManufacturedProductInventoryRepository.UpdateAsync` only marks the entity `Modified` and does not call `SaveChangesAsync`. If it does, the adapter must instead rely on EF change tracking only and `UpdateAsync` must be skipped." Rationale: today's handlers call `UpdateAsync` followed by `SaveChangesAsync`, so the current call-shape relies on `UpdateAsync` being commit-free; the adapter inherits this assumption and it should be checked once.

4. **FR-7 — name the chosen refactor shape.** The spec says "refactor helpers to accept parameters if needed; no copy-paste." Amend to: "Convert the existing `[Fact]` into a `[Theory]` driven by `MemberData`, with one row per module-boundary rule. Each row is `(InspectedNamespace, ForbiddenPrefixes[], Allowlist)`. Existing Leaflet row keeps its current allowlist; Logistics row has an empty allowlist." Rationale: removes ambiguity in implementation.

5. **FR-3 — narrow the catch comment.** Add: "Place an inline code comment on the `catch (InvalidOperationException)` block in `TryConsumeAsync` naming `ManufacturedProductInventoryItem.Consume` as the sole producer of this exception on the call path." Rationale: Decision 2 / MEDIUM risk above.

6. **Out-of-scope addendum.** No spec change needed, but call out for follow-up: typed `InsufficientInventoryException` in the Manufacture domain so the adapter does not have to catch a broad framework exception. Track as a separate ticket.

No other amendments. The spec is unusually well-grounded; the precedent it references is verified to be exactly the shape claimed.

## Prerequisites

1. **Confirm `ManufacturedProductInventoryRepository.UpdateAsync` is commit-free.** Read `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs` (and its base) to verify `UpdateAsync` only sets EF state. If it commits, the adapter design needs to drop the `UpdateAsync` call (EF tracking will pick up the mutation from `item.Consume`/`item.Restore` automatically).
2. **Confirm `InternalsVisibleTo` for the test project.** Verify `Anela.Heblo.Application.csproj` exposes internals to `Anela.Heblo.Tests` so the new `ManufactureInventoryReservationAdapterTests` can `new ManufactureInventoryReservationAdapter(...)`. Mirrors the assumption for `KnowledgeBaseLeafletSourceAdapter` tests (if any exist) — if not granted, either add `InternalsVisibleTo` or test the adapter via the DI container.
3. **No infrastructure, migration, configuration, secret, or NuGet changes required** (NFR-6).
4. **No OpenAPI regeneration required** (no contract changes on the HTTP surface).
5. **Order of landing** to make FR-7 acceptance trivially auditable:
   a. Land FR-1, FR-2, FR-3, FR-4 (new contract, adapter, DI binding) — code compiles; existing handlers still use the repository directly; nothing functionally changes.
   b. Land FR-7 refactor of `ModuleBoundariesTests` with the Logistics row — test goes red on this branch.
   c. Land FR-5, FR-6, FR-8 (migrate handlers, migrate handler tests, add adapter tests) — test goes green.