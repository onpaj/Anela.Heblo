## Module / File
`backend/src/Anela.Heblo.Domain/Features/ProductPricing/VatRateCalculator.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
`VatRateCalculator.FromPrices` has two distinct branches, neither of which is covered:
1. When `priceWithoutVat <= 0`, the method falls back to `StandardVatRate` (21 %). This is the guard for zero or negative prices.
2. When `priceWithoutVat > 0`, the method computes the actual VAT rate via `(priceWithVat / priceWithoutVat - 1) × 100`, rounded to zero decimals.

Neither branch has any test asserting the returned value.

## Why it matters
This is domain financial logic used to recover the VAT rate from a price pair. If the fallback is accidentally removed or the rounding is changed, pricing calculations that rely on the recovered rate will silently produce wrong figures — affecting margin calculations and possibly downstream accounting data.

## Suggested approach
Unit tests for `VatRateCalculator.FromPrices` as a pure-function suite (no dependencies to stub):
- `priceWithoutVat <= 0` → returns `21`
- A pair that yields the standard rate (e.g. `121 / 100 - 1 = 21 %`) → returns `21`
- A pair with a reduced rate (e.g. `115 / 100 - 1 = 15 %`) → returns `15`
- Rounding edge (result not a whole number) → verify the `Math.Round(..., 0)` behaviour

Effort: ~30 min, no mocks needed.

---
_Filed by weekly coverage-gap routine on 2026-09-28. Based on CI run #35977921040 (22bb3b8ff6194bdd055cb438d08b7c6633a85221)._
