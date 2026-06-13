# Architecture Review: Consistent TimeProvider Usage in Manufacture Order Handlers

## Skip Design: true

This is a backend-only, surgical refactor. No new visual components, screens, or layout changes.

## Architectural Fit Assessment

The change aligns perfectly with the existing pattern. `TimeProvider` is registered once in DI as `TimeProvider.System` (singleton) in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:128` and injected through handler constructors across the Manufacture, Logistics, Catalog, and Dashboard slices. All three target handlers already accept `TimeProvider` and use `_timeProvider.GetUtcNow().DateTime` correctly for adjacent fields — so the proposed replacement is the explicit completion of an already-applied convention, not a new architectural direction.

Main integration points:

- **Handler constructors** — already inject `TimeProvider`; no DI changes needed.
- **Test suites** — `Mock<TimeProvider>` from Moq is the established project convention (see `CreateNewTransportBoxHandlerAuditFieldsTests.cs:24`, `DuplicateManufactureOrderHandlerTests.cs:24`). The spec's mention of `Microsoft.Extensions.TimeProvider.Testing` does NOT match the codebase — that NuGet package is not referenced anywhere. The hand-rolled `FakeTimeProvider` subclass in `GetPackingDashboardHandlerTests.cs:18-32` is a special case for time-zone-sensitive code (`GetLocalNow`) and does not apply here.

## Proposed Architecture

### Component Overview

```
ASP.NET Core DI container
  └── TimeProvider.System  (singleton, registered once)
        │
        ▼  (constructor injection — already in place)
  ┌─────────────────────────────────────────────────────┐
  │ CreateManufactureOrderHandler                       │
  │   _timeProvider.GetUtcNow().DateTime → CreatedDate  │ ← change L46
  │   _timeProvider.GetUtcNow().DateTime → StateChangedAt│ ← change L52
  ├─────────────────────────────────────────────────────┤
  │ DuplicateManufactureOrderHandler                    │
  │   _timeProvider.GetUtcNow().DateTime → CreatedDate  │ ← change L47
  │   _timeProvider.GetUtcNow().DateTime → StateChangedAt│ ← change L52
  ├─────────────────────────────────────────────────────┤
  │ UpdateManufactureOrderHandler                       │
  │   _timeProvider.GetUtcNow().DateTime → note.CreatedAt│ ← change L145
  └─────────────────────────────────────────────────────┘
```

No new components, no new abstractions, no surface-area changes.

### Key Design Decisions

#### Decision 1: Inline replacement vs. local variable extraction
**Options considered:**
- (A) Replace each `DateTime.UtcNow` with an inline `_timeProvider.GetUtcNow().DateTime` call.
- (B) Capture once at method start: `var now = _timeProvider.GetUtcNow().DateTime;` and reuse.
- (C) Add a project-wide extension/helper such as `_timeProvider.UtcNow()`.

**Chosen approach:** (A) Inline replacement per occurrence.

**Rationale:** Matches the surrounding style — `CreateManufactureOrderHandler.cs:62-63` and `DuplicateManufactureOrderHandler.cs:41,57,59` already call `_timeProvider.GetUtcNow().DateTime` inline rather than caching it. The spec is explicit that helper extraction is out of scope. Caching the value (B) would change observable behavior: today the handler reads the clock multiple times within a method; collapsing to a single read is a separate, non-trivial decision and is not part of the brief.

#### Decision 2: Test double — `Mock<TimeProvider>` vs. `FakeTimeProvider` package
**Options considered:**
- (A) Use `Mock<TimeProvider>` with `_timeProviderMock.Setup(x => x.GetUtcNow()).Returns(fixed)`.
- (B) Add `Microsoft.Extensions.TimeProvider.Testing` NuGet package and use the framework's `FakeTimeProvider`.
- (C) Hand-roll a `FakeTimeProvider : TimeProvider` subclass per test file.

**Chosen approach:** (A) `Mock<TimeProvider>`.

**Rationale:** This is the dominant convention in the test suite (used in 10+ files surveyed across Transport, Catalog, Dashboard, and Manufacture). `DuplicateManufactureOrderHandlerTests` already uses this exact pattern with `FixedNow = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero)`. Adding a new NuGet dependency for one test change is unjustified, and the spec's listing of `Microsoft.Extensions.TimeProvider.Testing` as a dependency is incorrect for this codebase — it is not present in any `.csproj`. The hand-rolled subclass (C) is reserved for time-zone-sensitive tests that need a non-virtual `GetLocalNow()` override.

#### Decision 3: How to assert the frozen timestamp
**Options considered:**
- (A) Compare to `FixedNow.UtcDateTime` directly.
- (B) Compare with tolerance (e.g., `BeCloseTo`).

**Chosen approach:** (A) Exact equality.

**Rationale:** With a mocked `TimeProvider`, the handler will read the exact `DateTimeOffset` returned by `Setup`. There is no clock drift to tolerate. Exact equality (`Should().Be(FixedNow.UtcDateTime)`) gives the strongest regression signal — any reintroduction of `DateTime.UtcNow` will produce a divergence on the next test run.

## Implementation Guidance

### Directory / Module Structure

No new files. All changes confined to:

```
backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/
  ├── CreateManufactureOrder/CreateManufactureOrderHandler.cs       (2 line edits)
  ├── DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs (2 line edits)
  └── UpdateManufactureOrder/UpdateManufactureOrderHandler.cs       (1 line edit)

