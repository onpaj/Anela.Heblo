## Module
KnowledgeBase

## Finding
`KnowledgeBaseRepository.GetFeedbackStatsAsync` fetches every feedback row that has any score into a `List<>` in application memory and then averages them in C#:

```csharp
// backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs:303-321
var withFeedback = await _context.KnowledgeBaseQuestionLogs
    .Where(l => l.PrecisionScore != null || l.StyleScore != null)
    .ToListAsync(ct);                          // ← entire result set materialised

double? avgPrecision = withFeedback.Count > 0
    ? withFeedback.Where(l => l.PrecisionScore != null)
                  .Average(l => (double?)l.PrecisionScore)
    : null;
```

`KnowledgeBaseQuestionLogs` grows with every `/ask` call. As usage increases, this method will transfer an unbounded number of full log rows (including `Question` and `Answer` text fields) over the wire just to compute two scalar averages.

## Why it matters
SQL aggregation (`AVG`, `COUNT`) is far more efficient than client-side LINQ. This is a concrete performance regression path: the method is called every time the Feedback page loads its stats header. Loading full text columns (`Question`, `Answer`) into memory for averaging violates KISS — the DB can compute this in a single pass over an index.

## Suggested fix
Replace the in-memory aggregation with a single SQL-side query using EF Core:

```csharp
var totalQuestions = await _context.KnowledgeBaseQuestionLogs.CountAsync(ct);

var totalWithFeedback = await _context.KnowledgeBaseQuestionLogs
    .CountAsync(l => l.PrecisionScore != null || l.StyleScore != null, ct);

var avgPrecision = await _context.KnowledgeBaseQuestionLogs
    .Where(l => l.PrecisionScore != null)
    .AverageAsync(l => (double?)l.PrecisionScore, ct);

var avgStyle = await _context.KnowledgeBaseQuestionLogs
    .Where(l => l.StyleScore != null)
    .AverageAsync(l => (double?)l.StyleScore, ct);
```

That's 4 lightweight aggregation queries (or collapse into one via `GroupBy(_ => 1)` + `.Select`) instead of a full table scan materialised in memory.

---
_Filed by daily arch-review routine on 2026-05-13._