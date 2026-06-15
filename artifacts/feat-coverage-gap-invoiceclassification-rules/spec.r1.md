# Specification: Unit Test Coverage for Invoice Classification Rules

## Summary
Add comprehensive xUnit unit tests for the four invoice classification rule implementations (`AmountClassificationRule`, `CompanyNameClassificationRule`, `DescriptionClassificationRule`, `ItemDescriptionClassificationRule`) and the `RuleEvaluationEngine` that orchestrates them. All five files are currently below the 60% coverage filter threshold; the rule files sit at 0% and the engine at 23.5%. These rules drive automated invoice classification, so silent regressions misclassify invoices or bypass manual review.

## Background
The invoice classification subsystem matches an incoming `ReceivedInvoice` against an ordered list of `ClassificationRule` records. Each rule is keyed to one of the `IClassificationRule` implementations by `RuleTypeIdentifier`, which evaluates the rule's `Pattern` against the invoice. The `RuleEvaluationEngine.FindMatchingRule` method returns the first matching active rule (ordered by `Order`) or `null` when no rule matches — `null` is the signal that an invoice should enter manual review.

The rule logic is pure domain code with no dependencies: every branch is reachable through deterministic inputs. The coverage gap is therefore cheap to close and high-value, because:
- `AmountClassificationRule` has six independent operator branches (`>=`, `<=`, `>`, `<`, `=`, bare literal). A regression in any single branch silently misclassifies a subset of invoices.
- Three rules (`CompanyName`, `Description`, `ItemDescription`) use a regex match with a `Contains` fallback when the pattern is not a valid regex. The fallback path is never exercised, so a change in regex semantics or fallback handling would not be caught.
- `RuleEvaluationEngine` ordering (`OrderBy(r => r.Order)`) and `IsActive` filtering are critical to predictable rule precedence; the no-match `null` return controls manual review routing.

Existing tests under `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/` use xUnit + FluentAssertions + Moq with AAA structure. New tests must match that style.

## Functional Requirements

### FR-1: AmountClassificationRule — operator coverage
Add a dedicated test class `AmountClassificationRuleTests` covering each comparison operator branch on `TotalAmount`.

**Acceptance criteria:**
- Test `>=` operator: returns `true` when amount equals threshold; returns `true` when amount exceeds threshold; returns `false` when amount is below threshold.
- Test `<=` operator: returns `true` when amount equals threshold; returns `true` when amount is below threshold; returns `false` when amount exceeds threshold.
- Test `>` operator: returns `true` when amount strictly exceeds threshold; returns `false` when amount equals threshold; returns `false` when amount is below threshold.
- Test `<` operator: returns `true` when amount strictly below threshold; returns `false` when amount equals threshold; returns `false` when amount exceeds threshold.
- Test `=` operator: returns `true` when amount equals threshold; returns `false` when amount differs by any amount.
- Test bare numeric pattern (no operator prefix): returns `true` when amount equals the literal; returns `false` otherwise. The bare-literal branch is semantically equivalent to `=`.
- Test empty pattern returns `false`.
- Test whitespace-only pattern (`"   "`) returns `false`.
- Test pattern with operator but non-numeric body (e.g. `">=abc"`) returns `false` (parse failure path).
- Test bare non-numeric pattern (e.g. `"abc"`) returns `false`.
- Each operator boundary test uses unambiguous values (e.g. amount `100m`, threshold `100m` / `99m` / `101m`) to keep equality semantics for `decimal` explicit.
- `[Theory]` with `[InlineData]` is preferred where the operator and expected outcome are the only varying inputs.

### FR-2: CompanyNameClassificationRule — regex and fallback coverage
Add `CompanyNameClassificationRuleTests` covering both the regex path and the invalid-regex `Contains` fallback.

**Acceptance criteria:**
- Returns `true` when the regex pattern matches the company name (e.g. pattern `"ACME"` against `"ACME s.r.o."`).
- Returns `true` for case-insensitive match (e.g. pattern `"acme"` matches `"ACME s.r.o."`).
- Returns `false` when a valid regex does not match.
- Returns `true` when the pattern is an invalid regex but is a substring of the company name (fallback path, e.g. pattern `"["` against `"Company [old]"`).
- Returns `false` when the pattern is an invalid regex and not a substring (fallback miss).
- Returns `false` when `invoice.CompanyName` is `null` or empty or whitespace.
- Returns `false` when `pattern` is `null` or empty or whitespace.
- The fallback test must use a pattern that genuinely throws `ArgumentException` from `Regex.IsMatch` (e.g. an unclosed character class `"["`).

### FR-3: DescriptionClassificationRule — regex and fallback coverage
Add `DescriptionClassificationRuleTests` covering the same matrix as FR-2 but against `invoice.Description`.

**Acceptance criteria:**
- Regex match hit and miss with case-insensitive semantics.
- Invalid-regex fallback hit and miss against `invoice.Description`.
- Returns `false` for null/empty/whitespace description.
- Returns `false` for null/empty/whitespace pattern.

### FR-4: ItemDescriptionClassificationRule — items iteration coverage
Add `ItemDescriptionClassificationRuleTests` covering iteration over `invoice.Items` and per-item evaluation.

