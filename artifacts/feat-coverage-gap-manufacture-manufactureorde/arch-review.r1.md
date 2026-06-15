# Architecture Review: Unit test coverage for ManufactureOrderExtensions

## Skip Design: true

## Architectural Fit Assessment
This is a **test-only change** to raise line coverage of a pure static extension class above the 60% CI gate. It introduces no new public API, no DI registrations, no MediatR handlers, no DB calls, and no production behavior changes. It aligns cleanly with the existing pattern in `backend/test/Anela.Heblo.Tests/Features/Manufacture/`, which already contains pure-function test classes (e.g. `ErpManufactureTypeTests.cs`, `ManufactureTypeTests.cs`, `ManufactureOrderStateTransitionTests.cs`) using xUnit + FluentAssertions with no fixtures and no DI. Integration with CI is mechanical — the new test file is auto-discovered by `dotnet test backend/test/Anela.Heblo.Tests` and coverlet collects coverage as today (no `coverlet.runsettings` change needed).

Verified against the codebase:
- `Anela.Heblo.Tests.csproj` already references xUnit 2.9.2, FluentAssertions 6.12.0, coverlet.collector 6.0.2, and the `Anela.Heblo.Domain` project (lines 12–28). **No new packages required.**
- `ManufactureOrder.SemiProduct` is declared `ManufactureOrderSemiProduct? SemiProduct { get; set; } = null!;` — nullable on paper, but the production code `manufactureOrder.SemiProduct.ExpirationMonths` (line 22) and `SemiProduct.ExpirationDate = …` (line 23) dereference it unconditionally. Tests for `SetDefaultExpiration(ManufactureOrder, DateTime)` and `SetDefaultLot(ManufactureOrder, DateTime)` MUST initialize `SemiProduct` to a real instance.
- `manufactureOrder.Products` is `List<ManufactureOrderProduct>` (concrete, non-nullable, initialized to `new()`). The production code calls `.ForEach(…)` — an instance method on `List<T>`. Tests must use `List<ManufactureOrderProduct>` (not `IList`/`IEnumerable`) — otherwise compilation fails. The spec already implies this; flagging it for the implementer.

## Proposed Architecture

### Component Overview
```
backend/
└── test/
    └── Anela.Heblo.Tests/
        └── Features/
            └── Manufacture/
                └── ManufactureOrderExtensionsTests.cs   ← NEW (only file added)
```

Single new test class. No new test fixtures, helpers, or builders. The class consumes:
- `ManufactureOrderExtensions` (SUT)
- `ManufactureOrder`, `ManufactureOrderSemiProduct`, `ManufactureOrderProduct` (existing domain entities, instantiated inline)

### Key Design Decisions

#### Decision 1: Test file co-location
**Options considered:**
- (A) Place the new test class under `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderExtensionsTests.cs` (matches every other test class in this folder).
- (B) Create a `Domain/` sub-folder mirroring the source path (`Anela.Heblo.Domain/Features/Manufacture/`).

**Chosen approach:** (A) — exact file path matches the spec FR-1.

**Rationale:** Every existing test class in `backend/test/Anela.Heblo.Tests/Features/Manufacture/` lives at this depth regardless of whether the SUT is in `Domain`, `Application`, or `Infrastructure`. Inventing a new `Domain/` sub-folder would diverge from the established convention without functional benefit.

#### Decision 2: Test organization — one class, sectioned by SUT method
**Options considered:**
- (A) One `ManufactureOrderExtensionsTests` class, regions or `#region` blocks per public method.
- (B) Nested classes per public method (e.g. `GetDefaultLotTests`, `GetDefaultExpirationTests`).
- (C) Separate `*.cs` files per public method.

**Chosen approach:** (A) — single flat class with method-name-prefixed test names (`GetDefaultLot_ReturnsCorrectFormat_ForMondayInWeek1`, `SetDefaultLot_WritesSameValue_ToAllProducts`, etc.).

**Rationale:** Matches the convention in `ManufactureOrderMappingProfileTests.cs` and `ManufactureOrderStateTransitionTests.cs` (single class, flat test list). Nested classes would add navigation friction without semantic value for ~25 tests against 9 short members.

#### Decision 3: How to construct `ManufactureOrder` for writer tests
**Options considered:**
- (A) Direct inline construction with object initializers in each test.
- (B) A private `MakeOrder(int semiProductExpirationMonths, int productCount)` helper inside the test class.

**Chosen approach:** (B) — a single private factory `MakeOrder(int semiProductExpirationMonths, int productCount = 2)` inside the test class.

**Rationale:** The order-level tests in FR-6 and FR-7 both require ≥2 products, a non-null `SemiProduct`, and the rest of `ManufactureOrder`'s `= null!` required strings (`OrderNumber`, `CreatedByUser`, `StateChangedByUser`) populated to prevent NREs in any future assertion that walks the object. Inlining this in two tests would duplicate ~10 lines; a 5-line local helper is more readable and keeps the AAA blocks focused on what is actually being asserted. Helper is kept inside the test class (not in a separate `*Builder.cs` file) per Decision 2.

