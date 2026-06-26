# Specification: Decouple Logistics handlers from Manufacture inventory via consumer-owned contract

## Summary
Two Logistics MediatR handlers (`AddItemToBoxHandler`, `ChangeTransportBoxStateHandler`) currently inject `IManufacturedProductInventoryRepository` from the Manufacture domain and invoke Manufacture-owned domain methods (`ManufacturedProductInventoryItem.Consume`, `Restore`) directly, violating the cross-module communication rule in `docs/architecture/development_guidelines.md`. This spec describes introducing a Logistics-owned `IInventoryReservationService` contract, an adapter in the Manufacture module that implements it, and the migration of both handlers — modeled on the existing `ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter` precedent. Module-boundary enforcement is extended so the rule is verified in CI going forward.

## Background
`docs/architecture/development_guidelines.md` (sections "❌ Forbidden Practices" and "Cross-Module Communication Example: ILeafletKnowledgeSource") forbids:
- "Direct access to another module's entities"
- "Shared repositories across modules"

It also prescribes the consumer/provider inversion pattern: the **consumer** owns the interface in its `Contracts/` folder, the **provider** writes an adapter that lives in its own `Infrastructure/` and registers the binding in its `Module.cs`.

The current Logistics handlers violate this rule in three observable ways:

1. **`AddItemToBoxHandler`** (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs`)
   - Injects `IManufacturedProductInventoryRepository` (line 16).
   - Imports `Anela.Heblo.Domain.Features.Manufacture.Inventory`.
   - Calls `GetByIdAsync`, then `inventoryItem.Consume(...)` on the Manufacture domain entity (passing amount, user, timestamp, transport-box id, transport-box code, `allowNegativeStock`), then `UpdateAsync`.
   - Translates Manufacture's `InvalidOperationException` (insufficient stock) into `ErrorCodes.ManufacturedInventoryInsufficientStock` and a missing item into `ErrorCodes.ManufacturedInventoryItemNotFound`.

2. **`ChangeTransportBoxStateHandler`** (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`)
   - Injects `IManufacturedProductInventoryRepository` (line 17).
   - In `RestoreInventoryForItemsAsync` (lines 266–288), iterates items captured when transitioning from `Opened → New`, fetches each inventory item via `GetByIdAsync`, calls `inventoryItem.Restore(...)`, and persists via `UpdateAsync`. A missing inventory item is logged as a warning and skipped.

3. **Persistence coupling.** Both handlers call `await _inventoryRepository.UpdateAsync(inventoryItem, cancellationToken);` and rely on Manufacture's `SaveChangesAsync` semantics via the shared `ApplicationDbContext` to commit alongside Logistics changes from `_repository.SaveChangesAsync(cancellationToken)`. The new contract must preserve the same transactional behavior so that a successful "add item to box" still atomically debits inventory, and a transition from `Opened → New` still atomically restores it.

A precedent for the fix already exists in this codebase:
- Consumer contract: `Anela.Heblo.Application.Features.Leaflet.Contracts.ILeafletKnowledgeSource`
- Provider adapter: `Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.KnowledgeBaseLeafletSourceAdapter` (sealed, internal)
- DI registration: in `KnowledgeBaseModule.AddKnowledgeBaseModule`
- CI guard: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces that Leaflet types contain no references to KnowledgeBase-owned namespaces.

This spec applies the same pattern to Logistics ↔ Manufacture.

## Functional Requirements

### FR-1: Define `IInventoryReservationService` contract in Logistics
Create a new Logistics-owned interface at `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs`. The interface must expose **only** the operations the two handlers actually need today — no speculative methods.

