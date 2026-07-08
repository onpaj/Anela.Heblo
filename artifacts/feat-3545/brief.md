# [arch-review] InvoiceClassification: CreateClassificationRuleHandler loads all rules to compute max order

## Module
InvoiceClassification

## Finding
`CreateClassificationRuleHandler` (`CreateClassificationRuleHandler.cs:29–30`) fetches the entire rule set to derive the next order value:

```csharp
var allRules = await _ruleRepository.GetAllAsync();
var maxOrder = allRules.Count > 0 ? allRules.Max(r => r.Order) : 0;
```

`GetAllAsync` issues `SELECT * FROM ClassificationRules ORDER BY Order`, transferring every column of every rule across the wire just to extract a single integer. The cost grows linearly with the number of rules, and the full dataset is discarded immediately afterward.

## Why it matters
This is a YAGNI/efficiency violation: the repository interface already has a database-backed implementation, so pushing the aggregation into the DB is straightforward. Under moderate load (batch imports, concurrent creation) this also creates a race window where two concurrent handlers read the same max and assign duplicate order values.

## Suggested fix
Add `Task GetMaxOrderAsync()` to `IClassificationRuleRepository` and implement it as a targeted query:

```csharp
public async Task GetMaxOrderAsync()
    => await _context.ClassificationRules.MaxAsync(r => (int?)r.Order) ?? 0;
```

Replace the two handler lines with:

```csharp
var maxOrder = await _ruleRepository.GetMaxOrderAsync();
```

---
_Filed by daily arch-review routine on 2026-07-07._
