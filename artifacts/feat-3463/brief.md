# [arch-review] Analytics: DailyInvoiceCount carries application-layer concern in Domain type

## Module
Analytics

## Finding
`DailyInvoiceCount.IsBelowThreshold` (`backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs:21`) is a Domain-layer type with a property that is set by the Application layer:

```csharp
// GetInvoiceImportStatisticsHandler.cs:50-52
foreach (var dayCount in dailyCounts)
{
    dayCount.IsBelowThreshold = dayCount.Count < minimumThreshold;
}
```

`minimumThreshold` comes from `InvoiceImportOptions` (application configuration). This is an application-layer decision — comparing against a configured threshold — being stamped back onto a Domain object. The Domain type is also mutable (`IsBelowThreshold { get; set; }`), which means the repository returns it and the handler modifies it before handing it to the response.

## Why it matters
Clean Architecture requires that Domain types be independent of application concerns. Here the Domain type has a property whose value is meaningless without knowledge of `InvoiceImportOptions.MinimumDailyThreshold`. The Domain entity leaks application configuration awareness. It also violates the principle that Domain objects should not be mutated by Application services after retrieval.

## Suggested fix
Remove `IsBelowThreshold` from `DailyInvoiceCount`. Compute it inline when building the response:

```csharp
return new GetInvoiceImportStatisticsResponse
{
    Data = dailyCounts.Select(c => new DailyInvoiceCountDto
    {
        Date = c.Date,
        Count = c.Count,
        IsBelowThreshold = c.Count < minimumThreshold
    }).ToList(),
    MinimumThreshold = minimumThreshold
};
```

A dedicated `DailyInvoiceCountDto` in `Contracts/` keeps the Domain type immutable and free of application concerns.

---
_Filed by daily arch-review routine on 2026-07-03._