backend/test/Anela.Heblo.Tests/Features/Manufacture/
  ├── CreateManufactureOrderHandlerTests.cs       (switch TimeProvider.System → Mock<TimeProvider>; add assertion test)
  ├── CreateManufactureOrderHandlerSinglePhaseTests.cs  (same — verify if it also constructs the handler)
  ├── DuplicateManufactureOrderHandlerTests.cs    (add new assertions — mock already in place)
  └── UpdateManufactureOrderHandlerTests.cs       (switch TimeProvider.System → Mock<TimeProvider>; add assertion test)
```

### Interfaces and Contracts

No interface or contract changes. The MediatR commands, responses, DTOs, repository interfaces, and DI registrations remain untouched. The five edits are pure value-source substitutions.

### Data Flow

For the create flow:
```
Request → Handler.Handle()
         → _timeProvider.GetUtcNow().DateTime   (was: DateTime.UtcNow)
         → ManufactureOrder { CreatedDate, StateChangedAt = <that value> }
         → _repository.AddOrderAsync(order)
         → Response
```

For the test path:
```
Mock<TimeProvider>.Setup(x => x.GetUtcNow()).Returns(FixedNow)
         → injected into Handler
         → Handler reads FixedNow.UtcDateTime
         → captured order.CreatedDate == FixedNow.UtcDateTime  ✓
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing tests in `CreateManufactureOrderHandlerTests` and `UpdateManufactureOrderHandlerTests` use `TimeProvider.System` and may have implicit assertions that compare timestamps to `DateTime.UtcNow`. Swapping in `Mock<TimeProvider>` could break them. | Low | Scan existing tests for `DateTime.UtcNow`, `BeCloseTo`, or `BeOnOrBefore` assertions on `CreatedDate`/`StateChangedAt`/`CreatedAt` before changing the constructor. Adjust each to compare against the mocked `FixedNow.UtcDateTime`. |
| Spec lists `Microsoft.Extensions.TimeProvider.Testing` as a dependency, but it is not in any `.csproj`. A developer following the spec verbatim might add the package needlessly. | Low | Use `Mock<TimeProvider>` instead — see Specification Amendments below. |
| Future `DateTime.UtcNow` reintroductions in the same handlers will silently re-break time-freezing. | Medium | Tests added under FR-4 must compare to the exact mocked timestamp so any reintroduction fails the assertion. Optionally add a follow-up ticket for a Roslyn analyzer (explicitly out of scope here per spec). |
| `_timeProvider.GetUtcNow()` called multiple times in one method body can return different values under a wall-clock provider; the change does not collapse these calls. | Low | Accepted per Decision 1 — preserves current behavior. The mocked provider returns a deterministic value, so tests are unaffected. |

## Specification Amendments

1. **Correct the test-double dependency.** Replace the spec's `Dependencies` reference to `Microsoft.Extensions.TimeProvider.Testing` with:
   > `Moq` — for `Mock<TimeProvider>`; already in use throughout the test suite. No new NuGet packages required.

2. **Update FR-4 acceptance criteria** to reflect that two of the three existing test classes (`CreateManufactureOrderHandlerTests`, `UpdateManufactureOrderHandlerTests`) currently inject `TimeProvider.System` and must be migrated to `Mock<TimeProvider>` before the frozen-timestamp assertions can be added. `DuplicateManufactureOrderHandlerTests` already injects a mocked `TimeProvider` and only needs the new assertions added.

3. **Add a clarifying note on `DateTimeOffset` → `DateTime` conversion.** The replacement value is `_timeProvider.GetUtcNow().DateTime`, which discards the offset. Since the property type is `DateTime` (not `DateTimeOffset`), the test should assert `Should().Be(FixedNow.UtcDateTime)` rather than `FixedNow.DateTime` to avoid accidental kind mismatch (`UtcDateTime` is guaranteed `DateTimeKind.Utc`).

4. **Verify `CreateManufactureOrderHandlerSinglePhaseTests.cs` is also addressed.** It is in the same module and likely constructs the handler with `TimeProvider.System`; if so, FR-4 applies to it implicitly. The spec should either name it explicitly or state that single-phase coverage piggy-backs on the multi-phase test changes.

## Prerequisites

None. All required infrastructure is already in place:

- TimeProvider.System registered as singleton DI (verified at `ServiceCollectionExtensions.cs:128`).
- TimeProvider already injected into the three target handlers.
- Moq referenced by the test project and used for `Mock<TimeProvider>` in adjacent suites.
- No migrations, no config changes, no infrastructure changes.

Implementation can begin immediately.