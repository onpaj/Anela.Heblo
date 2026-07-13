## Module
Leaflet

## Finding
`LeafletGenerationRepository.GetGenerationStatsAsync` issues 4 independent async database queries to compute stats that could be returned in a single SQL aggregate:

```csharp
// backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs:55–67
var total = await _context.LeafletGenerations.CountAsync(cancellationToken);
var withFeedback = await _context.LeafletGenerations
    .CountAsync(g => g.PrecisionScore != null || g.StyleScore != null, cancellationToken);
var avgPrecision = await _context.LeafletGenerations
    .Where(g => g.PrecisionScore != null)
    .AverageAsync(g => (double?)g.PrecisionScore, cancellationToken);
var avgStyle = await _context.LeafletGenerations
    .Where(g => g.StyleScore != null)
    .AverageAsync(g => (double?)g.StyleScore, cancellationToken);
```

This method is called on every request to `GET /api/leaflet/feedback/list` (inside `GetLeafletFeedbackListHandler.Handle`, line 36), alongside the paged query — so each feedback list page load hits the database 5 times (1 paged query + 4 stats queries) instead of 2.

## Why it matters
Three unnecessary extra round trips per page load. The four values (`total`, `withFeedback`, `avgPrecision`, `avgStyle`) are independent aggregates over the same table — there is no cross-query dependency that forces sequential execution. PostgreSQL can compute all four in a single scan.

## Suggested fix
Replace the four calls with a single `FromSqlRaw` (or an equivalent LINQ projection) that returns all four values at once:

```sql
SELECT
    COUNT(*) AS total,
    COUNT(*) FILTER (WHERE "PrecisionScore" IS NOT NULL OR "StyleScore" IS NOT NULL) AS with_feedback,
    AVG("PrecisionScore") AS avg_precision,
    AVG("StyleScore") AS avg_style
FROM "LeafletGenerations";
```

Alternatively, use a LINQ `GroupBy(g => 1)` projection or a small anonymous-type query with `Select` + `SingleOrDefaultAsync`. Either way, one DB round trip replaces four.

---
_Filed by daily arch-review routine on 2026-07-11._
