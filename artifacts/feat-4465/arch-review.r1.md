# Architecture Review: Rename MarketingFolderType enum values

## Skip Design: true
Labels and layout are unchanged; only identifiers change.

## Architectural Fit Assessment
Pure rename inside the Marketing vertical slice. Domain enum is consumed by the domain aggregate (`MarketingAction.LinkToFolder`, `ReplaceFolderLinks`), `MarketingActionFolderLink`, the application contracts/handlers, EF configuration, and the generated TS client. Verified via grep: no `HasConversion<string>` on the column, so persistence is int-backed; Migration snapshots contain only `int` columns, so no snapshot/migration change.

## Proposed Architecture

### Component Overview
Domain enum -> (name-serialized) Application DTO/request -> OpenAPI -> generated TS enum -> `MarketingActionModal` option list. DB path: enum -> int.

### Key Design Decisions

#### Decision 1: Rename in place, keep integers
**Options considered:** (a) rename with same ints; (b) add new members and obsolete old; (c) change wire format to int.
**Chosen approach:** (a).
**Rationale:** Smallest change, no migration, matches issue. Wire is name-based, so backend and frontend ship together in one image; no compatibility shim needed (b/c are over-engineering for a solo-developer single-image deploy).

#### Decision 2: Pin integer values with a test
**Rationale:** The safety of this rename rests entirely on ints being stable; a cheap unit test makes accidental reordering fail CI.

## Implementation Guidance

### Directory / Module Structure
No new files except one test (`backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingFolderTypeTests.cs`).

### Interfaces and Contracts
Files to edit: `MarketingFolderType.cs`; tests in `Domain/Marketing/{MarketingActionLinkToFolderTests,MarketingActionReplaceFolderLinksTests,MarketingActionRescheduleTests}.cs` and `Application/Marketing/{MarketingActionDtoTests,CreateMarketingActionHandlerTests,MoveMarketingActionHandlerTests,UpdateMarketingActionHandlerTests}.cs`; `frontend/src/components/marketing/detail/MarketingActionModal.tsx` and its test; `frontend/src/api/generated/api-client.ts` (regenerate, do not hand-edit beyond what generation yields); `docs/processes/feed-marketing-action-to-outlook.md`.

### Data Flow
Unchanged apart from enum names.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale generated client keeps old names, FE build breaks | Medium | Regenerate client in same PR; run `npm run build` |
| Cached browser bundle sends old names after deploy, request binding fails | Low | Single-image deploy; user reload resolves; accepted |
| Reordering ints later corrupts data | Low | Int-pinning test |
| Process-docs CI gate fails | Medium | Update doc + `check.py index` in same PR |

## Specification Amendments
None.

## Prerequisites
None (no migration).