The contract must support two operations:
- A **consume** operation that decrements inventory for a transport-box item, with optional override to allow negative stock; this operation can fail with an "insufficient stock" outcome and a "not found" outcome and must communicate both without throwing module-foreign exceptions.
- A **restore** operation that re-increments inventory when a transport box transitions `Opened → New`; this operation must tolerate a missing inventory record by skipping it (matching today's "log warning and continue" behavior) so that a partial recovery scenario still allows the box to revert.

The contract surface must be defined in Logistics terms and Logistics primitives only — no Manufacture domain types (e.g. `ManufacturedProductInventoryItem`) may appear in the signature, return type, or any exception explicitly thrown across the boundary.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs`.
- Namespace is `Anela.Heblo.Application.Features.Logistics.Contracts`.
- The interface declares `TryConsumeAsync` and `RestoreAsync` (or equivalent names; see FR-2 for exact signatures).
- No `using Anela.Heblo.Domain.Features.Manufacture.*` directive in the file.
- No type from `Anela.Heblo.Domain.Features.Manufacture.*` or `Anela.Heblo.Application.Features.Manufacture.*` appears anywhere in the public surface of the interface (signatures, return types, exception attributes, XML doc cref).

### FR-2: Operation signatures
The two operations must capture all the parameters the current direct calls use.

**Consume** must accept:
- `int inventoryId` — the Manufacture inventory item id (already exposed to Logistics today as `TransportBoxItem.SourceInventoryId`).
- `decimal amount` — quantity to consume.
- `string userName` — current user; resolved by the handler from `ICurrentUserService`.
- `DateTime timestamp` — UTC; resolved by the handler from `TimeProvider`.
- `int boxId`, `string? boxCode` — for the inventory audit log.
- `bool allowNegativeStock` — pass-through of `AddItemToBoxRequest.AllowNegativeStock`.
- `CancellationToken cancellationToken`.

Consume must return a structured result that distinguishes:
- success,
- not-found (inventory id does not exist),
- insufficient-stock (amount exceeds available and `allowNegativeStock` is false).

The implementation choice for "structured result" is open (sealed result type, discriminated union, `(Outcome, …)` tuple, or an enum + nullable payload). The constraint is that the handler must be able to map outcomes to existing `ErrorCodes.ManufacturedInventoryItemNotFound` and `ErrorCodes.ManufacturedInventoryInsufficientStock` without catching a Manufacture-owned exception type. See Open Questions on naming.

**Restore** must accept:
- `int inventoryId`,
- `decimal amount`,
- `string userName`,
- `DateTime timestamp`,
- `int boxId`, `string? boxCode`,
- `CancellationToken cancellationToken`.

Restore must:
- be idempotent with respect to a missing inventory record (return success/no-op when the id is unknown, equivalent to today's "log warning and skip"),
- not throw across the boundary on missing inventory,
- log internally inside the adapter (preserving today's `LogWarning` message content as closely as practical).

**Acceptance criteria:**
- Both methods return `Task<…>` and accept `CancellationToken` as the last parameter, per `csharp-coding-style.md`.
- The consume result type lives in `Anela.Heblo.Application.Features.Logistics.Contracts` (Logistics-owned) — not in Manufacture.
- The result type is expressive enough that no `try/catch (InvalidOperationException)` is needed in `AddItemToBoxHandler` to detect insufficient stock.
- Restore's contract documents the missing-id behavior in an XML doc comment.

### FR-3: Implement adapter in Manufacture module
Create `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs`. The adapter:
- Is `internal sealed` (mirroring `KnowledgeBaseLeafletSourceAdapter`).
- Implements `IInventoryReservationService`.
- Depends on `IManufacturedProductInventoryRepository` injected via constructor.
- Inside `TryConsumeAsync`:
  - Loads the item via `GetByIdAsync`.
  - If null, returns the "not found" outcome without throwing.
  - Calls `inventoryItem.Consume(amount, userName, timestamp, boxId, boxCode, allowNegativeStock)`.
  - If `Consume` throws `InvalidOperationException` (insufficient stock per current domain logic), catches it and returns the "insufficient stock" outcome.
  - Otherwise calls `UpdateAsync` and returns success.
- Inside `RestoreAsync`:
  - Loads the item via `GetByIdAsync`.
  - If null, logs a warning preserving the structure of today's message in `ChangeTransportBoxStateHandler.RestoreInventoryForItemsAsync` (item id, box id) and returns success/no-op.
  - Otherwise calls `inventoryItem.Restore(amount, userName, timestamp, boxId, boxCode)` and then `UpdateAsync`.

The adapter must NOT call `SaveChangesAsync` itself — persistence is committed by the handlers' existing `_repository.SaveChangesAsync(cancellationToken)` against the shared `ApplicationDbContext` (Phase 1 of ADR-001). This preserves today's atomic-commit semantics for both `AddItemToBox` and `ChangeTransportBoxState`. (See NFR-3.)

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs`.
- Class is `internal sealed`.
- All exception handling for Manufacture domain exceptions is confined to the adapter; nothing module-foreign leaks into the consumer.
- The adapter does not call `SaveChangesAsync` on any repository.
- Logging uses `ILogger<ManufactureInventoryReservationAdapter>`.

### FR-4: Register binding in `ManufactureModule`
Update `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` to register the binding:

```csharp
services.AddScoped<IInventoryReservationService, ManufactureInventoryReservationAdapter>();
```

The registration belongs to Manufacture per the guideline ("Module B's `{Module}.cs` registers `services.AddScoped<IConsumerContract, ProviderAdapter>();`"). The Logistics module must remain ignorant of the binding.

**Acceptance criteria:**
- `ManufactureModule.AddManufactureModule` registers the new binding.
- No registration of `IInventoryReservationService` exists in any Logistics module file.
- Application starts and `dotnet build` passes.

### FR-5: Migrate `AddItemToBoxHandler`
Replace the direct repository dependency with the new contract.

- Remove the `IManufacturedProductInventoryRepository _inventoryRepository` field and constructor parameter.
- Remove the `using Anela.Heblo.Domain.Features.Manufacture.Inventory;` directive.
- Inject `IInventoryReservationService` instead.
- Replace the `GetByIdAsync` → `Consume` → `UpdateAsync` block with a single call to `TryConsumeAsync` and translate its result to:
  - success → continue,
  - not-found → `ErrorCodes.ManufacturedInventoryItemNotFound` with the same `sourceInventoryId` param dictionary,
  - insufficient-stock → `ErrorCodes.ManufacturedInventoryInsufficientStock` with the same `sourceInventoryId` param dictionary.
- The `try/catch (InvalidOperationException)` around the consume call must be removed (the contract surfaces this as a structured result instead).
- The handler must continue to call `transportBox.AddItem(...)` and `_repository.SaveChangesAsync(cancellationToken)` exactly as today; the order (debit inventory, then add box item, then save) is preserved.

**Acceptance criteria:**
- Compilation: file does not import `Anela.Heblo.Domain.Features.Manufacture.Inventory`.
- Existing tests covering `AddItemToBoxHandler` (happy path, source-not-found, insufficient-stock, allow-negative-stock) pass without behavioral change.
- API responses for failure cases (`ErrorCode` and `Params`) are byte-identical to the pre-change responses for the same inputs.

### FR-6: Migrate `ChangeTransportBoxStateHandler`
Replace the direct repository dependency with the new contract.

- Remove the `IManufacturedProductInventoryRepository _inventoryRepository` field and constructor parameter.
- Remove the `using Anela.Heblo.Domain.Features.Manufacture.Inventory;` directive.
- Inject `IInventoryReservationService` instead.
- Reimplement `RestoreInventoryForItemsAsync` to loop over items and call `_inventoryReservationService.RestoreAsync(...)` per item. The missing-id case continues to be a no-op (logging moves into the adapter per FR-3).
- The capture of `itemsToRestore` before `transition.ChangeStateAsync(...)` (lines 124–126) and the call site of `RestoreInventoryForItemsAsync` (line 132) are preserved as-is.
- The order — capture items, run transition, restore, `UpdateAsync(box)`, `SaveChangesAsync` — must not change.

**Acceptance criteria:**
- Compilation: file does not import `Anela.Heblo.Domain.Features.Manufacture.Inventory`.
- Existing tests covering the `Opened → New` reversion path (including the missing-inventory branch) pass without behavioral change.
- For all non-`Opened → New` transitions, no call to `IInventoryReservationService` occurs (verified by mock assertions where tests exist).

### FR-7: Extend module-boundary CI guard to cover Logistics
The existing reflection-based test in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces the rule for Leaflet only. Extend it (or add a parallel test in the same file) to enforce that **Logistics types must not reference Manufacture-owned namespaces**, with the same allowlist semantics.

Forbidden namespace prefixes (for the Logistics → Manufacture rule):
- `Anela.Heblo.Domain.Features.Manufacture`
- `Anela.Heblo.Application.Features.Manufacture`
- `Anela.Heblo.Persistence.Manufacture`

Inspected namespace prefix: `Anela.Heblo.Application.Features.Logistics`.

The test must fail on the current code (proving the guard catches the violation) and pass once FR-5 and FR-6 are landed. Any unavoidable, pre-existing leak should be allowlisted with a justification comment, matching the existing allowlist style. The expectation is that there are **no** allowlist entries needed for this migration — the spec is to eliminate the references, not document them.

**Acceptance criteria:**
- A new `[Fact]` (or parameterized fact) in `ModuleBoundariesTests.cs` covers Logistics → Manufacture.
- Test fails when run against `main` (or any commit before the migration is complete).
- Test passes after FR-5 and FR-6 are applied.
- The new fact reuses the existing `EnumerateReferencedTypes` / `IsForbidden` / `ExpandGenerics` helpers (refactor them to accept parameters if needed; no copy-paste).

### FR-8: Update tests for both handlers
Existing unit tests against `AddItemToBoxHandler` and `ChangeTransportBoxStateHandler` are currently written against `IManufacturedProductInventoryRepository`. They must be migrated to mock `IInventoryReservationService` instead.

**Acceptance criteria:**
- All test files that mocked `IManufacturedProductInventoryRepository` for these two handlers now mock `IInventoryReservationService`.
- Test names continue to express behavior, not implementation (per `csharp-testing.md`).
- The full test suite (`dotnet test`) passes with no skipped tests in the migrated files.
- Coverage of the two handlers does not regress.

A separate adapter-level unit test (`ManufactureInventoryReservationAdapterTests`) covers the adapter's own behavior:
- consume happy path,
- consume returns not-found when repository returns null,
- consume returns insufficient-stock when domain throws `InvalidOperationException`,
- consume with `allowNegativeStock = true` succeeds even when amount > current,
- restore happy path,
- restore is a no-op (logs warning) when repository returns null.

## Non-Functional Requirements

### NFR-1: Behavioral parity
The migration must be a refactor, not a behavior change. All externally observable behavior of `AddItemToBox` and `ChangeTransportBoxState` (HTTP responses, error codes, log message structure for the warning path, inventory log entries written by `Consume`/`Restore`) is identical before and after. NFR-1 is satisfied by FR-5/FR-6/FR-8 acceptance criteria collectively.

### NFR-2: Module boundaries
After this change, the only edges between Logistics and Manufacture must be:
- the Logistics-owned `IInventoryReservationService` contract,
- consumed by Logistics handlers,
- implemented by Manufacture's adapter,
- bound in `ManufactureModule`.

Enforced in CI by FR-7.

### NFR-3: Transaction semantics
The shared `ApplicationDbContext` (ADR-001, Phase 1) is the unit of work. Both `_repository.SaveChangesAsync(...)` calls in the two handlers must continue to commit both the transport-box mutation and the inventory mutation atomically. The adapter must not introduce a second `SaveChangesAsync` or a new transaction scope; doing so could break atomicity if the inventory write succeeds and the box write fails (or vice versa).

### NFR-4: Logging
The adapter takes over logging for the restore-missing-id case from `ChangeTransportBoxStateHandler`. The warning's structured fields (`InventoryId`, `BoxId`) and severity (`LogWarning`) must be preserved; the message wording may be adjusted to read naturally from the adapter's perspective but must not omit the IDs.

### NFR-5: Code style
- Adapter is `internal sealed` (matches precedent).
- Files <800 lines (trivially).
- Naming follows `csharp-coding-style.md` (`IInventoryReservationService`, `TryConsumeAsync`, `RestoreAsync`).
- Nullable reference types enabled (project default).
- All async methods accept `CancellationToken` as the last parameter.

### NFR-6: No new dependencies
This refactor must not introduce new NuGet packages.

## Data Model
No schema changes. No new entities. The contract operates on primitive identifiers and value parameters; the Manufacture domain model (`ManufacturedProductInventoryItem`, `ManufacturedProductInventoryLog`, `InventoryChangeType`) is unchanged.

The contract result type for `TryConsumeAsync` is a Logistics-owned application-layer type (sealed record or enum + payload) with at minimum a success/not-found/insufficient-stock discriminator. It does not require persistence and never crosses the wire.

## API / Interface Design

### Public HTTP surface
No change. The MVC + MediatR pipeline, request DTOs, response DTOs, error codes, and status codes are all preserved. Existing OpenAPI clients (C# and TypeScript) regenerate identically.

### Internal module-boundary contract
A single new file owned by Logistics:

```
Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs
```

with two async methods (`TryConsumeAsync`, `RestoreAsync`) and a result type for consume outcomes. Manufacture-side adapter at:

```
Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs
```

DI binding in:

```
Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs
```

### Files modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs` (FR-5)
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` (FR-6)
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` (FR-4)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (FR-7)
- Existing test files for the two handlers (FR-8)

### Files created
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs` (FR-1, FR-2)
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs` (FR-3)
- Adapter unit tests file (FR-8)

## Dependencies
- Existing `IManufacturedProductInventoryRepository` and `ManufacturedProductInventoryItem` domain methods (`Consume`, `Restore`) are unchanged.
- Existing `ErrorCodes.ManufacturedInventoryItemNotFound` and `ErrorCodes.ManufacturedInventoryInsufficientStock` are reused as-is.
- Pattern reference: `ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter` / `KnowledgeBaseModule` registration.
- CI: existing `ModuleBoundariesTests.cs` reflection scaffolding (extended, not rewritten).

## Out of Scope
- Splitting `ApplicationDbContext` per module (ADR-001 Phase 2 — explicitly future work).
- Moving inventory persistence out of the shared DbContext.
- Introducing distributed transactions, outbox patterns, or eventual consistency between Logistics and Manufacture.
- Renaming or relocating `ManufacturedProductInventoryItem` or `IManufacturedProductInventoryRepository` (still owned by Manufacture; only the cross-module call path changes).
- Refactoring the `HandleReceived` flow in `ChangeTransportBoxStateHandler` (calls `IStockUpProcessingService` from Catalog — separate concern, not flagged in the brief).
- Generalizing the contract to other Logistics-side consumers of Manufacture (none exist today).
- Removing the existing Leaflet→KnowledgeBase allowlist entries.
- Updating Czech-language documentation (`docs/📘 Architecture Documentation – MVP Work.md`) — this spec only touches `docs/architecture/development_guidelines.md`-governed code paths; no doc changes are needed because the pattern being applied is already documented there.

## Open Questions
None.

## Status: COMPLETE