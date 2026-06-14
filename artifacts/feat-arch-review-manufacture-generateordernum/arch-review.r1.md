# Architecture Review: Replace `DateTime.Now` with caller-supplied year in `ManufactureOrderRepository.GenerateOrderNumberAsync`

## Skip Design: true

Backend-only refactor. No UI, screens, layouts, or visual components are added or changed.

## Architectural Fit Assessment

The change aligns perfectly with the codebase's existing temporal-dependency convention: handlers inject `TimeProvider` (see `CreateManufactureOrderHandler.cs:24` and `DuplicateManufactureOrderHandler.cs:17`) and the Persistence layer is intended to remain free of wall-clock reads. The current implementation in `ManufactureOrderRepository.cs:150` is the lone deviation and is a textbook hidden-dependency leak from infrastructure.

Verified integration points:
- **Interface owner** — `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs:24` (Domain layer; no other ports referenced from outside).
- **Implementation** — `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs:148-170`.
- **Call sites** — confirmed exactly two: `CreateManufactureOrderHandler.cs:40` and `DuplicateManufactureOrderHandler.cs:38`. No other production caller exists in the solution.
- **Test impact** — four test files in `backend/test/Anela.Heblo.Tests/Features/Manufacture/` mock `GenerateOrderNumberAsync(It.IsAny<CancellationToken>())`. Each Setup/Verify call must be updated to add the new `year` argument.

The Purchase module already demonstrates the pattern of caller-supplied temporal data: `IPurchaseOrderNumberGenerator.GenerateOrderNumberAsync(DateTime orderDate, …)`. The Manufacture refactor brings this module into line, while keeping its narrower "year-only" surface as specified.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────┐
│ Application Layer (handlers — own TimeProvider)              │
│  ┌──────────────────────────────────┐                        │
│  │ CreateManufactureOrderHandler    │  var year =            │
│  │ DuplicateManufactureOrderHandler │  _timeProvider         │
│  └────────────────┬─────────────────┘   .GetUtcNow().Year    │
│                   │                                          │
│                   │ GenerateOrderNumberAsync(year, ct)       │
│                   ▼                                          │
├──────────────────────────────────────────────────────────────┤
│ Domain Layer (interface — pure contract)                     │
│  IManufactureOrderRepository                                 │
│  + GenerateOrderNumberAsync(int year, CancellationToken)     │
│                   │                                          │
├──────────────────────────────────────────────────────────────┤
│ Persistence Layer (NO temporal dependency)                   │
│  ManufactureOrderRepository                                  │
│  - Builds prefix from `year` argument                        │
│  - Runs sequence lookup (existing logic, unchanged)          │
└──────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Year flows as a primitive `int` parameter, not a `DateTimeOffset`/`DateOnly`
**Options considered:**
- (a) Pass `int year` (chosen).
- (b) Pass `DateTimeOffset` and let the repo extract `.Year`.
- (c) Inject `TimeProvider` into the repository.

**Chosen approach:** (a) `int year` parameter.

**Rationale:** The repository genuinely needs only the year; a `DateTimeOffset` invites future drift (someone uses `.Month`, reintroducing temporal coupling). Option (c) directly contradicts spec NFR-4 — moving the hidden dependency rather than removing it. Option (a) is the minimum sufficient contract: explicit, unambiguous, trivially testable, and exhausts what the prefix needs.

#### Decision 2: Mirror Purchase module's pattern, not its abstraction
**Options considered:**
- (a) Extract `IManufactureOrderNumberGenerator` to mirror `IPurchaseOrderNumberGenerator`.
- (b) Keep number generation inside `IManufactureOrderRepository` (chosen).

**Chosen approach:** (b) Keep the method on the repository.

**Rationale:** The Purchase generator is stateless (`Task.FromResult(...)` only). The Manufacture generator must query `_context.ManufactureOrders` to find the highest existing sequence — it inherently belongs with the data context. Extracting it would require either passing the DbContext into a separate service (leaks Persistence) or duplicating EF access (worse). Spec explicitly puts new abstractions out of scope; staying on the repository is correct.

#### Decision 3: Update tests in the same PR
**Chosen approach:** Update all four test files alongside the production change.

**Rationale:** Adding a required parameter to an interface mock signature is a compile break. The tests must change in lockstep — there is no incremental path. Tests should retain `It.IsAny<int>()` for year matching unless the test specifically asserts on year-boundary behavior (FR-3), in which case use the exact `int` value derived from the test's fixed `TimeProvider`.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits only:

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs` | Add `int year` to signature. |
| `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs` | Replace `DateTime.Now.Year` with parameter; delete the assignment line. |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` | Compute `var year = _timeProvider.GetUtcNow().Year;` once and pass it. Reuse the same instant for `CreatedDate` and `StateChangedAt` for audit consistency (see Risk R-2). |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs` | Same pattern. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs` | Update all `Setup`/`Verify` calls. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerSinglePhaseTests.cs` | Same. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` | Same. Add at least one new test per FR-3 acceptance criterion (year-boundary). |

`backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderHandlerTests.cs` is **not** affected (different interface).

### Interfaces and Contracts

```csharp
// Anela.Heblo.Domain/Features/Manufacture/IManufactureOrderRepository.cs
Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default);
```

```csharp
// Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs
public async Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default)
{
    var prefix = $"MO-{year}-";

    var lastOrderNumber = await _context.ManufactureOrders
        .Where(x => x.OrderNumber.StartsWith(prefix))
        .OrderByDescending(x => x.OrderNumber)
        .Select(x => x.OrderNumber)
        .FirstOrDefaultAsync(cancellationToken);

    var nextSequence = 1;
    if (lastOrderNumber != null)
    {
        var sequencePart = lastOrderNumber.Substring(prefix.Length);
        if (int.TryParse(sequencePart, out var lastSequence))
        {
            nextSequence = lastSequence + 1;
        }
    }

    return $"{prefix}{nextSequence:D3}";
}
```

```csharp
// Both handlers
var now = _timeProvider.GetUtcNow();
var orderNumber = await _repository.GenerateOrderNumberAsync(now.Year, cancellationToken);
// reuse `now` for CreatedDate / StateChangedAt to guarantee row-level audit consistency
```

### Data Flow

```
Handler.Handle()
  └── now = TimeProvider.GetUtcNow()        ◄── single source of time
  └── orderNumber = repo.GenerateOrderNumberAsync(now.Year, ct)
        └── prefix := "MO-{year}-"
        └── EF query: max OrderNumber for prefix → nextSequence
        └── return "MO-{year}-{nextSequence:D3}"
  └── order.CreatedDate = now.DateTime
  └── order.StateChangedAt = now.DateTime
  └── repo.AddOrderAsync(order, ct)
```

All temporal values on the same row originate from one `TimeProvider` read.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| **R-1: Hidden third caller exists outside this search.** | Low | Search confirms only two production callers, but compile errors from the breaking interface change will surface any missed call site. Treat compile errors as the safety net; list any new site in the PR description per spec FR-2. |
| **R-2: Handler reads `_timeProvider.GetUtcNow()` multiple times within one call.** Inspection of `CreateManufactureOrderHandler` shows 4 separate `GetUtcNow()` reads (lines 46, 52, 62, 63). If wall-clock ticks across calls, year and `CreatedDate` could still diverge at the exact stroke of midnight UTC. | Low | Cache `var now = _timeProvider.GetUtcNow();` at the top of `Handle()` and reuse it for **all** stamps in that handler. Same for `DuplicateManufactureOrderHandler`. This is in-scope per spec FR-3 acceptance ("same TimeProvider reading within a single handler invocation"). |
| **R-3: Year-boundary race on concurrent inserts.** Two requests at 23:59:59.999 UTC on Dec 31 could both compute `year=2026`, then one row commits with `CreatedDate=2027` if processing crosses midnight. | Low | Out of scope per spec; the fix targets static skew, not concurrency. Pre-existing race; not introduced by this change. |
| **R-4: Existing tests mock `It.IsAny<CancellationToken>()`; the new `int year` parameter shifts argument positions.** | Low | Mechanical update: change Setup to `Setup(x => x.GenerateOrderNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))`. Compile errors will flag any miss. |
| **R-5: Spec demands "no clock read" inside the repository, but a future maintainer might re-add one for "convenience" defaults.** | Medium | Add a single-line comment at the method head: `// year is supplied by the caller; do not introduce TimeProvider here (see ADR / spec).` This is the one comment that meets the "non-obvious why" bar in the project's coding standards. |

## Specification Amendments

The spec is implementable as written. One **mandatory clarification** that should be added to FR-3 acceptance criteria:

- **FR-3 (clarification):** Both handlers MUST cache the `TimeProvider.GetUtcNow()` result in a local variable at the top of `Handle()` and reuse it for the year argument **and** every `CreatedDate`/`StateChangedAt` assignment on the new row. The current handler code reads `_timeProvider.GetUtcNow()` multiple times; without caching, a millisecond-precision crossing of the year boundary between reads would silently reintroduce the very inconsistency this spec fixes. Add a unit test that uses a fake `TimeProvider` whose successive reads cross midnight, and asserts year + `CreatedDate` agree.

One **optional addition** (low-priority cleanup, not required to satisfy the brief):
- The Purchase module's `PurchaseOrderNumberGenerator.cs:15-16` reads `DateTime.Now.Hour`/`Minute` — same anti-pattern, different module. Out of scope here but worth filing as a follow-up brief.

## Prerequisites

None. All required infrastructure is already in place:
- `TimeProvider` is registered in DI and already injected into both handlers.
- `IManufactureOrderRepository` and its implementation exist.
- All test projects exist with the right mocking framework (Moq + FluentAssertions + xUnit).

No migrations, no config, no new packages, no new DI registrations.