#### Decision 4: Lock current behavior vs. fix obvious bugs
**Options considered:**
- (A) Fix the dead `lotNumber` calculation inside `GetDefaultExpiration` (lines 53–56 of the SUT), use `ISOWeek.GetYear()` for the lot year, and rewrite `GetWeekNumber` to use `System.Globalization.ISOWeek`.
- (B) Lock the current observable behavior verbatim. Note surprises as `// note:` comments on the relevant `[InlineData]` rows. Do not touch production code.

**Chosen approach:** (B) — per spec NFR-4.

**Rationale:** Coverage gates exist to detect *unintended* behavior changes. Fixing the calendar-year-vs-ISO-year quirk would change every printed lot number around year boundaries — a product decision that requires regulatory/ops sign-off, not a code-review aside. The spec is explicit: this is a test-only change.

## Implementation Guidance

### Directory / Module Structure
**One new file only:**
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderExtensionsTests.cs`
  - Namespace: `Anela.Heblo.Tests.Features.Manufacture`
  - `using Anela.Heblo.Domain.Features.Manufacture;`
  - `using FluentAssertions;`
  - `using Xunit;`

**No changes to:**
- `Anela.Heblo.Tests.csproj` (no new packages, no `<Compile Remove>` exclusions)
- `coverlet.runsettings` (existing collector config covers this assembly)
- Any production file under `backend/src/`
- CI workflow / coverage threshold (`60` stays put — this PR raises the *measured* number above it)

### Interfaces and Contracts
The SUT exposes nine public members. Each test is named `<Member>_<ExpectedBehavior>_<Condition>`:

| Member | Test count (minimum) |
|---|---|
| `GetDefaultLot(DateTime)` static | 7 `[InlineData]` rows (per FR-2) + 1 format/length assertion per row |
| `GetDefaultLot(ManufactureOrderSemiProduct, DateTime)` | 1 `[Fact]` (FR-3) |
| `GetDefaultExpiration(DateTime, int)` static | 8 `[InlineData]` rows (per FR-4) + 1 last-day-of-month invariant |
| `GetDefaultExpiration(ManufactureOrderSemiProduct, DateTime)` | 1 `[Fact]` (FR-5) |
| `SetDefaultExpiration(ManufactureOrderSemiProduct, DateTime)` | 1 `[Fact]` (FR-6) |
| `SetDefaultExpiration(ManufactureOrderProduct, DateTime, int)` | 1 `[Fact]` (FR-6) |
| `SetDefaultExpiration(ManufactureOrder, DateTime)` | 1 `[Fact]` (FR-6) |
| `SetDefaultLot(ManufactureOrderSemiProduct, DateTime)` | 1 `[Fact]` (FR-7) |
| `SetDefaultLot(ManufactureOrderProduct, DateTime)` | 1 `[Fact]` (FR-7) |
| `SetDefaultLot(ManufactureOrder, DateTime)` | 1 `[Fact]` (FR-7) |

Each `SetDefault*` test verifies the writer wrote and also asserts that the written value equals the corresponding `GetDefault*` output, so the writers are tested as a thin shell over the pure functions.

### Data Flow
For the three "writer-on-order" tests (`SetDefaultExpiration(ManufactureOrder, …)`, `SetDefaultLot(ManufactureOrder, …)`):

```
Test                                    Production SUT
─────────────────────────                ───────────────
Arrange: MakeOrder(expMonths=24, n=2)
  ManufactureOrder
    .SemiProduct = new() { ExpirationMonths = 24, ... }
    .Products    = [ new(), new() ]
                            │
                            ▼
Act:  order.SetDefaultExpiration(2024-01-15)
                            │
                            ├──► GetDefaultExpiration(2024-01-15, 24) → 2026-02-28
                            ├──► order.SemiProduct.ExpirationDate = 2026-02-28
                            └──► order.Products.ForEach(p => p.ExpirationDate = 2026-02-28)
                            │
                            ▼
