# Architecture Review: Align CreateMarketingActionHandler with UpdateMarketingActionHandler's bulk-assignment APIs

## Skip Design: true
Backend-only, same-method-body refactor inside an existing MediatR handler. No new/changed API contract, no new/changed UI component, no new visual or interaction design decisions. The designer phase should be skipped.

## Architectural Fit Assessment
This change fits an already-established pattern in this codebase rather than introducing a new one. `docs/superpowers/plans/2026-06-09-marketing-action-replace-collections.md` (merged prior work) added `MarketingAction.ReplaceProductAssociations` / `ReplaceFolderLinks` specifically so the Application layer would stop mutating EF-tracked navigation collections directly, and refactored `UpdateMarketingActionHandler` to delegate to them. `CreateMarketingActionHandler` was not touched by that plan and still uses the older per-item domain methods (`AssociateWithProduct`, `LinkToFolder`) in a hand-rolled loop. This issue closes that gap: it is a "finish the migration," not a new architectural decision. Both domain methods already exist, are already unit-tested at the domain level (`MarketingActionReplaceProductAssociationsTests.cs`, `MarketingActionReplaceFolderLinksTests.cs`), and are already exercised end-to-end through `UpdateMarketingActionHandlerTests.cs`. The only integration point is `CreateMarketingActionHandler.Handle`, lines 59–65.

## Proposed Architecture

### Component Overview
```
CreateMarketingActionRequest (MediatR request, unchanged)
        │
        ▼
CreateMarketingActionHandler.Handle   <-- ONLY file with behavioral changes
        │  constructs `action = new MarketingAction(...)`
        │  (NEW) action.ReplaceProductAssociations(request.AssociatedProducts, now)
        │  (NEW) action.ReplaceFolderLinks(request.FolderLinks?.Select(...), now)
        │  ... (Outlook sync / save / compensation — UNCHANGED) ...
        ▼
MarketingAction (domain aggregate, UNCHANGED — methods already exist)
        │  ReplaceProductAssociations / ReplaceFolderLinks
        ▼
IMarketingActionRepository (UNCHANGED)
```
No new components, services, interfaces, or files are introduced. The change is confined to the body of one method.

### Key Design Decisions

