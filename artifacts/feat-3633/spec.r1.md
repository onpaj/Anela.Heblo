# Specification: Inject TimeProvider into three Manufacture handlers

## Summary
Three handlers in the Manufacture module call `DateTime.UtcNow` or `DateTime.Now` directly instead of using the module's injected `TimeProvider` abstraction. This is a mechanical refactor: inject `TimeProvider` into each handler's constructor and replace the static calls with `_timeProvider.GetUtcNow().DateTime`, matching the existing pattern used by `UpdateManufactureOrderStatusHandler` and other handlers in the module. No behavior, API surface, or data model changes beyond the exact wall-clock value used for one previously-local-time field.

## Background
An arch-review finding identified that `GetManufactureProtocolHandler`, `ResolveManualActionHandler`, and `GetSemiproductRecipePdfHandler` bypass the module's established `TimeProvider` abstraction. Every other time-stamping handler in the module (`UpdateManufactureOrderStatusHandler`, `ConfirmProductCompletionWorkflow`, all four `DashboardTiles`, `ConfirmSemiProductManufactureWorkflow`) already injects `TimeProvider` and calls `_timeProvider.GetUtcNow()`. `TimeProvider` is registered as a singleton in the DI container already (framework-provided `TimeProvider.System` by default), so no new registration is required.

Two problems result from the current code:
1. **Untestable timestamps** — handlers calling `DateTime.UtcNow`/`DateTime.Now` directly cannot be given a deterministic clock in unit tests, so existing tests (`ResolveManualActionHandlerTests.cs`, `GetManufactureProtocolHandlerTests.cs`) can only assert non-null, not exact values.
2. **Local-time bug** — `GetSemiproductRecipePdfHandler` uses `DateTime.Now` (local server time) instead of UTC, which is inconsistent with every other timestamp in the system and will produce incorrect `PrintedAt` values if the server ever runs outside UTC.

## Functional Requirements

### FR-1: Inject `TimeProvider` into `GetManufactureProtocolHandler`
Add a `TimeProvider` constructor parameter and private readonly field, following the exact pattern in `UpdateManufactureOrderStatusHandler` (`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`, lines 18, 25, 33). Replace the direct call at `GetManufactureProtocolHandler.cs:85`:
```csharp
GeneratedAt = DateTime.UtcNow,
```
with:
```csharp
GeneratedAt = _timeProvider.GetUtcNow().DateTime,
```

**Acceptance criteria:**
- `GetManufactureProtocolHandler` has a `private readonly TimeProvider _timeProvider;` field, set from a new constructor parameter.
- Line 85 no longer references `DateTime.UtcNow`; it uses `_timeProvider.GetUtcNow().DateTime`.
- No other line in the file is changed.
- All call sites constructing `GetManufactureProtocolHandler` (production DI and `GetManufactureProtocolHandlerTests.cs`) are updated to supply a `TimeProvider` instance.

### FR-2: Inject `TimeProvider` into `ResolveManualActionHandler`
Add a `TimeProvider` constructor parameter and private readonly field. Replace both direct calls in `ResolveManualActionHandler.cs`:
- Line 54: `order.ErpDiscardResidueDocumentNumberDate = DateTime.UtcNow;` → `order.ErpDiscardResidueDocumentNumberDate = _timeProvider.GetUtcNow().DateTime;`
- Line 66: `CreatedAt = DateTime.UtcNow,` → `CreatedAt = _timeProvider.GetUtcNow().DateTime,`

**Acceptance criteria:**
- `ResolveManualActionHandler` has a `private readonly TimeProvider _timeProvider;` field, set from a new constructor parameter.
- Neither `DateTime.UtcNow` call remains in the file; both are replaced with `_timeProvider.GetUtcNow().DateTime`.
- Both replaced calls continue to write to the same fields (`order.ErpDiscardResidueDocumentNumberDate`, `ManufactureOrderNote.CreatedAt`) with no change to surrounding logic.
- No other line in the file is changed.
- All call sites constructing `ResolveManualActionHandler` (production DI and `ResolveManualActionHandlerTests.cs`) are updated to supply a `TimeProvider` instance.

