## Module
Analytics

## Finding
`GetProductMarginSummaryRequest.TimeWindow` is a free-form `string` with no FluentValidation validator registered:

```csharp
// GetProductMarginSummaryRequest.cs
public string TimeWindow { get; set; } = "current-year";
// current-year, current-and-previous-year, last-6-months, last-12-months, last-24-months
```

`TimeWindowParser.ParseTimeWindow` (line 21-29, `Services/TimeWindowParser.cs`) throws `ArgumentException` for any value not in the allowed set:

```csharp
_ => throw new ArgumentException($"Unknown time window value: '{timeWindow}'", nameof(timeWindow))
```

`GetProductMarginSummaryHandler.Handle` has no try/catch and no registered `IPipelineBehavior<GetProductMarginSummaryRequest, …>` in `AnalyticsModule`. By contrast, `GetMarginReportRequest` and `GetProductMarginAnalysisRequest` both have validators and registered `ValidationResultBehavior` pipeline behaviors.

## Why it matters
`GET /api/analytics/product-margin-summary?timeWindow=bogus` will result in an unhandled `ArgumentException` propagating to the ASP.NET middleware and returning a 500 Internal Server Error instead of a 400 Bad Request with a user-actionable message.

## Suggested fix
Add a FluentValidation validator and register it with a pipeline behavior in `AnalyticsModule`, following the existing pattern for `GetMarginReportRequest`:

```csharp
// GetProductMarginSummaryRequestValidator.cs
public class GetProductMarginSummaryRequestValidator : AbstractValidator<GetProductMarginSummaryRequest>
{
    private static readonly string[] ValidTimeWindows =
        ["current-year", "current-and-previous-year", "last-6-months", "last-12-months", "last-24-months"];

    public GetProductMarginSummaryRequestValidator()
    {
        RuleFor(x => x.TimeWindow)
            .Must(v => ValidTimeWindows.Contains(v))
            .WithMessage($"TimeWindow must be one of: {string.Join(", ", ValidTimeWindows)}");
    }
}
```

And in `AnalyticsModule.AddAnalyticsModule`:
```csharp
services.AddScoped<IValidator<GetProductMarginSummaryRequest>, GetProductMarginSummaryRequestValidator>();
services.AddScoped<IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>,
    ValidationResultBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>>();
```

Alternatively, replace `string TimeWindow` with a `TimeWindow` enum to make invalid values impossible at the model-binding level.

---
_Filed by daily arch-review routine on 2026-09-19._