## Module
Bank

## Finding
`frontend/src/components/customer/tabs/ImportTab.tsx` renders four filter controls — Transfer ID text input, Account text input, statement date range pickers, and an "errors only" checkbox — but none of these values are passed to the API call.

The `useBankStatementsList` hook is called with only pagination and sorting parameters:

```typescript
// ImportTab.tsx, lines 48–58
const { data, isLoading: loading, error, refetch } = useBankStatementsList({
  skip: (pageNumber - 1) * pageSize,
  take: pageSize,
  orderBy: sortBy,
  ascending: !sortDescending,
  // transferIdFilter, accountFilter, statementDateFrom, statementDateTo,
  // showOnlyErrors are never passed here
});
```

`handleApplyFilters` (line 69) sets the filter state variables but does not include them in the query parameters. The "Filtrovat" button triggers a `refetch()` that re-fetches with the same unfiltered query. From the user's perspective, clicking "Filtrovat" does nothing.

The `useBankStatements.ts` hook's `GetBankStatementListRequest` interface does not expose `transferId`, `account`, or `errorsOnly` parameters, and the backend API (`GET /api/bank-statements`) likewise does not support them. So the disconnect extends from UI all the way through to the backend.

## Why it matters
Users see operational filter controls that silently have no effect. This is a correctness bug, not just dead code — it creates a misleading UI. The filter state variables (`transferIdFilter`, `accountFilter`, `statementDateFrom`, `statementDateTo`, `showOnlyErrors`) add ~40 lines of stateful code with zero runtime value.

## Suggested fix
Two options depending on intent:
1. **Remove the dead filters** if they were never intended to work: delete the filter state, the filter UI section, and `handleApplyFilters`/`handleClearFilters`.
2. **Wire up the filters** if they are planned features: add `transferId`, `account`, `dateFrom`, `dateTo`, `errorsOnly` parameters to `GetBankStatementListRequest` (backend + frontend hook), extend the repository's `GetFilteredAsync` to handle them, and pass the filter state into the hook call.

---
_Filed by daily arch-review routine on 2026-05-29._