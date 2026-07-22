# Architecture Review: Remove unreachable `GetByNameAsync` from Supplier repositories

## Skip Design: true

## Architectural Fit Assessment
This is a pure dead-code deletion inside the existing Purchase module's repository layer — it removes code, adds nothing new, and touches no interface, DI registration, or call site. It fully aligns with the project's existing interface-segregated repository pattern (`ISupplierRepository` → `FlexiSupplierRepository` adapter + `MockSupplierRepository` test double), and requires no new integration points.

Verification performed independently of the spec:
- `grep -rn "GetByNameAsync"` across the full worktree returns exactly two hits: the definitions in `FlexiSupplierRepository.cs:49` and `MockSupplierRepository.cs:56`. No other file references it — confirms it is unreachable.
- Confirmed `ISupplierRepository` (`backend/src/Anela.Heblo.Domain/Features/Purchase/ISupplierRepository.cs`) declares only `SearchSuppliersAsync` and `GetByIdAsync`.
- Confirmed `FlexiAdapterServiceCollectionExtensions.cs` registers `FlexiSupplierRepository` only as `ISupplierRepository`, so the concrete type is never resolved directly — `GetByNameAsync` cannot be reached through DI.
- Confirmed the duplicate `using Anela.Heblo.Domain.Features.Purchase;` on `MockSupplierRepository.cs` lines 1–2.

Spec's Option A (removal) is correct; there is no evidence anywhere in the codebase of Option B's premise (a needed name-based lookup).

## Proposed Architecture
No architecture change. Component boundaries, the `ISupplierRepository` contract, and DI wiring are all unchanged before and after.

### Key Design Decisions

#### Decision 1: Remove vs. promote to interface
**Options considered:** (A) delete the unreachable method from both implementations; (B) add `GetByNameAsync` to `ISupplierRepository` and wire a real caller.
**Chosen approach:** (A) — delete.
**Rationale:** No caller exists anywhere in `backend/`, and the spec explicitly scopes this as dead-code removal, not new-feature work. Introducing an interface member with no consumer would just create a different flavor of dead code (an unused contract method), and would require inventing a use case not requested by the brief. If name-based supplier lookup becomes a real requirement later, it should be proposed and speced as its own feature with a concrete call site, not smuggled into a cleanup task.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit in place:
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs` — delete `GetByNameAsync` (lines 49–53).
- `backend/test/Anela.Heblo.Tests/Controllers/MockSupplierRepository.cs` — delete `GetByNameAsync` (lines 56–60) and collapse the duplicate `using Anela.Heblo.Domain.Features.Purchase;` (line 1 or 2) to a single line.

### Interfaces and Contracts
`ISupplierRepository` is not touched — both implementations continue to satisfy it fully via `SearchSuppliersAsync` and `GetByIdAsync`. No consumer-facing contract changes.

### Data Flow
Unaffected — no runtime path currently exercises `GetByNameAsync`, so no data flow changes as a result of removing it.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A yet-undiscovered caller exists outside the searched paths (e.g. reflection, dynamic dispatch) | Very Low | Repo-wide grep for `GetByNameAsync` returned only the two definition sites; the method is not virtual/reflection-targeted and the class is DI-resolved solely via the interface. Re-run `dotnet build` after removal — a missed direct-concrete-type usage would fail to compile. |
| Future need for name-based supplier lookup re-emerges, and this removal is seen as a regression | Low | Not a regression — the method was never reachable. If needed later, re-add via a proper interface change with a real call site (Option B), not by resurrecting this dead code. |

## Specification Amendments
None. The spec (FR-1 through FR-3, NFR-1, NFR-2) is accurate, minimal, and independently verified against the current code. No changes needed.

## Prerequisites
None. No migrations, config, or infrastructure changes required — implementation can start immediately. Standard validation applies: `dotnet build` + `dotnet format`, and the existing test suite must continue to pass (no test currently exercises `GetByNameAsync`, so none require updates).
