# Architecture Review: MarketingActionQueryCriteria placement

## Skip Design: true
Backend-only, no UI.

## Architectural Fit Assessment
The finding is correct that the class is not a domain invariant, but its proposed remedies break the project dependency graph:
- Domain -> Xcc only. Persistence -> Domain, Xcc. Application -> Domain, Persistence, Xcc.
- The repository contract (Domain interface, Persistence implementation) must name the criteria type, so the type must live in Domain or a project both reference without a cycle (only Xcc, which cannot see Domain's `MarketingActionType`).
- Established convention (`docs/architecture/development_guidelines.md`: generic repository in Xcc, implementation in Persistence; `PagedResult<T>` in Xcc) already accepts paging in lower layers. Repository query-object parameters in Domain are a common, accepted pattern (specification/query object).

## Proposed Architecture

### Component Overview
Unchanged: Controller -> MediatR `GetMarketingActionsRequest` -> `GetMarketingActionsHandler` -> `IMarketingActionRepository.GetPagedAsync(MarketingActionQueryCriteria)` -> `MarketingActionRepository`.

### Key Design Decisions

#### Decision 1: Where the criteria lives
**Options considered:** (a) move to Application (cycle, rejected); (b) pass Request to repository (Application type in Domain, rejected); (c) move to Xcc (blocked by `MarketingActionType` in Domain; moving the enum is out of proportion); (d) keep in Domain as a repository query object and document it.
**Chosen approach:** (d), with an XML doc comment on the class stating it is a repository query object and why it lives in Domain, plus a short decision note in `memory/decisions/`.
**Rationale:** Only option that preserves dependency direction with minimal blast radius. Mapping boilerplate is one handler block and preserves the API-contract/persistence decoupling.

#### Decision 2: Keep request/criteria split
Request is the generated-client contract; criteria is the persistence contract. Retain the mapping.

## Implementation Guidance

### Directory / Module Structure
No file moves. Edits: `Domain/Features/Marketing/MarketingActionQueryCriteria.cs` (doc comment only); new `memory/decisions/marketing-query-criteria-in-domain.md`.

### Interfaces and Contracts
No signature changes.

### Data Flow
Unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Reviewer expects the finding "fixed" by a move | Low | Decision note and PR description explain the cycle |
| Future criteria added in Domain accumulate paging | Low | Doc comment sets the convention |

## Specification Amendments
Resolve the spec's open question as: keep in Domain, document (FR-2 becomes "document, do not move"). FR-1 and FR-3 stand.

## Prerequisites
None.
