# Architecture Review: Map folderKey to path for Marketing folder links

## Skip Design: true
No new or changed UI; data-mapping fix only.

## Architectural Fit Assessment
Pure frontend adapter fix at the API-to-view-model boundary in `MarketingCalendarPage.tsx`. Aligns with existing pattern of renaming fields there (description->detail, startDate->dateFrom).

## Proposed Architecture

### Component Overview
API DTO (folderKey, folderType) -> MarketingCalendarPage mapping -> local MarketingActionDto (path) -> MarketingActionModal form (path).

### Key Design Decisions
#### Decision 1: Where to map
**Options considered:** fix in modal (accept folderKey), fix in page mapping.
**Chosen approach:** page mapping, as the issue suggests; modal contract (`path`) stays unchanged.
**Rationale:** Surgical, keeps the modal's local type as the single contract.

## Implementation Guidance
### Directory / Module Structure
Edit `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` (two sites) and the test file `pages/__tests__/MarketingCalendarPage.test.tsx`. Consider a small local helper `mapFolderLinks` in the page used by both sites. `calendar/MobileAgendaView.tsx:72` has the same pass-through; check whether it feeds the grid only (display) and mention, do not change unless it reaches the modal.

### Interfaces and Contracts
Output element: `{ path: string; label?: string; folderType?: string }`.

### Data Flow
API response -> mapFolderLinks -> editingAction -> modal init -> submit (filter on path.trim()) -> MarketingFolderLinkRequest.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Generated client DTO may lack `label` | Low | Check api-client.ts; only map fields that exist |
| Existing test fixtures use folderKey | Low | Update assertions accordingly |

## Specification Amendments
None.

## Prerequisites
None.
