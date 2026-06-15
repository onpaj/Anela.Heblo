# Specification: Unit test coverage for ManufactureOrderExtensions

## Summary
Raise unit-test coverage of `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderExtensions.cs` from 49% above the 60% line-coverage threshold by adding pure unit tests around `GetDefaultLot`, `GetDefaultExpiration`, and the four `SetDefault*` writers. The tests must lock the current behavior of the ISO-8601 week-number calculation, the lot-number string format (`wwyyyyMM`), and the "last day of month + 1 month" expiration arithmetic so that any future change that would silently shift a printed lot number or shelf-life date fails CI.

## Background
`ManufactureOrderExtensions` produces two values that are stamped onto physical cosmetic product batches:
- **Lot number** in the format `wwyyyyMM` (ISO week, four-digit year, two-digit month) — printed on every unit and used for regulatory traceability.
- **Expiration date** — derived from manufacture date plus a per-product/per-semi-product `ExpirationMonths` value, then rounded to the last day of the *following* calendar month — stored in the ERP and printed on labels.

Both values are non-recoverable after production: a wrong ISO week silently produces a mismatched lot number, and a one-month drift in expiration mislabels shelf life. The current line coverage of 49% leaves the non-obvious pieces — the Sunday-to-7 correction in `GetWeekNumber`, the `Math.Ceiling` week calculation, the double `AddMonths` minus one day in `GetDefaultExpiration`, and the `D2` zero-padding in the lot string — unverified. Adding tests for these is required to defend the current behavior against regressions and to meet the project's 60% coverage gate.

This is a test-only change. The production code is **not** modified.

## Functional Requirements

### FR-1: Test project and file location
A new test class `ManufactureOrderExtensionsTests` is created under the existing backend test project that already covers `Anela.Heblo.Domain.Features.Manufacture`:

