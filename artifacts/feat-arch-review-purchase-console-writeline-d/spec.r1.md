# Specification: Remove debug `Console.WriteLine` from `PurchaseOrder.AddLine()`

## Summary
Remove the stray `Console.WriteLine` debug statement from the `PurchaseOrder` domain entity's `AddLine()` method. This eliminates an infrastructure (I/O) concern from the purest layer of Clean Architecture and stops unstructured noise from being written to container stdout on every line addition.

## Background
`PurchaseOrder.AddLine()` (`backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs:72`) contains a leftover debug statement:

```csharp
Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");
```

The accompanying `// Debug logging` comment confirms this was never intended to be permanent. The line violates several principles of the project's Clean Architecture:

- **Domain purity**: Domain entities must be free of I/O and infrastructure dependencies. Console I/O is infrastructure.
- **Performance**: Synchronous stdout I/O on a hot path (called from every `CreatePurchaseOrder` / `UpdatePurchaseOrder` request) adds unnecessary latency.
- **Observability hygiene**: Stdout is captured by the container runtime and lands in production logs as unstructured text with no log level, scope, or correlation fields.
- **Maintainability**: Debug `WriteLine` calls are exactly the kind of accidental telemetry that leaks into prod when not policed.

If add-line telemetry is ever genuinely needed, the correct place is the MediatR handler (`CreatePurchaseOrderHandler` / `UpdatePurchaseOrderHandler`), which already has `ILogger<T>` injected. That is out of scope here — the brief explicitly asks for deletion only.

## Functional Requirements

### FR-1: Delete the debug `Console.WriteLine` statement
Remove the `Console.WriteLine(...)` call (and its preceding `// Debug logging` comment, if present) from `PurchaseOrder.AddLine()`. The behavior of `AddLine()` must remain identical in every other respect: the line is still appended to `_lines`, all invariants and validations execute unchanged, and any domain events raised continue to be raised in the same order.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs` no longer contains `Console.WriteLine` anywhere in the file.
- The `// Debug logging` comment (if present and directly attached to the deleted line) is removed.
- No other lines in `AddLine()` are modified — purely a deletion.
- All existing unit tests for `PurchaseOrder` (e.g. `AddLine` tests) continue to pass without modification.
- `dotnet build` completes with no new warnings or errors attributable to this change.
- `dotnet format` reports the file as compliant.

### FR-2: Verify no other production `Console.*` calls exist in the Domain layer
While addressing this finding, perform a targeted scan of `backend/src/Anela.Heblo.Domain/` for any other `Console.Write`, `Console.WriteLine`, or `Console.Error.*` calls. If any are found, list them in `## Open Questions` (do **not** delete them as part of this change — they may have separate context). If none are found, note that explicitly in the PR description.

**Acceptance criteria:**
- A grep / ripgrep scan of `backend/src/Anela.Heblo.Domain/**/*.cs` for `Console\.` is performed.
- Findings (or the absence of findings) are documented in the PR description.
- No additional files are modified as part of this PR even if other `Console.*` calls are discovered.

## Non-Functional Requirements

### NFR-1: Performance
Removing synchronous stdout I/O from a per-line hot path is a micro-improvement; no measurable regression is expected and any change in request latency for `CreatePurchaseOrder` / `UpdatePurchaseOrder` should be a (very small) net positive. No benchmarking is required.

### NFR-2: Security & Data Sensitivity
The deleted statement currently writes `line.Id` and `Id` (purchase order id) to stdout. These are internal identifiers, not PII, but removing them from container logs is a marginal improvement to log hygiene. No secrets are involved.

### NFR-3: Observability
After this change, no telemetry will be emitted by `PurchaseOrder.AddLine()`. This is the intended state — domain entities should not emit telemetry. If observability gaps arise in the future, they are addressed at the application (handler) layer with `ILogger`, not in the domain.

### NFR-4: Architectural conformance
The change brings `PurchaseOrder` into compliance with the Clean Architecture rule that domain entities have zero infrastructure dependencies (see `docs/architecture/development_guidelines.md` and `docs/📘 Architecture Documentation – MVP Work.md`).

## Data Model
No data model changes. No entity properties, value objects, database schema, migrations, or DTOs are touched.

## API / Interface Design
No API changes. The public signature of `PurchaseOrder.AddLine()` is unchanged. No REST endpoints, MediatR requests/handlers, or generated OpenAPI clients are affected. The frontend is unaffected — no `npm run build` regeneration is required for this change, though the standard build still applies.

## Dependencies
- **None added or removed.** No NuGet packages, no infrastructure components, no external services.
- **Files touched:** exactly one — `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs`.
- **Tests affected:** any existing unit tests on `PurchaseOrder.AddLine()` should continue to pass without changes. No new tests are required (deleting a side-effect-only line does not warrant a regression test; the absence of the line cannot be meaningfully asserted from behavior).

## Out of Scope
- **Adding replacement logging.** No `ILogger.LogInformation(...)` or equivalent is introduced anywhere as a substitute. If telemetry is desired later, it is a separate change at the handler layer.
- **Refactoring `PurchaseOrder` more broadly.** Per project policy ("Surgical changes — touch only what the task requires"), no adjacent cleanup, renaming, comment editing, or structural refactor of the entity is performed.
- **Removing other `Console.*` calls** discovered elsewhere in the Domain layer — these are merely reported (FR-2), not deleted.
- **Backend-wide audit** for `Console.WriteLine` outside `backend/src/Anela.Heblo.Domain/`.
- **Adding lint / analyzer rules** (e.g., a Roslyn analyzer that forbids `Console.*` in the Domain assembly). Worth considering as a follow-up, but not part of this PR.
- **Frontend / E2E / migration work.** None required.

## Open Questions
None.

## Status: COMPLETE