### FR-3: Inject `TimeProvider` into `GetSemiproductRecipePdfHandler` and fix UTC bug
Add a `TimeProvider` constructor parameter and private readonly field. Replace the direct call at `GetSemiproductRecipePdfHandler.cs:65`:
```csharp
PrintedAt = DateTime.Now,
```
with:
```csharp
PrintedAt = _timeProvider.GetUtcNow().DateTime,
```
This both fixes the local-time-vs-UTC inconsistency and aligns the handler with the module's `TimeProvider` pattern.

**Acceptance criteria:**
- `GetSemiproductRecipePdfHandler` has a `private readonly TimeProvider _timeProvider;` field, set from a new constructor parameter.
- Line 65 no longer references `DateTime.Now`; it uses `_timeProvider.GetUtcNow().DateTime` (UTC, not local time).
- No other line in the file is changed.
- All call sites constructing `GetSemiproductRecipePdfHandler` (production DI and `GetSemiproductRecipePdfHandlerTests.cs`) are updated to supply a `TimeProvider` instance.

### FR-4: Update existing unit tests to supply `TimeProvider`
The three affected handlers' constructors gain a new required parameter, which will break compilation of their existing test fixtures until updated.

**Acceptance criteria:**
- `GetManufactureProtocolHandlerTests.cs`, `ResolveManualActionHandlerTests.cs`, and `GetSemiproductRecipePdfHandlerTests.cs` are updated to pass a `TimeProvider` to the handler constructor (consistent with how `UpdateManufactureOrderStatusHandlerTests.cs` currently passes `TimeProvider.System`, per existing convention in this test suite — using a fake/fixed `TimeProvider` is acceptable if a test wants to assert an exact timestamp, but is not required by this change).
- All pre-existing tests in these three files continue to pass after the change (behavior is otherwise unchanged; timestamp assertions that only checked non-null/near-now remain valid).
- No new test cases are required by this change; adding deterministic-clock timestamp assertions is optional and left to the implementer's discretion.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. `TimeProvider.GetUtcNow()` on `TimeProvider.System` has equivalent cost to `DateTime.UtcNow`.

### NFR-2: Security
No change. No new data is read, stored, or exposed; no authentication/authorization surface is touched.

## Data Model
No changes. The same fields (`ManufactureProtocolData.GeneratedAt`, `ManufactureOrder.ErpDiscardResidueDocumentNumberDate`, `ManufactureOrderNote.CreatedAt`, `SemiproductRecipeData.PrintedAt`) are populated with the same semantic value (current UTC time), only sourced via `TimeProvider` instead of the static `DateTime` API. Note: `SemiproductRecipeData.PrintedAt` will now hold a UTC value instead of a local-time value — this is the intended bug fix (see FR-3), and any downstream rendering that assumed local time should be checked but is not expected to exist since this is presentational-only (printed timestamp on a recipe PDF).

## API / Interface Design
No public API, request/response contract, or route changes. This is an internal implementation change to three MediatR request handlers:
- `GetManufactureProtocolHandler : IRequestHandler<GetManufactureProtocolRequest, GetManufactureProtocolResponse>`
- `ResolveManualActionHandler : IRequestHandler<ResolveManualActionRequest, ResolveManualActionResponse>`
- `GetSemiproductRecipePdfHandler : IRequestHandler<GetSemiproductRecipePdfRequest, GetSemiproductRecipePdfResponse>`

Each gains one new constructor parameter: `TimeProvider timeProvider`.

## Dependencies
- `System.TimeProvider` (BCL, .NET 8) — already used elsewhere in the module; already registered in the DI container (no new registration needed, confirmed by brief and by `UpdateManufactureOrderStatusHandler`'s existing working injection).
- No new NuGet packages, no new external services.

## Out of Scope
- Any handler outside the three named in the brief (`GetManufactureProtocolHandler`, `ResolveManualActionHandler`, `GetSemiproductRecipePdfHandler`).
- Any changes to other `DateTime` usage patterns in the codebase outside the Manufacture module.
- Introducing a fake/mockable `TimeProvider` test helper or convention change for the test suite beyond what's needed to keep the three affected test files compiling and passing.
- Any new functionality, new fields, new endpoints, or behavioral change beyond the UTC-vs-local-time correction in `GetSemiproductRecipePdfHandler` that is an implicit side effect of using `TimeProvider`.

## Open Questions
None.

## Status: COMPLETE