Assert:
  order.SemiProduct.ExpirationDate.Should().Be(new DateOnly(2026, 2, 28))
  order.Products.Should().AllSatisfy(p => p.ExpirationDate.Should().Be(new DateOnly(2026, 2, 28)))
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Test author hand-computes an expected lot number wrong, locking a value the production code does NOT actually emit (false green). | Medium | Per spec NFR-2, expected values are literal strings. Before adding `[InlineData]` rows, the implementer runs `dotnet test` once with a single asserting test (`result.Should().Be("PROBE")`) per date, reads the actual output from the failure message, and only then locks the real value. Document the technique in a single PR-description line ("expected values captured from current production output of commit `2d0eaef6`"). |
| `ManufactureOrder.SemiProduct` is declared nullable; tests that omit initializing it will NRE in `SetDefaultExpiration(ManufactureOrder, …)` and `SetDefaultLot(ManufactureOrder, …)`. | Low | The `MakeOrder` helper (Decision 3) always sets `SemiProduct` to a fresh instance. Spec FR-6 / FR-7 already implies this; called out here for the implementer. |
| The dead-code lot calculation in `GetDefaultExpiration` (lines 53–56 of the SUT) is *executed* (so it counts toward coverage) but its result is never returned. A future refactor that deletes it would not change behavior but would drop coverage. | Low | This is explicitly out of scope (NFR-4). The test suite verifies only return values, so deleting the dead lines later will not break tests — but coverage may dip. Note this in the PR description as a known follow-up. |
| `GetWeekNumber` produces ISO week numbers that may diverge from `System.Globalization.ISOWeek.GetWeekOfYear` for the year-boundary cases (e.g. `2024-12-30` is ISO week 1 of *2025*; the production code uses calendar year 2024). The spec correctly locks this. Implementer may "fix" it on autopilot. | Medium | Add a single `// note: calendar year used, not ISO year — locks current behavior, see brief.md` comment on the `[InlineData(2024-12-30 …)]` row. Spec NFR-4 covers this but the implementer benefits from the in-file note. |
| Coverage tool reports < 60% locally despite all paths exercised (e.g. compiler-generated branches in switch expressions or null-coalescing — none present here, but worth checking). | Low | Spec FR-8 requires running the project's collector locally before declaring done. The SUT has only one conditional (`if (dayNum == 0) dayNum = 7;`), exercised by the `2024-12-29` Sunday case. All other lines are straight-line. Expected coverage should be ~100% of `ManufactureOrderExtensions.cs`. |
| `DateTime` `Kind` differences (`Unspecified` vs `Utc`) cause `GetWeekNumber`'s internal `new DateTime(…, DateTimeKind.Utc)` to behave differently if the input `Kind` flips. | Low | Inputs are unspecified-kind literals (e.g. `new DateTime(2024, 1, 1)`). `GetWeekNumber` rewraps to `DateTimeKind.Utc` immediately, so the `Kind` of the input does not affect arithmetic. No mitigation needed; NFR-2 already documents this. |

## Specification Amendments

The spec is high-quality and ready to implement. Two minor additions for the implementer:

1. **FR-1 — add an explicit project reference check.** The spec lists "Anela.Heblo.Domain project reference (already in `Anela.Heblo.Tests.csproj`)" under Dependencies, which is verified. Add a sentence to FR-1: *"No additions to `Anela.Heblo.Tests.csproj` are required (xUnit 2.9.2, FluentAssertions 6.12.0, coverlet.collector 6.0.2, and `<ProjectReference>` to `Anela.Heblo.Domain` are already present)."* — this preempts an implementer from speculatively bumping versions.

2. **FR-6 / FR-7 — initialize `ManufactureOrder.SemiProduct` and required strings.** The order-level tests must construct a `ManufactureOrder` with at minimum:
   - `OrderNumber = ""`, `CreatedByUser = ""`, `StateChangedByUser = ""` (the `= null!` properties; non-nullable reference warnings will not fire because of `null!`, but if any future assertion touches them, an NRE would be confusing).
   - `SemiProduct = new ManufactureOrderSemiProduct { ProductCode = "", ProductName = "", ExpirationMonths = 24 }` (the `= null!` annotation on `SemiProduct` is misleading — production code dereferences it unconditionally on line 22 of `ManufactureOrderExtensions.cs`).
   - `Products = new List<ManufactureOrderProduct> { new() { ProductCode = "", ProductName = "", SemiProductCode = "" }, new() { ProductCode = "", ProductName = "", SemiProductCode = "" } }` — must be a concrete `List<T>` because production calls instance `List<T>.ForEach`.

   Recommend the spec call out these specific construction requirements (or accept the proposed private `MakeOrder` helper as the implementation pattern, see Decision 3).

3. **NFR-2 — capture-then-lock technique.** Add: *"Expected values for `[InlineData]` rows are captured from the current production output of the SUT (commit listed in the PR description) and pasted as literals. They are not hand-computed."* — closes the "false green" risk above and matches the spec's intent ("lock the current observed output").

No other amendments. FR-2 through FR-8 are precise enough to implement directly.

## Prerequisites
None. All required infrastructure is in place:

- `Anela.Heblo.Tests.csproj` exists with all required packages (xUnit, FluentAssertions, coverlet.collector) and a `<ProjectReference>` to `Anela.Heblo.Domain`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/` exists.
- `dotnet test backend/test/Anela.Heblo.Tests` runs in CI (per the brief's reference to run `#27416879267`).
- The 60% coverage gate is already enforced; no CI workflow edits are required.

The implementer can open `ManufactureOrderExtensionsTests.cs`, write the tests, and run `dotnet test` immediately.