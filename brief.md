## Module
Journal

## Finding
`JournalIndicator` declares three properties:

```csharp
// backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs:9–10
public int FamilyEntries { get; set; }
public int TotalEntries => DirectEntries + FamilyEntries;
```

`JournalIndicatorDto` mirrors this exactly:

```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs:9–10
public int FamilyEntries { get; set; }
public int TotalEntries => DirectEntries + FamilyEntries;
```

`GetJournalIndicatorsAsync` in `JournalRepository.cs` (lines 175–220) populates `DirectEntries` from a grouped query on lines 189–208, but `FamilyEntries` is never assigned. It stays at `0` for every indicator. `TotalEntries` therefore always equals `DirectEntries`. The distinction between "direct" and "family" entries exists conceptually (prefix matching vs. exact match) but is not implemented.

## Why it matters
Any consumer reading `TotalEntries` or `FamilyEntries` from the API gets misleading data — `FamilyEntries` is always 0 and `TotalEntries` adds no information over `DirectEntries`. The YAGNI principle: speculative properties that aren't computed shouldn't exist in the public API surface.

## Suggested fix
Two options — pick one:
1. **Implement it**: add a prefix-based query to `GetJournalIndicatorsAsync` that counts entries linked via product code prefixes (e.g. where `productCode.StartsWith(jep.ProductCodePrefix)` but not exact match) and assign to `FamilyEntries`.
2. **Remove it** (simplest): delete `FamilyEntries` and change `TotalEntries` to a straight property set equal to `DirectEntries`. Remove the mirroring in `JournalIndicatorDto` as well.

---
_Filed by daily arch-review routine on 2026-05-12._