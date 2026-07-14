# [coverage-gap] Logistics/GetTransportBoxesHandler: "ACTIVE" special-case state filter untested

## Module / File
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxes/GetTransportBoxesHandler.cs`

## Coverage
Line coverage: 20.0% (filter threshold: 60%)

## What's not tested
The handler implements a three-way state-filter routing that is entirely uncovered:

1. **`State == "ACTIVE"` (case-insensitive)** → sets `isActiveFilter = true`, meaning "all boxes except Closed" — this is a special business rule, not an enum value.
2. **Parseable enum string** → sets `stateFilter` to the matching `TransportBoxState`, `isActiveFilter` stays false.
3. **Null/empty `State`** → both flags stay at their defaults; no filter applied.

Both `stateFilter` and `isActiveFilter` are forwarded to `GetPagedListAsync`. If the `ACTIVE` branch were accidentally removed or the string comparison changed (e.g., using `==` instead of `OrdinalIgnoreCase`), the main transport-box list view would silently return an empty or wrong result set with no error.

## Why it matters
The "ACTIVE" filter is the default view for the logistics transport-box screen. A regression here would show an empty list to users without any error indication.

## Suggested approach
Unit tests with a mocked `ITransportBoxRepository`, covering:
- `State = "ACTIVE"` → assert `isActiveFilter=true` passed to repository (not a parsed enum)
- `State = "active"` → same (case-insensitivity)
- `State = "Open"` → assert `stateFilter = TransportBoxState.Open`, `isActiveFilter = false`
- `State = null` / `State = ""` → assert both flags are default

~1–2 hours effort.

---
_Filed by weekly coverage-gap routine on 2026-07-13. Based on CI run #28968007617 (06d109fe5edcb456730222410f64385606100b1b)._