- File path: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderExtensionsTests.cs`
- Namespace: `Anela.Heblo.Tests.Features.Manufacture`
- Framework: xUnit (matches the rest of `Anela.Heblo.Tests`)
- Assertions: FluentAssertions (matches existing convention in the same folder)
- No DI container, no mocks, no fixtures — all tests are pure-function tests against the static extension class.

**Acceptance criteria:**
- File exists at the path above and compiles as part of `Anela.Heblo.Tests`.
- `dotnet test backend/test/Anela.Heblo.Tests` discovers and runs the new tests.
- No new NuGet packages are added.

### FR-2: `GetDefaultLot(DateTime)` — week / month / year formatting
Verify the output of `ManufactureOrderExtensions.GetDefaultLot(DateTime)` for representative dates that exercise every branch of the format string.

**Acceptance criteria:**
- A `[Theory]` covers, at minimum, the following dates and expected lot numbers (verify each expected value against the current implementation; the test locks the *current* observed output):
  - `2024-01-01` (Monday, ISO week 1, month 01, year 2024) → `01202401`
  - `2024-05-29` (Wednesday in mid-year, single-digit week boundary) → exercises both `D2`-padded week and month
  - `2024-12-30` (Monday — ISO week 1 of 2025 within December 2024 — locks the year-vs-ISO-year quirk in the current code, which uses calendar year, not ISO year)
  - `2024-12-29` (Sunday — exercises the `dayNum == 0 → 7` branch)
  - `2020-12-31` (Thursday — ISO week 53 case)
  - `2024-02-29` (leap day — month padded `02`)
  - `2024-09-15` (single-digit month padded `09`)
- Each case asserts that the returned string is exactly 8 characters and matches `^\d{2}\d{4}\d{2}$`.
- Each case asserts the substring breakdown: chars `[0..2)` = ISO week, `[2..6)` = calendar year, `[6..8)` = calendar month.

### FR-3: `GetDefaultLot` on `ManufactureOrderSemiProduct`
Verify that the instance extension `GetDefaultLot(this ManufactureOrderSemiProduct, DateTime)` returns the same value as the static `GetDefaultLot(DateTime)` for the same `manufactureDate`. The semi-product instance state must not affect the output.

**Acceptance criteria:**
- A single test creates a `ManufactureOrderSemiProduct` with arbitrary property values, calls `.GetDefaultLot(date)`, and asserts equality with `GetDefaultLot(date)` for the same date.

### FR-4: `GetDefaultExpiration(DateTime, int months)` — arithmetic
Lock the current double-`AddMonths`-minus-one-day behavior: the result is the last day of the month that is `months + 1` calendar months after `manufactureDate`.

**Acceptance criteria:**
- A `[Theory]` covers the following `(manufactureDate, months, expected DateOnly)` cases. Each expected value must be derived from the current code and asserted as a `DateOnly`:
  - `(2024-01-15, 24)` → `2026-02-28` (24 months → Jan 2026; +1 month rollover → last day of Feb 2026, non-leap)
  - `(2024-01-15, 25)` → `2026-03-31`
  - `(2024-02-15, 12)` → `2025-03-31`
  - `(2023-01-31, 1)` → `2023-03-31` (verifies that `AddMonths` from Jan-31 to Feb-28 then to last day of next month works)
  - `(2024-01-15, 23)` → `2026-01-31` (last day of Jan, 31 days)
  - `(2024-01-15, 0)` → `2024-02-29` (leap-year February)
  - `(2023-01-15, 0)` → `2023-02-28` (non-leap February)
  - `(2024-11-15, 2)` → `2025-02-28` (year boundary crossing)
- Each case asserts the returned `DateOnly` exactly (year, month, day).
- A separate `[Fact]` documents the "last-day-of-month" invariant: for any input the returned date's day equals `DateTime.DaysInMonth(returnedYear, returnedMonth)`. Run this assertion against the parameterized cases as well.

### FR-5: `GetDefaultExpiration` on `ManufactureOrderSemiProduct`
Verify that the instance extension `GetDefaultExpiration(this ManufactureOrderSemiProduct, DateTime)` forwards `ExpirationMonths` from the semi-product to the static method.

**Acceptance criteria:**
- A `[Fact]` constructs a `ManufactureOrderSemiProduct` with `ExpirationMonths = 24`, calls `.GetDefaultExpiration(2024-01-15)`, and asserts the result equals `GetDefaultExpiration(2024-01-15, 24)`.

### FR-6: `SetDefaultExpiration` writers
Verify all three `SetDefaultExpiration` overloads write the computed value to the correct property.

**Acceptance criteria:**
- `SetDefaultExpiration(ManufactureOrderSemiProduct, DateTime)` sets `semiProduct.ExpirationDate` to `GetDefaultExpiration(date, semiProduct.ExpirationMonths)` and leaves all other properties untouched. The pre-existing value of `ExpirationDate` is irrelevant — the method is a setter, not a guard.
- `SetDefaultExpiration(ManufactureOrderProduct, DateTime, int)` sets `product.ExpirationDate` to `GetDefaultExpiration(date, months)`.
- `SetDefaultExpiration(ManufactureOrder, DateTime)` sets:
  - `order.SemiProduct.ExpirationDate` from `order.SemiProduct.ExpirationMonths`, and
  - each `order.Products[i].ExpirationDate` to the *same* value derived from the semi-product's expiration months (note: the `Product`-level `ExpirationMonths`, if any, is intentionally ignored — lock this behavior).
- The order-level test uses at least two products in `order.Products` and asserts each receives the same expiration.

### FR-7: `SetDefaultLot` writers
Verify all three `SetDefaultLot` overloads write the computed value to the correct property.

**Acceptance criteria:**
- `SetDefaultLot(ManufactureOrderSemiProduct, DateTime)` sets `semiProduct.LotNumber` to `GetDefaultLot(date)`.
- `SetDefaultLot(ManufactureOrderProduct, DateTime)` sets `product.LotNumber` to `GetDefaultLot(date)`.
- `SetDefaultLot(ManufactureOrder, DateTime)` sets `order.SemiProduct.LotNumber` and each `order.Products[i].LotNumber` to the same value.
- The order-level test uses at least two products and asserts each receives the same lot number.

### FR-8: Coverage threshold
After the new tests are added, the line coverage of `ManufactureOrderExtensions.cs` must be at or above 60% as measured by whatever coverage tool the existing CI run (`#27416879267`) used. In practice, the test cases listed in FR-2 through FR-7 should cover every executable line of the file, including both branches of `if (dayNum == 0) dayNum = 7;`.

**Acceptance criteria:**
- Running the project's coverage collector locally on `Anela.Heblo.Tests` shows `ManufactureOrderExtensions.cs` ≥ 60% line coverage.
- The `dayNum == 0 → 7` branch is exercised by at least one Sunday-dated test (FR-2 covers this via `2024-12-29`).

## Non-Functional Requirements

