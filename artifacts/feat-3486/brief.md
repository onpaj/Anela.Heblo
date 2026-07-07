# [arch-review] Analytics: GetProductMarginSummaryRequest.TopProductCount accepted but silently ignored

## Module
Analytics

## Finding
`GetProductMarginSummaryRequest.TopProductCount` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs:9`) is declared as a public API parameter:

```csharp
public int TopProductCount { get; set; } = 15; // Configurable, default 15
```

`GetProductMarginSummaryHandler.Handle` never reads `request.TopProductCount`. The `GenerateTopProducts` method (lines 69–117) returns all groups without any limit. The frontend workaround confirms this: it passes `topProductCount: 0` explicitly, relying on the backend ignoring the value.

Because the TypeScript client is auto-generated from the OpenAPI spec, `topProductCount` appears as a documented query parameter with a default of 15, but it has no effect on the response.

## Why it matters
The public API lies: callers who pass `topProductCount=5` expecting a shorter, faster response receive the full dataset. This is a contract violation and will mislead anyone integrating against the spec. It also creates confusion whenever the parameter is "discovered" in the source — it looks like a feature but is dead code.

## Suggested fix
Pick one option:
1. **Remove the parameter** from `GetProductMarginSummaryRequest` and update `GetProductMarginSummaryHandler` (no behaviour change; cleans up the API surface).
2. **Implement the limit** — after sorting in `GenerateTopProducts`, truncate: `return sortedProducts.Take(request.TopProductCount > 0 ? request.TopProductCount : int.MaxValue).ToList();`.

Option 2 honours the documented intent. Update the frontend to pass a meaningful value instead of `0`.

---
_Filed by daily arch-review routine on 2026-07-05._
