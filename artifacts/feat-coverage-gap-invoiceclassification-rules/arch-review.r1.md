```markdown
# Architecture Review: Unit Test Coverage for Invoice Classification Rules

## Skip Design: true

Backend-only test code addition. No UI, no schema, no public APIs, no production code changes. Pure additive coverage of existing domain logic and one application-layer orchestrator.

## Architectural Fit Assessment

The proposal aligns cleanly with existing conventions:

- **Test project layout** mirrors `src/` already: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/` matches `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/` and `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/`. Adding `Rules/` and `Services/` subfolders extends the mirror, it does not invent a new structure.
- **Tooling** (xUnit + FluentAssertions + Moq, AAA comments, `MethodName_Scenario_ExpectedOutcome`) is already established in `GetClassificationRuleTypesHandlerTests.cs` — the new tests inherit it without amendment.
- **Targets are pure functions** with no infrastructure dependencies. `IClassificationRule` is a stateless strategy interface; `RuleEvaluationEngine` takes `IEnumerable<IClassificationRule>` via constructor and a `List<ClassificationRule>` per call. No `DateTime.UtcNow`, no I/O, no statics — fully deterministic and parallel-safe.
- **Integration points** stay untouched. The existing `ClassifyInvoicesHandlerTests` and `InvoiceClassificationServiceTests` exercise the rules through the full pipeline; the new tests sit *below* them at the unit level. Adding unit coverage does not duplicate or compete with the integration suite — it pinpoints regressions to specific branches the integration tests cannot localize.

Verified facts that constrain the design:

- `ClassificationRule` (line 33–53 of `ClassificationRule.cs`) has a **public constructor** plus a public `SetOrder(int)` method. The spec is correct: the entity can be constructed directly without reflection or test-only factory hacks.
- `RuleEvaluationEngine.EvaluateRule` (line 27–31) uses `FirstOrDefault` against `Identifier`, returning `false` for unknown identifiers — the no-throw behavior the spec asserts.
- `AmountClassificationRule` wraps the operator dispatch in a `try { … } catch { return false; }`. The `decimal.TryParse` calls *never throw*, so the `catch` block is effectively unreachable through public inputs. The spec does not require covering it explicitly; documenting this in the architecture review is enough.
- Each regex rule catches **only `ArgumentException`** from `Regex.IsMatch`. Tests must use a pattern guaranteed to raise `ArgumentException` (unclosed character class `"["` is correct; other malformed regexes raise different exception types and would not exercise the fallback).

No friction. Proceed.

## Proposed Architecture

### Component Overview

```
backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/
├── Rules/                                          (new — domain-layer unit tests)
│   ├── AmountClassificationRuleTests.cs            → covers AmountClassificationRule
│   ├── CompanyNameClassificationRuleTests.cs       → covers CompanyNameClassificationRule
│   ├── DescriptionClassificationRuleTests.cs       → covers DescriptionClassificationRule
│   └── ItemDescriptionClassificationRuleTests.cs   → covers ItemDescriptionClassificationRule
├── Services/                                       (new — application-layer unit tests)
│   └── RuleEvaluationEngineTests.cs                → covers RuleEvaluationEngine
└── TestHelpers/                                    (new — shared, test-internal)
    └── InvoiceClassificationFixtures.cs            → CreateInvoice(...), CreateRule(...), CreateItemizedInvoice(...)
```

Test-side fixture helpers live in `TestHelpers/` so each test class stays focused on Arrange/Act/Assert rather than object construction. The helpers are `internal static` — not consumed by production code, not exposed beyond the test assembly.

### Key Design Decisions

#### Decision 1: Pure-function tests for rule classes, no mocks
**Options considered:**
- (A) Mock `ReceivedInvoice` / `ReceivedInvoiceItem` via Moq.
- (B) Construct real `ReceivedInvoice` instances directly in tests.

**Chosen approach:** (B). The domain entities are POCOs with public setters; constructing them is one line.

**Rationale:** Mocking POCOs introduces friction without benefit. The rule code reads concrete properties (`invoice.TotalAmount`, `invoice.CompanyName`, `invoice.Items[i].Name`); real instances exercise the same code path and produce more readable Arrange blocks. This mirrors the spec's own NFR-4 (helper methods for readability).

#### Decision 2: Mock `IClassificationRule` strategies in `RuleEvaluationEngineTests`, construct real `ClassificationRule` domain entities
**Options considered:**
- (A) Use real `AmountClassificationRule` etc. in `RuleEvaluationEngineTests` and craft patterns that match/don't match.
- (B) Mock `IClassificationRule` to control match outcomes deterministically; keep `ClassificationRule` (the data record) real.