#### Decision 1: Delegate to the exact same two calls `UpdateMarketingActionHandler` already uses
**Options considered:**
1. Fix only the case-sensitive `.Distinct()` bug in the Create handler's existing loop, leaving the per-item-method structure intact.
2. Replace the loops with `ReplaceProductAssociations` / `ReplaceFolderLinks`, matching Update exactly (the issue's suggested fix).
3. Extract a shared private/static helper used by both handlers.

**Chosen approach:** Option 2 — call `action.ReplaceProductAssociations(request.AssociatedProducts, now)` and `action.ReplaceFolderLinks(request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)), now)` directly in `CreateMarketingActionHandler.Handle`, verbatim as already done in `UpdateMarketingActionHandler.Handle` lines 95–98.

**Rationale:** Option 1 only patches the reported symptom (case-sensitive dedup) and leaves the deeper inconsistency (two domain entry points for the same conceptual operation) in place — it does not satisfy the issue's actual ask. Option 3 (shared helper) is unnecessary indirection: the two call sites are two lines each, calling public domain methods that already encapsulate all the normalization/dedup logic; a wrapper would only rename `action.ReplaceX(...)` to `Shared.SetX(action, ...)` without adding value, and would introduce a new abstraction the issue never asked for. Option 2 is also the path of least architectural risk: it reuses code the reviewer/tests already trust (it has been in production via Update since the prior plan merged) rather than writing anything new.

#### Decision 2: Accept the folder-link dedup-key behavior change for Create, do not special-case it
**Options considered:**
1. Accept `ReplaceFolderLinks`'s composite-key `(FolderKey, FolderType)` dedup for Create, same as Update already has.
2. Pre-filter `request.FolderLinks` in the Create handler to dedupe by `FolderKey` alone first (preserving today's Create-only behavior), then call `ReplaceFolderLinks`.

**Chosen approach:** Option 1 — no pre-filtering; call `ReplaceFolderLinks` with the request's folder links unfiltered.

**Rationale:** The asymmetry between `LinkToFolder` (dedupe by `FolderKey` alone) and `ReplaceFolderLinks` (dedupe by composite key) is explicitly documented as intentional on the domain method itself ("*The asymmetry is intentional; new code should use this method when replacing the full set.*") and was a deliberate decision in the prior plan that added these methods. `UpdateMarketingActionHandler` already exhibits the composite-key behavior in production. Special-casing Create to preserve the old, narrower dedup would reintroduce exactly the inconsistency this issue exists to remove, for a case (same folder key linked under two different folder types) that has no indication of being a real, relied-upon Create-time behavior — no existing Create test exercises `FolderKey`-only dedup across folder types. Treat this as an intentional, spec-documented behavior change (see spec FR-2), not a regression to guard against.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Single-file change:
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — replace lines 59–65.

Test files to update (see Specification Amendments — both must be checked):
- `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs`

### Interfaces and Contracts
No interface or contract changes. `MarketingAction.ReplaceProductAssociations(IEnumerable<string>?, DateTime)` and `MarketingAction.ReplaceFolderLinks(IEnumerable<(string folderKey, MarketingFolderType folderType)>?, DateTime)` are pre-existing, already-public domain methods — this change is purely a caller-side substitution. `CreateMarketingActionRequest`, `CreateMarketingActionResponse`, and the `IRequestHandler<,>` MediatR contract are untouched.

### Data Flow
Unchanged end-to-end shape: `CreateMarketingActionRequest` → handler builds `action` → sets product associations and folder links on `action` → (conditionally) syncs to Outlook → `_repository.AddAsync` + `SaveChangesAsync` → response. Only the *mechanism* by which the two association collections are populated on `action` changes (bulk-replace call vs. per-item loop); the mechanism's position in the sequence (immediately after construction, before the Outlook sync block) is unchanged, matching current lines 59–65.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Folder-link dedup-key change (`FolderKey`-only → composite `(FolderKey, FolderType)`) silently changes Create's persisted state for any caller who (perhaps accidentally) links the same folder key under two folder types | Low | Documented explicitly in spec FR-2 as an intentional, in-scope behavior change; add/confirm a test asserting both links persist (mirroring `ReplaceFolderLinks_KeepsBothEntries_WhenSameKeyButDifferentType` domain test) in the Create handler test suite so the new behavior is locked in, not just implied |
| Two differently-namespaced `CreateMarketingActionHandlerTests.cs` files exist (`Application/Marketing/` and `Features/Marketing/`) with overlapping test names; a developer could update only one and leave the other asserting the old case-sensitive/`FolderKey`-only behavior, causing a false sense of coverage | Medium | Both files must be located and checked during implementation (Specification Amendments below); at minimum verify neither file contains an assertion that depends on the old case-sensitive-Distinct or FolderKey-only-dedup behavior for Create |
| `ArgumentException` propagation path changes (now thrown by `ReplaceProductAssociations`/`ReplaceFolderLinks` instead of `AssociateWithProduct`/`LinkToFolder`) — functionally equivalent (`ArgumentException`, same "cannot be empty" messages) but `ParamName` differs (`"productCodes"`/`"links"` vs. no param name captured today) | Low | No handler-level catch exists for `ArgumentException` today (it propagates as an unhandled exception either way), so no behavior-visible-to-caller change; no mitigation needed beyond noting it, in case any test asserts a specific `ParamName` |

## Specification Amendments
1. **Confirm and update both `CreateMarketingActionHandlerTests.cs` files.** The spec (Open Questions) already flags that two differently-namespaced test files exist with the same class name. The planner must schedule a task to inspect both `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` and `backend/test/Anela.Heblo.Tests/Features/Marketing/CreateMarketingActionHandlerTests.cs`, and update/add assertions in whichever file(s) actually compile against the current `CreateMarketingActionHandler` (one may be stale/orphaned — verify with the `.csproj`'s test discovery, e.g. `dotnet test --list-tests` filtered to `CreateMarketingActionHandlerTests`, before assuming both are live).
2. **Add an explicit case-insensitive-dedup regression test for products** (mirroring FR-1's acceptance criterion) and an explicit **same-key-different-type folder link test** (mirroring FR-2's acceptance criterion) to whichever Create handler test file(s) are confirmed live, so both intentional behavior changes are locked in by a test rather than only implied by the domain-level tests added under the prior `ReplaceFolderLinks`/`ReplaceProductAssociations` plan.
3. No other amendments — the spec's FRs, NFRs, and Out-of-Scope section are architecturally sound as written.

## Prerequisites
None. `ReplaceProductAssociations` and `ReplaceFolderLinks` already exist on `MarketingAction` and are already merged and in production use via `UpdateMarketingActionHandler`. No migrations, config, or infrastructure changes are required before implementation can start.