**Acceptance criteria:**
- Returns `true` when the first item's `Name` matches the pattern.
- Returns `true` when only a non-first item's `Name` matches (verifies `Any` continues past non-matches).
- Returns `false` when no item names match.
- Returns `false` when `invoice.Items` is empty.
- Returns `false` when all item names are null/empty/whitespace.
- Returns `false` when pattern is null/empty/whitespace.
- Case-insensitive regex match works against item names.
- Invalid-regex fallback evaluates per item with `Contains` (e.g. pattern `"["` matches an item name containing `"["`).
- A single item with null/empty `Name` does not cause the evaluation to throw; non-matching items are silently skipped.

### FR-5: RuleEvaluationEngine — ordering and no-match coverage
Add `RuleEvaluationEngineTests` in the application test project covering ordering, active-rule filtering, and the `null` no-match path.

**Acceptance criteria:**
- Returns the matching rule with the lowest `Order` value when multiple rules match.
- Skips inactive rules (`IsActive == false`) even when they would otherwise match.
- Returns `null` when no active rule matches.
- Returns `null` when the rules list is empty.
- Returns `null` when the rule references a `RuleTypeIdentifier` that does not correspond to any registered `IClassificationRule` (the engine should not throw on an unknown identifier).
- Returns the matching rule even when a higher-`Order` rule was inserted into the list first (verifies sort behavior, not list insertion order).
- The first matching rule (after filtering inactive and sorting by `Order`) short-circuits subsequent evaluations.
- Tests use `Mock<IClassificationRule>` for the rule strategies registered in the engine so behavior is deterministic and not coupled to the real rule implementations. `ClassificationRule` domain entities are constructed directly (not mocked) using its public constructor and `SetOrder`.

### FR-6: Test organization and conventions
All new tests follow the existing project's test conventions.

**Acceptance criteria:**
- Rule tests live under `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/` (new subfolder) — one `<RuleName>Tests.cs` file per rule.
- `RuleEvaluationEngineTests.cs` lives under `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/` (new subfolder).
- Namespace mirrors source: `Anela.Heblo.Tests.Features.InvoiceClassification.Rules` and `Anela.Heblo.Tests.Features.InvoiceClassification.Services`.
- Test framework: xUnit (`[Fact]`, `[Theory]`/`[InlineData]`).
- Assertions: FluentAssertions.
- Mocking: Moq (only where mocks are warranted — see FR-5; rule tests need no mocks since the rules are pure functions).
- AAA structure: each test has explicit `// Arrange`, `// Act`, `// Assert` comments matching `GetClassificationRuleTypesHandlerTests.cs` style.
- Test method naming: `MethodName_Scenario_ExpectedOutcome` (e.g. `Evaluate_AmountEqualsThreshold_ReturnsTrueForGreaterOrEqual`).
- No production code is modified for the purpose of testability — all behavior described above is already reachable through public APIs.

## Non-Functional Requirements

### NFR-1: Coverage targets
- Each of the four rule files reaches ≥ 90% line coverage and ≥ 85% branch coverage.
- `RuleEvaluationEngine.cs` reaches ≥ 90% line coverage.
- The five files no longer appear under the 60% coverage filter in the next CI run.

### NFR-2: Test performance
- The full new test set executes in under 1 second locally (pure in-memory unit tests, no I/O, no shared fixtures required).

### NFR-3: Determinism
- No reliance on `DateTime.UtcNow`, random values, environment, or culture. Decimal parsing uses the invariant behavior the production code uses; tests document this by using whole-number test values to avoid culture-sensitive separators.

### NFR-4: Maintainability
- Helper methods (e.g. `CreateInvoice(decimal amount)`, `CreateInvoiceWithItems(params string[] itemNames)`) keep test arrangement readable and avoid copy-paste.
- No production-code behavior changes are introduced.

## Data Model
No data model changes. Tests exercise existing domain entities:
- `ReceivedInvoice` — properties used: `CompanyName`, `Description`, `TotalAmount`, `Items`.
- `ReceivedInvoiceItem` — property used: `Name`.
- `ClassificationRule` — properties used: `RuleTypeIdentifier`, `Pattern`, `Order`, `IsActive`. Constructed via public constructor + `SetOrder(int)`.
- `IClassificationRule` — the rule strategy interface.

## API / Interface Design
No public API changes. The work is purely additive test code under the test project tree:

```
backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/
├── Rules/
│   ├── AmountClassificationRuleTests.cs            (new)
│   ├── CompanyNameClassificationRuleTests.cs       (new)
│   ├── DescriptionClassificationRuleTests.cs       (new)
│   └── ItemDescriptionClassificationRuleTests.cs   (new)
└── Services/
    └── RuleEvaluationEngineTests.cs                (new)
```

## Dependencies
- xUnit (already referenced by `Anela.Heblo.Tests.csproj`).
- FluentAssertions (already referenced).
- Moq (already referenced — used for FR-5 only).
- No new NuGet packages required.

## Out of Scope
- Modifying production rule code (refactoring, adding new operators, changing fallback behavior).
- Adding new classification rule types beyond the four already implemented.
- Adding tests for `VatClassificationRule` (already covered or not flagged by the coverage gap routine).
- Integration tests covering the full classification pipeline through HTTP/MediatR — those already exist (`ClassifyInvoicesHandlerTests.cs`, `InvoiceClassificationServiceTests.cs`).
- Mutation testing or property-based testing.
- Performance benchmarks.

## Open Questions
None.

## Status: COMPLETE
