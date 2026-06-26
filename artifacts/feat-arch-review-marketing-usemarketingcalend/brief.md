## Module
Marketing

## Finding

Every API call in `frontend/src/api/hooks/useMarketingCalendar.ts` uses `(client as any).method()`, which discards all TypeScript type information for the generated API client.

Examples from the file:
```typescript
// Line 54
return await (client as any).marketingCalendar_GetMarketingActions(
    params.pageNumber, params.pageSize, params.searchTerm, ...
);

// Line 75
return await (client as any).marketingCalendar_GetMarketingAction(id);

// Line 92
return await (client as any).marketingCalendar_GetCalendar(params.startDate, params.endDate);

// Line 106
return await (client as any).marketingCalendar_CreateMarketingAction(request);

// Line 133
return await (client as any).marketingCalendar_UpdateMarketingAction(id, { ...request, id });

// Line 158
return await (client as any).marketingCalendar_DeleteMarketingAction(id);

// Line 191
return await (client as any).marketingCalendar_ImportFromOutlook(payload);
```

The file even includes an explanatory comment acknowledging the fragility:
```typescript
// Line 51-53
// Positional args match the generated signature (last verified at commit 2f582c12):
// ...
// If the backend DTO changes, re-run `npm run build` to regenerate and verify argument positions.
```

The comment shows that correctness is manually verified at a point in time — TypeScript cannot catch regressions automatically.

## Why it matters

- **Type safety is lost**: the entire purpose of auto-generating a TypeScript client from the OpenAPI spec is to get compile-time checking. Casting to `any` defeats this completely.
- **Silent breakage on API change**: if a backend DTO field is renamed or the method signature changes, TypeScript won't flag the hook. The bug surfaces at runtime (or in E2E tests, which run nightly).
- **Positional argument risk**: `GetMarketingActions` takes 10 positional parameters. A future reordering of query parameters in the backend DTO silently reorders the arguments at the call site — TypeScript cannot warn about this with an `any` cast.

## Suggested fix

Use the generated typed method directly. The generated `ApiClient` class exports `marketingCalendar_GetMarketingActions` as a properly typed method. Remove the `as any` cast and let TypeScript resolve the call:

```typescript
// Before
return await (client as any).marketingCalendar_GetMarketingActions(
    params.pageNumber, params.pageSize, ...
);

// After
return await client.marketingCalendar_GetMarketingActions(
    params.pageNumber, params.pageSize, ...
);
```

If the current generated types don't compile cleanly without the cast, identify the specific mismatch (likely a `Date` vs `string` issue in the generated query parameter types) and fix the NSwag template or add a targeted type assertion only at that point, not a blanket `any` cast.

---
_Filed by daily arch-review routine on 2026-06-07._