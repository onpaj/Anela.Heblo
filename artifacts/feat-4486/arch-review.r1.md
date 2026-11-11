# Architecture Review
- Pure Application-layer refactor inside one vertical slice; no layering, contract, DTO or persistence change.
- `GetMaterialNamesByIdsAsync` already exists on the domain interface, the EF repository, and `MockPackingMaterialRepository`; precedent in GetConsumptionHistoryHandler.
- Behavioural note: repository returns only found ids, so missing ids fall to the existing "Unknown" fallback; duplicate ids are tolerated (repo tests cover it).
- Risk: low. No docs/processes change (no sync/calculation semantics change).
Verdict: approved.
