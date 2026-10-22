# Architecture Review: Leaflet dark-mode variants

## Skip Design: true
Pure class-name additions using existing tokens; no new components, layout or visual decisions.

## Architectural Fit Assessment
Fully aligned with ADR-006 and the existing graphite token usage in `PifUploadDialog.tsx`, `ImportTab.tsx` and `LeafletGenerateTab.tsx`. No integration points are affected.

## Proposed Architecture

### Component Overview
Two existing components, `LeafletForm` and `LeafletResult`, in `frontend/src/features/leaflet-generator/`. Only `className` strings change.

### Key Design Decisions

#### Decision 1: Token set
**Options considered:** ad hoc `dark:bg-gray-*` classes vs. graphite tokens.
**Chosen approach:** graphite tokens exactly as listed in the brief.
**Rationale:** repo convention; verified present in neighbouring files.

## Implementation Guidance

### Directory / Module Structure
Edit `LeafletForm.tsx` and `LeafletResult.tsx` in place. No new files.

### Interfaces and Contracts
No change to props or exports. Existing tests in `__tests__/LeafletForm.test.tsx` and `LeafletResult.test.tsx` must still pass; add class assertions only if cheap.

### Data Flow
None.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Token name typo makes class silently inert | Low | Grep each token against tailwind config and existing usages |
| Radio inputs remain native-styled in dark mode | Low | Out of scope; label text color is the requirement |

## Specification Amendments
None.

## Prerequisites
None.
