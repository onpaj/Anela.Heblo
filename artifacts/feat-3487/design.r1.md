# Design: Remove dead `IncludeDetailedBreakdown` flag from GetMarginReport

## Component Design

No new components. Existing `GetMarginReport` use case (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/`) loses one dead property; all responsibilities and interfaces are otherwise unchanged.

- **`GetMarginReportRequest.cs`** — request DTO. Loses the `IncludeDetailedBreakdown` property. Remains an `IRequest<GetMarginReportResponse>` bound wholesale via `[FromQuery]`.
- **`GetMarginReportHandler.cs`** — unchanged. Never read the property; continues to unconditionally build `ProductSummaries` and `CategorySummaries`.
- **`GetMarginReportRequestValidator.cs`** — unchanged. Had no validation rule for the removed property.
- **`AnalyticsController.GetMarginReport`** — unchanged. Binds the whole DTO via `[FromQuery] GetMarginReportRequest request`; no per-property mapping to edit.
- **`frontend/src/api/generated/api-client.ts`** — regenerated (not hand-edited) via the documented NSwag workflow (`docs/development/api-client-generation.md`), so `analytics_GetMarginReport(...)` drops the `includeDetailedBreakdown` parameter and its query-string serialization.
- **`GetMarginReportRequestValidatorTests.cs:220`** — test-object initializer for the removed property is deleted so the suite still compiles.

## Data Schemas

`GetMarginReportRequest` after the change (query-bound DTO, `GET /api/analytics/margin-report`):

```csharp
public class GetMarginReportRequest : IRequest<GetMarginReportResponse>
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? ProductFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public int MaxProducts { get; set; } = 50;
}
```

`GetMarginReportResponse` is unchanged — still always returns `ProductSummaries` and `CategorySummaries` in full.

No database schema, event payload, or other endpoint contract changes. The only wire-format effect is the disappearance of the `includeDetailedBreakdown` query parameter from the OpenAPI spec and the generated TypeScript client.
