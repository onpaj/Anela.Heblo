## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetManufactureDifficultySettings/GetManufactureDifficultySettingsHandler.cs`

## Coverage
Line coverage: 30% (11/37 lines, filter threshold: 60%)

## What's not tested
The `Handle` method determines which difficulty setting is "current" by evaluating a nullable date range: `(ValidFrom == null || ValidFrom <= referenceDate) && (ValidTo == null || ValidTo >= referenceDate)`. Four boundary cases are uncovered: (1) `ValidFrom` is null (open-ended start) — should always satisfy the start condition; (2) `ValidTo` is null (open-ended end) — should always satisfy the end condition; (3) multiple settings overlap the reference date — only the first one is marked `IsCurrent`, the rest are not; (4) `request.AsOfDate` is supplied — the method should use it as `referenceDate` instead of the current time. The "no current setting" path (`currentSetting == null`) is also untested.

## Why it matters
Manufacture difficulty settings drive labour cost calculations. A wrong `IsCurrent` flag would cause the UI to display the wrong active setting or `CurrentSetting = null` when a valid setting exists. The `AsOfDate` override is the mechanism for historical queries — if it falls back to the current time silently, all historical views show today's data.

## Suggested approach
Unit tests with a mock repository returning a small list of settings: (1) single setting with null ValidFrom/ValidTo matching any date; (2) two settings where only one overlaps the query date → correct one is current; (3) two overlapping settings → first returned is current; (4) AsOfDate supplied → referenceDate is that date, not now; (5) no settings match → `CurrentSetting = null`, all `IsCurrent = false`. ~2h.

---
_Filed by weekly coverage-gap routine on 2026-10-05. Based on CI run #36876655065 (923c24940cf65d6ce38a8b56d75709bba333ca91)._
