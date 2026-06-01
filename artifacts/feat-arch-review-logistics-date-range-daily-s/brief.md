## Module
Logistics

## Finding
The same 5-line block for resolving a date range and computing `dailySales` is copy-pasted verbatim in two methods of `GiftPackageManufactureService`:

```
// Lines 55–63 in GetAvailableGiftPackagesAsync
var actualToDate = toDate ?? _timeProvider.GetUtcNow().DateTime;
var actualFromDate = fromDate ?? actualToDate.AddYears(-1);
var daysDiff = Math.Max((actualToDate - actualFromDate).Days, 1);
...
var totalSalesInPeriod = (decimal)product.GetTotalSold(actualFromDate, actualToDate) * salesCoefficient;
var dailySales = totalSalesInPeriod / daysDiff;

// Lines 112–118 in GetGiftPackageDetailAsync — identical
var actualToDate = toDate ?? _timeProvider.GetUtcNow().DateTime;
var actualFromDate = fromDate ?? actualToDate.AddYears(-1);
var daysDiff = Math.Max((actualToDate - actualFromDate).Days, 1);
...
var totalSalesInPeriod = (decimal)product.GetTotalSold(actualFromDate, actualToDate) * salesCoefficient;
var dailySales = totalSalesInPeriod / daysDiff;
```

File: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`

## Why it matters
`GetGiftPackageDetailAsync` already calls `GetAvailableGiftPackagesAsync` for the list scenario (line 200 in `CreateManufactureAsync`), so the duplication is not just stylistic — if the date-range defaulting logic ever changes (e.g. from 1 year to 6 months), it must be updated in two places.

## Suggested fix
Extract a private helper:

```csharp
private (DateTime from, DateTime to, int days) ResolveDateRange(DateTime? fromDate, DateTime? toDate)
{
    var to = toDate ?? _timeProvider.GetUtcNow().DateTime;
    var from = fromDate ?? to.AddYears(-1);
    return (from, to, Math.Max((to - from).Days, 1));
}
```

Call it at the top of both methods and replace the duplicated lines.

---
_Filed by daily arch-review routine on 2026-05-28._