## Module
Marketing

## Finding
`MarketingActionQueryCriteria` declares an `ActionType` filter field and the repository implements the filtering logic, but the field is never set by any handler — making the filter permanently dead code.

Relevant files:
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingActionQueryCriteria.cs:11` — `ActionType` field declared
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs:62–65` — filter applied when non-null
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/GetMarketingActionsRequest.cs` — no `ActionType` property
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs:24–35` — criteria built from request, `ActionType` never mapped

The `GetMarketingActionsHandler` maps every other criteria field from `GetMarketingActionsRequest` but skips `ActionType`. Since there is no corresponding request property, the criteria field is always `null` and the repository branch never executes.

## Why it matters
Dead infrastructure code misleads future developers into thinking action-type filtering is tested and working. If the omission is intentional, the dead branch in the repository should be removed to avoid confusion. If it is an oversight, users are missing a filter that the data model supports.

## Suggested fix
Pick one:
- **Add the filter**: Add `MarketingActionType? ActionType { get; set; }` to `GetMarketingActionsRequest`, map it in the handler, and expose it as a query parameter.
- **Remove the dead code**: Delete `ActionType` from `MarketingActionQueryCriteria` and the corresponding `if (criteria.ActionType.HasValue)` block in `MarketingActionRepository.GetPagedAsync`.

---
_Filed by daily arch-review routine on 2026-05-17._