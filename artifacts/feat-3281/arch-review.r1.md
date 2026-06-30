# Architecture Review: Unit Tests for ResolveManualActionHandler

## Skip Design: true

## Architectural Fit Assessment

This spec adds unit tests for an existing handler — no new production code, no new modules, no architectural decisions beyond test structure. The handler is already written; the task is coverage, not design. The "Skip Design: true" flag reflects that this is a pure testing task with a fixed implementation target.

The existing pattern in `SubmitManufactureHandlerTests.cs` already establishes the testing conventions (xUnit + Moq + FluentAssertions). This new suite follows the same pattern exactly. No deviation from established conventions is warranted or permitted.

The one legitimate constraint to call out: `DateTime.UtcNow` is called directly in the handler with no `ITimeProvider` abstraction. Tests must tolerate clock drift via `BeCloseTo` rather than pinning exact values.

## Proposed Architecture

### Component Overview

Single new file:

```
backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs
```

One test class, twelve `[Fact]` methods, no shared mutable state between tests.

### Key Design Decisions

#### Decision 1: Test class structure — shared constructor vs per-test setup

**Options considered:**
- Shared mocks initialized in constructor (matches existing pattern)
- Per-test local setup via a factory method

**Chosen approach:** Shared mock fields initialized in the constructor, handler instantiated in the constructor.

**Rationale:** Direct match to the established pattern in `SubmitManufactureHandlerTests.cs`. Each `[Fact]` runs in its own class instance under xUnit.

#### Decision 2: Handling the missing ITimeProvider

**Options considered:**
- Assert exact `DateTime.UtcNow` value (brittle)
- Assert `BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5))`
- Skip timestamp assertions entirely

**Chosen approach:** `BeCloseTo` with a 5-second tolerance.

**Rationale:** Generous enough to survive slow CI runners. The real fix — injecting `ITimeProvider` — is deferred as separate tech debt.

#### Decision 3: ICurrentUserService mock configuration

**Chosen approach:** Configure `GetCurrentUser()` per-test for tests that care about user identity; leave default `Returns((CurrentUser?)null)` for tests that do not.

**Rationale:** Makes each test self-documenting. No hidden shared state.

#### Decision 4: Scope of FR-2 (ManualActionRequired)

**Chosen approach:** Assert `order.ManualActionRequired == false` in every test that reaches `UpdateOrderAsync`.

**Rationale:** FR-2 states the field is "always reset." Verifying it only once leaves a gap.

## Implementation Guidance

### Directory / Module Structure

```
backend/test/Anela.Heblo.Tests/Features/Manufacture/
    ResolveManualActionHandlerTests.cs      ← new file
```

### Interfaces and Contracts

| Interface | Mock type | Notes |
|---|---|---|
| `IManufactureOrderRepository` | `Mock<IManufactureOrderRepository>` | `GetOrderByIdAsync` returns a real `ManufactureOrder` instance with `Notes = new List<ManufactureOrderNote>()` |
| `ICurrentUserService` | `Mock<ICurrentUserService>` | `GetCurrentUser()` configured per-test |
| `ILogger<ResolveManualActionHandler>` | `Mock<ILogger<ResolveManualActionHandler>>` | Not asserted; required for construction |

### Data Flow

Arrange/Act/Assert pattern per test:
- Arrange: configure mocks, build request
- Act: `await _handler.Handle(request, CancellationToken.None)`
- Assert: response Success/ErrorCode, order field state, UpdateOrderAsync call count, timestamps via BeCloseTo

### Test case inventory

| Test method | FR | Key assertions |
|---|---|---|
| `Handle_WhenOrderNotFound_ReturnsResourceNotFoundError` | FR-1 | ErrorCode == ResourceNotFound; UpdateOrderAsync never called |
| `Handle_WhenOrderFound_ResetsManualActionRequired` | FR-2 | order.ManualActionRequired == false |
| `Handle_WhenSemiproductNumberProvided_UpdatesField` | FR-3a | order.ErpOrderNumberSemiproduct == request value |
| `Handle_WhenSemiproductNumberOmitted_DoesNotOverwriteField` | FR-3b | order.ErpOrderNumberSemiproduct unchanged |
| `Handle_WhenProductNumberProvided_UpdatesField` | FR-4a | order.ErpOrderNumberProduct == request value |
| `Handle_WhenProductNumberOmitted_DoesNotOverwriteField` | FR-4b | order.ErpOrderNumberProduct unchanged |
| `Handle_WhenDiscardDocumentProvided_UpdatesFieldAndTimestamp` | FR-5a | field set; date BeCloseTo(DateTime.UtcNow, 5s) |
| `Handle_WhenDiscardDocumentOmitted_DoesNotSetTimestamp` | FR-5b | ErpDiscardResidueDocumentNumberDate unchanged |
| `Handle_WhenNoteProvidedAndUserPresent_AddsNoteWithUserName` | FR-6a | Notes.Count == 1; text, author, timestamp correct |
| `Handle_WhenNoteProvidedAndUserNull_AddsNoteWithUnknownUser` | FR-6b | Notes[0].CreatedByUser == "Unknown User" |
| `Handle_WhenNoteOmitted_DoesNotAddNote` | FR-7 | Notes.Count == 0 |
| `Handle_WithAllFieldsProvided_ReturnsSuccessAndUpdatesAllFields` | FR-8 | All fields set; Notes.Count == 1; Success == true |

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `DateTime.UtcNow` hardcoded — approximate assertions needed | Low | Use `BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5))` |
| `ManufactureOrder.Notes` not initialized — NRE in handler | Medium | Explicitly set `Notes = new List<ManufactureOrderNote>()` in test fixture |
| `ITimeProvider` absence | Medium | Raise separate tech-debt issue; do not block this spec |

## Specification Amendments

- FR-3 and FR-4 each require two tests (provided + omitted branch)
- FR-2 should be asserted in every success-path test, not only once
- Twelve tests total (not eight) for proper branch coverage

## Prerequisites

- No new NuGet packages required
- `ManufactureOrder` constructible with public constructor in tests
- Test project already references `Anela.Heblo.Application`