### NFR-1: Performance
Tests are pure CPU work over a handful of dates. The full `ManufactureOrderExtensionsTests` class must run in under 100 ms total locally. No I/O, no DB, no DateTime.Now / DateTime.UtcNow — every input is a deterministic literal.

### NFR-2: Determinism and locale safety
Tests must not depend on:
- The current culture (`CultureInfo.CurrentCulture`) or the OS calendar setting — `GetWeekNumber` uses raw integer arithmetic and the format strings use `D2` rather than culture-specific formatting. The tests must use literal expected strings (`"01202401"`, not `$"{week:D2}..."`) so a regression in the production format string is caught.
- The current time zone — all inputs are unspecified-kind `DateTime` literals; `GetWeekNumber` internally rewraps to `DateTimeKind.Utc`, so the test does not need to set a kind.

### NFR-3: Style and conventions
- Follow the project conventions in `~/.claude/rules/csharp-coding-style.md` and `~/.claude/rules/csharp-testing.md`:
  - xUnit `[Fact]` / `[Theory]` + `[InlineData]`
  - FluentAssertions (`result.Should().Be(...)`, `result.Should().MatchRegex(...)`)
  - AAA structure with `// Arrange` / `// Act` / `// Assert` comments
  - Test method names of the form `Method_DoesBehavior_WhenCondition`
- Nullable reference types enabled (inherits from the test project).
- No `dotnet format` violations.

### NFR-4: Surgical scope
**Production code under `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderExtensions.cs` must not be modified.** The coverage gap is closed exclusively by adding tests. If a behavior currently produced by the code is surprising (e.g. the use of calendar year alongside ISO week), the test locks the *current* behavior and the surprise is noted in a single-line `// note:` comment on the relevant `[InlineData]` row — no refactor.

## Data Model
No persistent data model changes. The tests instantiate the following existing types directly:

- `Anela.Heblo.Domain.Features.Manufacture.ManufactureOrder`
- `Anela.Heblo.Domain.Features.Manufacture.ManufactureOrderSemiProduct`
- `Anela.Heblo.Domain.Features.Manufacture.ManufactureOrderProduct`

For each type, only the properties referenced by the extension methods need to be initialized:
- `ManufactureOrderSemiProduct.ExpirationMonths` (int)
- `ManufactureOrderSemiProduct.ExpirationDate` (DateOnly?)
- `ManufactureOrderSemiProduct.LotNumber` (string?)
- `ManufactureOrderProduct.ExpirationDate` (DateOnly?)
- `ManufactureOrderProduct.LotNumber` (string?)
- `ManufactureOrder.SemiProduct` (single instance)
- `ManufactureOrder.Products` (`List<ManufactureOrderProduct>`)

Required properties on these types not used by the extension methods must still be set to satisfy `required`/non-nullable constraints. Use minimal placeholder values (e.g. `""`, `0`, `Guid.NewGuid()`); they are not asserted on.

## API / Interface Design
No public API changes. No new endpoints, no MediatR handlers, no DTOs. The test class only consumes the existing `public static ManufactureOrderExtensions` class and the three entity types listed under Data Model.

## Dependencies
- xUnit (already referenced by `Anela.Heblo.Tests.csproj`)
- FluentAssertions (already referenced)
- `Anela.Heblo.Domain` project reference (already in `Anela.Heblo.Tests.csproj`)

No new package or project references are required.

## Out of Scope
- Refactoring `ManufactureOrderExtensions` — including the calendar-year-vs-ISO-year inconsistency in `GetDefaultLot`, the double-`AddMonths` pattern in `GetDefaultExpiration`, or the duplicated lot-format calculation inside `GetDefaultExpiration` (lines 53–56) that overwrites a local `lotNumber` variable that is never returned. These are noted only; not fixed.
- Behavioral changes to `GetDefaultLot` to use `ISOWeek.GetYear()` instead of calendar year (this would change printed lot numbers and is a separate product decision).
- Adding `ExpirationMonths` to `ManufactureOrderProduct` or honoring a product-level expiration override in `SetDefaultExpiration(ManufactureOrder, DateTime)`.
- Integration tests, controller tests, or repository tests — this gap is in a pure-function utility.
- Changes to other files in `backend/src/Anela.Heblo.Domain/Features/Manufacture/`.
- Updating coverage configuration (`coverlet.runsettings`, threshold flags, CI workflow) — the 60% threshold is already enforced; this work raises the *measured* number above it.

## Open Questions
None.

## Status: COMPLETE