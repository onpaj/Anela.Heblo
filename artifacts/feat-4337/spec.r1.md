# Specification: VatRateCalculator.FromPrices Unit Test Coverage

## Summary
`VatRateCalculator.FromPrices` in `backend/src/Anela.Heblo.Domain/Features/ProductPricing/VatRateCalculator.cs` is a pure, static domain function with 0% line coverage. It has two branches — a fallback to the standard 21% VAT rate for non-positive `priceWithoutVat`, and a VAT-recovery formula otherwise — neither of which is asserted by any test today. This spec defines the unit test suite needed to close that coverage gap.

## Background
This is a weekly coverage-gap finding (CI run #35977921040, filed 2026-09-28). `FromPrices` recovers the VAT rate implied by a gross/net price pair and is used by domain/pricing logic that feeds margin calculations and downstream accounting data. Because it has no dependencies (no I/O, no injected services), it is a pure-function candidate for a fast, isolated `xUnit` test suite — no mocks required.

## Functional Requirements

### FR-1: Cover the non-positive `priceWithoutVat` fallback branch
When `priceWithoutVat <= 0` (zero or negative), `FromPrices` must return `VatRateCalculator.StandardVatRate` (21m), regardless of `priceWithVat`.

**Acceptance criteria:**
- A test asserts `FromPrices(anyPriceWithVat, 0m)` returns `21m`.
- A test asserts `FromPrices(anyPriceWithVat, -10m)` returns `21m` (negative net price also falls back).

### FR-2: Cover the VAT-recovery formula branch (standard rate)
When `priceWithoutVat > 0`, `FromPrices` computes `Math.Round((priceWithVat / priceWithoutVat - 1) * 100m, 0)`.

**Acceptance criteria:**
- A test asserts `FromPrices(121m, 100m)` returns `21m` (121/100 - 1 = 0.21 → 21%, matching the standard Czech VAT rate — same value as the fallback, but reached via the formula branch, not the guard).

### FR-3: Cover the VAT-recovery formula branch (reduced rate)
**Acceptance criteria:**
- A test asserts `FromPrices(115m, 100m)` returns `15m` (115/100 - 1 = 0.15 → 15%, the reduced VAT rate).

### FR-4: Cover the rounding behaviour of the formula
The formula result is rounded to zero decimal places using `Math.Round(value, 0)`, which uses banker's rounding (`MidpointRounding.ToEven`) by default in .NET.

**Acceptance criteria:**
- At least one test exercises a price pair whose raw formula result is not a whole number (e.g. a value that lands on `x.5`) and asserts the rounded result matches .NET's default `Math.Round` (to-even) behavior for that input — computed and hand-verified against the actual `Math.Round(..., 0)` semantics, not assumed.
- The test names/comments make clear this is intentionally probing rounding behavior, not just another arbitrary rate.

## Non-Functional Requirements

### NFR-1: Test isolation and speed
No mocks, stubs, or external dependencies. Tests run as plain `[Theory]`/`[Fact]` xUnit tests against the static `VatRateCalculator` class and complete in milliseconds.

### NFR-2: Style consistency
Tests follow the existing conventions of `backend/test/Anela.Heblo.Tests/Features/ProductPricing/*.cs` (namespace `Anela.Heblo.Tests.Features.ProductPricing`, `FluentAssertions` for assertions, `xUnit` `[Theory]`/`[InlineData]` for parameterized cases, Arrange/Act/Assert or terse equivalent).

## Data Model
None — `VatRateCalculator` is a stateless static class operating on `decimal` inputs/outputs. No entities, no persistence.

## API / Interface Design
No new or changed public interface. The test suite targets the existing public surface:
- `VatRateCalculator.StandardVatRate` (const `decimal`, value `21m`)
- `VatRateCalculator.FromPrices(decimal priceWithVat, decimal priceWithoutVat) : decimal`

## Dependencies
- `Anela.Heblo.Domain` project reference (already available to the test project, since `IProductVatRateProvider` and other `ProductPricing` domain types are already exercised there).
- `xUnit` + `FluentAssertions` (already in use throughout `backend/test/Anela.Heblo.Tests`).

## Out of Scope
- Any change to `VatRateCalculator.cs` itself — this is a test-only addition; the production code is not touched.
- Testing of `IProductVatRateProvider` or its implementations — out of scope for this coverage gap.
- Broader ProductPricing coverage gaps beyond `VatRateCalculator.FromPrices`.

## Open Questions

None.

## Status: COMPLETE
