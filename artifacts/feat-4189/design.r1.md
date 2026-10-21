# Design: MarketingActionQueryCriteria placement

## Component Design
- `MarketingActionQueryCriteria` (Domain, unchanged location and members): repository query object for `IMarketingActionRepository.GetPagedAsync`. Gains an XML doc summary explaining that it is a persistence query object kept in Domain because the repository interface and its Persistence implementation cannot reference Application, and that new filters are added here plus in `GetMarketingActionsRequest` and the handler mapping.
- `GetMarketingActionsHandler`: unchanged mapping.
- Decision note `memory/decisions/marketing-query-criteria-in-domain.md` records options and the reference graph.

## Data Schemas
None. No API, DB or OpenAPI changes.