**Chosen approach:** (B), as the spec already prescribes.

**Rationale:** Engine tests verify **ordering, filtering, short-circuit, and null-on-miss** — not rule semantics. Mocking the strategy decouples engine behavior from any future rule changes. Conversely, `ClassificationRule` is the data carrier the engine sorts and filters; substituting a mock would obscure what the engine actually inspects (`Order`, `IsActive`, `RuleTypeIdentifier`).

#### Decision 3: `[Theory]` + `[InlineData]` for `AmountClassificationRuleTests`, `[Fact]` elsewhere
**Options considered:**
- (A) One `[Fact]` per operator boundary, twelve+ methods.
- (B) `[Theory]` parameterized by `(pattern, amount, expected)` for operator coverage; `[Fact]` for the null/empty/whitespace/parse-failure edges where the scenario name carries semantic meaning.

**Chosen approach:** (B). The spec explicitly endorses this.

**Rationale:** `[Theory]` collapses ~18 nearly identical boundary cases into a single readable matrix. Edge cases (empty pattern, non-numeric body) get their own `[Fact]` because the *name* documents the contract and there is no parameter sweep to perform.

#### Decision 4: Fallback test uses `"["` (unclosed character class)
**Options considered:**
- (A) Use a guaranteed-invalid regex like `"["`.
- (B) Use `"\\"` or another regex with implementation-dependent behavior.

**Chosen approach:** (A).

**Rationale:** `Regex.IsMatch("anything", "[", …)` reliably throws `ArgumentException` on .NET, which is the **only** exception type the production code catches. Other malformed patterns may throw `RegexParseException` (which inherits from `ArgumentException` on .NET 7+; this is a stable contract, but `"["` is the clearest example). Document this in the test fixture's comment so the rationale survives future readers.

#### Decision 5: No new helper for `ClassificationRule` construction beyond `SetOrder`
**Options considered:**
- (A) Introduce a test-side builder.
- (B) Use the public constructor + `SetOrder` directly, optionally behind a small helper method `CreateRule(identifier, pattern, order, isActive)`.

**Chosen approach:** (B).

**Rationale:** The constructor has six parameters but most carry harmless defaults (`name`, `accountingTemplateCode`, `department`, `createdBy`). A 4-line helper in `InvoiceClassificationFixtures.cs` is enough; a fluent builder is overkill at this scale (YAGNI).

## Implementation Guidance

### Directory / Module Structure

Create exactly these files. Do not introduce a builder class, a base test class, or shared `[CollectionDefinition]` fixtures — none are warranted.

```
backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/
├── AmountClassificationRuleTests.cs
├── CompanyNameClassificationRuleTests.cs
├── DescriptionClassificationRuleTests.cs
└── ItemDescriptionClassificationRuleTests.cs

backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/
└── RuleEvaluationEngineTests.cs

backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/
└── InvoiceClassificationFixtures.cs
```

Namespaces:
- `Anela.Heblo.Tests.Features.InvoiceClassification.Rules`
- `Anela.Heblo.Tests.Features.InvoiceClassification.Services`
- `Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers`

### Interfaces and Contracts

The test code consumes existing public APIs only. No new contracts.

**`InvoiceClassificationFixtures.cs` — recommended surface:**

```csharp
internal static class InvoiceClassificationFixtures
{
    internal static ReceivedInvoice CreateInvoice(
        decimal totalAmount = 0m,
        string companyName = "",
        string description = "",
        params string[] itemNames);

    internal static ClassificationRule CreateRule(
        string ruleTypeIdentifier,
        string pattern,
        int order = 0,
        bool isActive = true);
}
```

`CreateRule` calls `new ClassificationRule(name: "test", ruleTypeIdentifier, pattern, accountingTemplateCode: "TEMPLATE", department: null, createdBy: "test")` then `SetOrder(order)`; if `isActive == false`, it calls `Update(...)` to flip the flag. (Inspect `Update` — it is the only public way to set `IsActive`.)

### Data Flow

**Rule test flow (per test):**
1. Arrange: instantiate `new <Rule>ClassificationRule()` + `ReceivedInvoice` via fixture.
2. Act: call `rule.Evaluate(invoice, pattern)`.
3. Assert: `result.Should().Be(...)`.

No async, no cancellation tokens, no DI container.

