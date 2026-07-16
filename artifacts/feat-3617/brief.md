## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidator.cs`

## Coverage
Line coverage: 0.0% (filter threshold: 60%)

## What's not tested
The validator has no test coverage at all. The most critical gap is the cross-field date invariant:

```
ValidFrom < ValidTo   (when both have values)
ValidTo > ValidFrom   (when both have values)
```

These rules use `.When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue)`, which means:
- When only one date is set the rules are skipped — it is unclear whether that is intentional
- When both are set and `ValidFrom >= ValidTo`, the request should be rejected

Additionally uncovered: `ProductCode` required + max 50 chars, `DifficultyValue >= 0`.

## Why it matters
A valid-from date that equals or is later than valid-to would create a manufacture difficulty record with an inverted validity window. Any code that queries "active difficulties" by date range would get wrong results silently (no record active when one should be, or the wrong record active).

## Suggested approach
Unit tests on `CreateManufactureDifficultyRequestValidator`:
- `ValidFrom < ValidTo` (both set) → no error
- `ValidFrom == ValidTo` (both set) → validation error
- `ValidFrom > ValidTo` (both set) → validation error
- Only `ValidFrom` set, `ValidTo` null → no cross-field error (confirm intended)
- Only `ValidTo` set, `ValidFrom` null → no cross-field error (confirm intended)
- `ProductCode = ""` → required error
- `DifficultyValue = -1` → validation error

~1 hour effort.

---
_Filed by weekly coverage-gap routine on 2026-07-13. Based on CI run #28968007617 (06d109fe5edcb456730222410f64385606100b1b)._
