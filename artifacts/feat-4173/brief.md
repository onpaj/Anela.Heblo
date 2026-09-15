## Module / File
`backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/CriticalGiftPackagesTile.cs`

## Coverage
Line coverage: 0.0% (filter threshold: 60%)

## What's not tested
The tile has two untested branches: (1) when the underlying query returns `!response.Success`, the tile returns an error-status object with zero counts instead of the populated data object — no test verifies this response shape; (2) the outer `catch` block catches all exceptions and returns a different error shape than the `!response.Success` branch — no test asserts that an exception produces the correct error object.

## Why it matters
Dashboard consumers rely on the error-status shape to display fallback UI. If the `!Success` and exception paths return different shapes that are treated as equivalent, one will silently produce a null-reference or a wrong tile count.

## Suggested approach
Unit tests with mocked service: (1) service returns unsuccessful result → assert error-status shape with zero counts; (2) service throws → assert exception-error shape. ~1h effort.

---
_Filed by weekly coverage-gap routine on 2026-09-14. Based on CI run #34699120372 (722ec6efc4c1f7e5db235606c763a2f2b4a9a374)._