**Engine test flow (per test):**
1. Arrange: build `Mock<IClassificationRule>` strategies (each with a fixed `Identifier` and a stubbed `Evaluate` return value), pass them as `IEnumerable<IClassificationRule>` to `new RuleEvaluationEngine(strategies)`. Build a `List<ClassificationRule>` via the fixture.
2. Act: call `engine.FindMatchingRule(invoice, rules)`.
3. Assert: returned `ClassificationRule` reference (or `null`); for short-circuit tests, also assert `Verify(...)` was **not** called on the lower-priority mock to prove early exit.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test for `Regex.IsMatch` fallback uses a pattern that does not actually throw `ArgumentException`, leaving the fallback branch uncovered while reporting green. | Medium | Standardize on `"["` (unclosed character class) in `CompanyNameClassificationRuleTests`, `DescriptionClassificationRuleTests`, and `ItemDescriptionClassificationRuleTests`. Add a one-line comment in each fallback test explaining why this pattern is used. Optionally, a single sanity `[Fact]` in one of the rule test files can directly call `Regex.IsMatch(..., "[")` and assert it throws — guards against future framework behavior changes. |
| Cultural decimal-separator drift makes `AmountClassificationRule` tests flake on non-invariant CI hosts. | Low | Use whole-number patterns only (`"100"`, `">=100"`, `"<99"`). The production code calls `decimal.TryParse(string)` without explicit culture; whole numbers parse identically across cultures. The spec's NFR-3 already calls this out — enforce it in code review. |
| `RuleEvaluationEngine` short-circuit test relies on `OrderBy` being stable; LINQ's `OrderBy` is stable, but the engine sorts on `Order` (an int) where the test must use **distinct** Order values to make the assertion unambiguous. | Low | Use distinct `Order` values (1, 2, 3) across mocks in the short-circuit test. Document the choice in a comment. |
| `ClassificationRule.SetOrder` mutates `UpdatedAt` via `DateTime.UtcNow`. Tests must avoid asserting on `UpdatedAt` (would couple to wall-clock). | Low | Restrict assertions to `Order`, `IsActive`, `RuleTypeIdentifier`, and the returned reference identity. NFR-3 covers this. |
| Helper class drift: `InvoiceClassificationFixtures` becomes a dumping ground over time. | Low | Cap its surface at the two methods listed. If another rule type later needs a different shape, add a focused helper next to its test — do not expand the shared fixture. |
| Future addition of a new operator to `AmountClassificationRule` (e.g. `!=`) leaves the `[Theory]` matrix silently incomplete. | Low | Out of scope for this work, but add a code-review note recommending the operator list be reflected in a test enum or table when the rule changes. No action required now. |

## Specification Amendments

The spec is sound. Minor reinforcements:

1. **FR-2 / FR-3 / FR-4 — fallback test must use `"["` specifically.** Already implied by the spec's `ArgumentException` reference; promote it to an explicit requirement in the acceptance criteria to prevent future authors from picking a pattern that fails differently.
2. **FR-1 — note that the `AmountClassificationRule` `catch` block is unreachable through public inputs.** The spec lists `">=abc"` as covering "parse failure path"; clarify this exercises `decimal.TryParse` returning `false` (the early-exit path), **not** the `catch`. No test needs to target the unreachable `catch` — call this out so future maintainers do not waste time chasing 100% branch coverage.
3. **FR-5 — explicit test for "first match short-circuits."** The spec lists this; ensure the implementing developer asserts via `Mock.Verify(r => r.Evaluate(...), Times.Never)` on the **later-ordered** mock, not just by reading the returned reference. This is the only way to *prove* short-circuit behavior.
4. **FR-6 — add `TestHelpers/InvoiceClassificationFixtures.cs` to the file list.** The spec's file tree omits it; the helpers are not optional given NFR-4.
5. **NFR-1 — branch coverage of 85% on `AmountClassificationRule.cs` will not include the unreachable `catch`.** Coverage tools count unreachable branches as uncovered. If the CI gate fails on this single file at exactly the 85% threshold, the resolution is either (a) accept the lower-than-target branch coverage with a documented exclusion, or (b) raise it as a separate cleanup item to remove the dead `try/catch` from the production code. Do **not** modify production code as part of *this* task — flag and defer.

## Prerequisites

None. Everything required is already present:

- `Anela.Heblo.Tests.csproj` references xUnit, FluentAssertions, Moq.
- `IClassificationRule`, the four rule classes, `RuleEvaluationEngine`, `ClassificationRule`, `ReceivedInvoice`, and `ReceivedInvoiceItem` are all public and constructible without DI.
- Test conventions are demonstrated in `GetClassificationRuleTypesHandlerTests.cs` — use it as the style reference.
- No migrations, no config, no infrastructure changes, no new packages.

Implementation can begin immediately.
```