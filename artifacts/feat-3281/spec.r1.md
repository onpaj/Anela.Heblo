# Specification: Unit Tests for ResolveManualActionHandler

## Summary
Add a comprehensive unit test suite for `ResolveManualActionHandler`, which closes pending manual-review flags on manufacture orders. The handler has six distinct conditional paths with 18% line coverage, far below the 60% threshold, exposing silent data-corruption risk in ERP reconciliation workflows. This specification defines the test cases, mocking strategy, and assertions needed to bring coverage to acceptable levels.

## Background
The `ResolveManualActionHandler` is the sole mechanism by which operators close out pending manual reviews in the manufacture workflow. It conditionally updates up to three ERP fields, optionally appends a note, and always resets `ManualActionRequired` to `false`. Because the handler operates on ERP reconciliation data, incorrect or omitted field updates produce silent data corruption — no downstream alarm fires when a timestamp or document number is absent. The current 18% line coverage means four of six conditional branches are entirely unexercised, creating unacceptable regression risk for a critical workflow transition.

## Functional Requirements

### FR-1: Order-not-found path
When the repository returns `null` for the requested manufacture order ID, the handler must return a `ResourceNotFound` error result and must not invoke any save operation.

**Acceptance criteria:**
- Given a command with an order ID that does not exist in the repository mock, the handler returns a result whose error type is `ResourceNotFound`.
- `IManufactureOrderRepository.UpdateOrderAsync` is never called.
- No other fields are mutated.

---

### FR-2: ManualActionRequired is always reset
For every execution path that reaches a found order, `ManualActionRequired` must be set to `false` before the order is persisted, regardless of which optional fields are present in the command.

**Acceptance criteria:**
- After a successful handler execution, `order.ManualActionRequired == false`.
- This assertion is present in every happy-path test case.

---

### FR-3: ErpOrderNumberSemiproduct conditional update
When `ErpOrderNumberSemiproduct` is provided (non-null, non-empty) in the command, the handler overwrites the corresponding field on the order. When it is absent (null or empty), the field retains its pre-existing value.

**Acceptance criteria:**
- Provided: `order.ErpOrderNumberSemiproduct` equals the value from the command after execution.
- Omitted: `order.ErpOrderNumberSemiproduct` retains its original value unchanged.

---

### FR-4: ErpOrderNumberProduct conditional update
When `ErpOrderNumberProduct` is provided in the command, the handler overwrites the field. When absent, the field is unchanged.

**Acceptance criteria:**
- Provided: `order.ErpOrderNumberProduct` equals the command value after execution.
- Omitted: `order.ErpOrderNumberProduct` retains its original value.

---

### FR-5: ErpDiscardResidueDocumentNumber conditional update with timestamp
When `ErpDiscardResidueDocumentNumber` is provided, the handler overwrites the field **and** sets `ErpDiscardResidueDocumentNumberDate` to a UTC timestamp. When absent, neither the document number nor the date field is modified.

**Acceptance criteria:**
- Provided: `order.ErpDiscardResidueDocumentNumber` equals the command value; `order.ErpDiscardResidueDocumentNumberDate` is a non-null `DateTime` with `Kind == DateTimeKind.Utc`.
- Provided: the timestamp is within a reasonable tolerance (e.g., ±5 seconds) of `DateTime.UtcNow` at the time of test execution.
- Omitted: `order.ErpDiscardResidueDocumentNumber` and `order.ErpDiscardResidueDocumentNumberDate` are unchanged from their pre-execution values.

---

### FR-6: Note creation with current-user identity
When a `Note` string is provided in the command, the handler appends a `ManufactureOrderNote` to the order's `Notes` collection. The note must carry the provided text, a UTC timestamp, and the user's display name obtained from `ICurrentUserService`.

**Acceptance criteria:**
- Provided: `order.Notes` gains exactly one new entry after execution.
- The new note's text equals the command `Note` value.
- The new note's timestamp is UTC and within ±5 seconds of `DateTime.UtcNow`.
- When `ICurrentUserService` returns a non-null user with a name, that name is the note's author field.
- When `ICurrentUserService` returns `null` user, the note's author field is `"Unknown User"`.

---

### FR-7: Note omitted — Notes collection unchanged
When no `Note` is provided in the command, the handler must not append any entry to `order.Notes`.

**Acceptance criteria:**
- `order.Notes.Count` after execution equals `order.Notes.Count` before execution.

---

### FR-8: All-fields happy path
A single test exercises all optional fields simultaneously to confirm no branch interferes with another.

**Acceptance criteria:**
- All three ERP fields are updated to command values.
- `ErpDiscardResidueDocumentNumberDate` is set (UTC).
- A note is appended with correct text and author.
- `ManualActionRequired == false`.
- Repository `UpdateOrderAsync` is called exactly once.

## Non-Functional Requirements

### NFR-1: Performance
Unit tests must complete in under 500 ms total. All dependencies are mocked in-process.

### NFR-2: Test isolation
Each test case must construct its own independent mock instances and order objects. Shared state between tests is not permitted.

### NFR-3: Coverage target
After this test suite is merged, line coverage for `ResolveManualActionHandler.cs` must reach ≥ 60% (project threshold). Aim for ≥ 85%.

## Data Model

**ManufactureOrder** (existing entity — do not modify):
- `Id` — order identifier
- `ManualActionRequired` (`bool`) — reset to `false` by handler
- `ErpOrderNumberSemiproduct` (`string?`) — conditionally overwritten
- `ErpOrderNumberProduct` (`string?`) — conditionally overwritten
- `ErpDiscardResidueDocumentNumber` (`string?`) — conditionally overwritten
- `ErpDiscardResidueDocumentNumberDate` (`DateTime?`) — set only when document number is provided
- `Notes` (`List<ManufactureOrderNote>`) — note appended when Note command field is present

**ManufactureOrderNote** (existing entity — do not modify):
- `Text` (`string`) — note content
- `CreatedAt` (`DateTime`) — UTC timestamp
- `CreatedByUser` (`string`) — author display name; falls back to `"Unknown User"` when service returns null

## API / Interface Design

Unit tests only — no new API surface. Interfaces under test:
- `IManufactureOrderRepository.GetOrderByIdAsync(int, CancellationToken)` → `ManufactureOrder?`
- `IManufactureOrderRepository.UpdateOrderAsync(ManufactureOrder, CancellationToken)`
- `ICurrentUserService.GetCurrentUser()` → `CurrentUser?`

Test file location: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs`

Framework: xUnit + Moq + FluentAssertions (matching existing tests).

## Dependencies

- `ResolveManualActionHandler` source
- `IManufactureOrderRepository` interface
- `ICurrentUserService` interface
- `CurrentUser` record (`string? Id, string? Name, string? Email, bool IsAuthenticated`)
- `ManufactureOrder` and `ManufactureOrderNote` domain entities
- Existing test project — reuse framework and mocking library without adding new NuGet packages

## Out of Scope

- Integration tests against a real database
- Changes to production code
- Coverage improvements for any file other than `ResolveManualActionHandler.cs`
- Testing other handlers in the Manufacture module

## Open Questions

None.

## Status: COMPLETE
