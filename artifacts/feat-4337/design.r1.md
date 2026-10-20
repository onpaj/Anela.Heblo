# Design: VatRateCalculator.FromPrices Unit Test Coverage

## Component Design

### `VatRateCalculatorTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/ProductPricing/VatRateCalculatorTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.ProductPricing`
- **Responsibility:** Exercise the two branches of `VatRateCalculator.FromPrices` as a pure-function test suite. No setup/teardown, no fixtures, no mocks — the class under test is `static` and stateless.
- **Interface it depends on:** `Anela.Heblo.Domain.Features.ProductPricing.VatRateCalculator` (existing, unchanged):
  - `public const decimal StandardVatRate = 21m;`
  - `public static decimal FromPrices(decimal priceWithVat, decimal priceWithoutVat)`
- **Test shape:** a single `[Theory]` with `[InlineData]` rows, one per case from spec FR-1–FR-4, following the pattern already used by `PriceComparisonDqtAdapterTests.GetDivergencesAsync_ClassifiesEachKindAsMismatchOrNot` in the same folder:

  | priceWithVat | priceWithoutVat | expected | covers |
  |---|---|---|---|
  | any (e.g. `100m`) | `0m` | `21m` | FR-1: non-positive net price falls back to standard rate |
  | any (e.g. `100m`) | `-10m` | `21m` | FR-1: negative net price also falls back |
  | `121m` | `100m` | `21m` | FR-2: formula branch reaches the standard rate |
  | `115m` | `100m` | `15m` | FR-3: formula branch computes a reduced rate |
  | `211m` | `200m` | `6m` | FR-4: rounding-edge case — raw result `5.5` rounds to `6` via `MidpointRounding.ToEven` (banker's rounding: 5.5 rounds to the nearest even integer, which is 6) |

  Assertion per row: `VatRateCalculator.FromPrices(priceWithVat, priceWithoutVat).Should().Be(expected);`

No other component is created, modified, or has its contract changed.

## Data Schemas
Not applicable — no persistence, no API request/response shape, no event payload. Inputs and outputs are plain `decimal` values passed directly to/from a static method call within the test.
