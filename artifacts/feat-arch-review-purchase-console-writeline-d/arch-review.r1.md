# Architecture Review: Remove debug `Console.WriteLine` from `PurchaseOrder.AddLine()`

## Skip Design: true

## Architectural Fit Assessment

This change restores compliance with the project's Clean Architecture / Vertical Slice rules. The Domain layer (`Anela.Heblo.Domain`) is the innermost layer and must have zero infrastructure dependencies — `Console.WriteLine` is an I/O side effect that violates that constraint. A grep confirms this is the **only** `Console.*` call in `backend/src/Anela.Heblo.Domain/`, so the fix is fully localized.

Integration surface: none. `PurchaseOrder.AddLine()` is called from `CreatePurchaseOrderHandler` / `UpdatePurchaseOrderHandler`. Those handlers already have `ILogger<T>` injected if telemetry is ever genuinely required, but adding such logging is explicitly out of scope.

## Proposed Architecture

### Component Overview

No component changes. The only edit is a deletion inside an existing domain method:

```
[MediatR Handler] --calls--> [PurchaseOrder.AddLine()]
                                  |
                                  +-- validates IsEditable
                                  +-- creates PurchaseOrderLine
                                  +-- appends to _lines
                                  +-- (DELETE) Console.WriteLine        <-- removed
                                  +-- sets UpdatedBy / UpdatedAt
```

### Key Design Decisions

#### Decision 1: Delete-only, no replacement logging
**Options considered:**
- (A) Delete the line outright.
- (B) Replace with `ILogger.LogDebug(...)` in the handler.
- (C) Introduce a domain event raised on every line add, observed in the handler for logging.

**Chosen approach:** (A) — straight deletion. The brief and spec mandate "Surgical changes."

**Rationale:** No observed need for per-line telemetry exists; (B) and (C) are speculative work (YAGNI). If a need later emerges, the handler layer is the correct seam — that decision belongs to a future, separately scoped change.

#### Decision 2: Do not introduce a Roslyn analyzer to forbid `Console.*` in the Domain assembly
**Options considered:**
- (A) Just delete the line.
- (B) Add a `BannedApiAnalyzers` rule to prevent recurrence.

**Chosen approach:** (A) for this PR; (B) noted as a separately-scoped follow-up.

**Rationale:** Spec explicitly lists analyzer rules as out of scope. The surgical-change policy dominates.

## Implementation Guidance

### Directory / Module Structure

One file only:
- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs`

Remove lines 71–72 (the `// Debug logging` comment and the `Console.WriteLine(...)` call). Leave the blank line above `UpdatedBy = CreatedBy;` consistent with surrounding methods (`RemoveLine`, `UpdateLine`).

### Interfaces and Contracts

None changed. `PurchaseOrder.AddLine(string materialId, string materialName, decimal quantity, decimal unitPrice, string? notes)` keeps its signature and observable behavior. No DTOs, MediatR requests/responses, or OpenAPI surfaces are touched; no TypeScript client regeneration is required.

### Data Flow

Unchanged. The only state mutations remain: append to `_lines`, set `UpdatedBy`, set `UpdatedAt`. Domain history entries are not emitted from `AddLine()` today and continue not to be. The deletion removes a stdout side effect, nothing more.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing tests or scripts depend on the stdout output | Low | `PurchaseOrderTests` assertions are behavioral (counts, totals, status); they do not capture stdout. Run `dotnet test` on the Tests project to confirm. |
| Operators rely on the message in production logs | Low | Message is unstructured, unprefixed, and was clearly a `// Debug logging` leftover — no log-based alert/dashboard could reliably parse it. No mitigation required. |
| Whitespace-only diff churn (formatter reflow) | Low | Run `dotnet format` on the file after the edit; confirm diff is limited to the two intended lines plus any adjacent blank-line normalization. |
| Other `Console.*` calls slip back into the Domain layer later | Medium (long-term) | Out-of-scope here; recorded as a follow-up (Roslyn `BannedApiAnalyzers` rule on the Domain assembly). |

## Specification Amendments

None. The spec is precise, the acceptance criteria are testable, and the scope boundaries are correctly drawn. No additions or corrections required.

One minor clarification worth recording in the PR description (already implied by spec FR-2): the `Console.*` scan confirmed only the single occurrence at `PurchaseOrder.cs:72`. No other Domain-layer `Console.*` calls exist, so FR-2's "no additional files modified" condition is trivially satisfied.

## Prerequisites

None.

- No migrations, no config keys, no infrastructure changes, no new packages.
- No frontend regeneration step (OpenAPI surface unchanged).
- Standard validation gate applies: `dotnet build` + `dotnet format` + `dotnet test` on `backend/test/Anela.Heblo.Tests/` (specifically the `Domain/Purchase/PurchaseOrderTests` fixture, which exercises `AddLine` across draft/in-transit/completed states).