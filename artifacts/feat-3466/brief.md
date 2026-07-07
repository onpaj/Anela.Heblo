## Module
Analytics

## Finding
`ProductMarginSegmentDto` (`backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs:28-34`) defines six computed property aliases marked as backward-compat:

```csharp
// Keep for backward compatibility
public string ProductCode => GroupKey;
public string ProductName => DisplayName;
public decimal MarginPerPiece => AverageMarginPerPiece;
public decimal SellingPriceWithoutVat => AverageSellingPriceWithoutVat;
public decimal MaterialCosts => AverageMaterialCosts;
public decimal LaborCosts => AverageLaborCosts;
```

None of these aliases are consumed anywhere:
- The frontend (`ProductMarginSummary.tsx:245-250`) uses the canonical `averageMarginPerPiece`, `averageSellingPriceWithoutVat`, `averageMaterialCosts`, `averageLaborCosts` names.
- No backend code references them on `ProductMarginSegmentDto` instances.

Because the TypeScript API client is auto-generated from the OpenAPI spec, these six properties appear verbatim in `frontend/src/api/generated/api-client.ts`, adding dead surface area to the public API contract.

## Why it matters
Dead alias properties in a DTO that feeds an auto-generated TypeScript client create confusion about the canonical names. They will be mistaken for live properties by future developers and must be kept in sync whenever the primary properties change.

## Suggested fix
Delete the six alias properties. No callers need updating (none exist). The generated TypeScript client will shrink accordingly on the next build.

---
_Filed by daily arch-review routine on 2026-07-03._
