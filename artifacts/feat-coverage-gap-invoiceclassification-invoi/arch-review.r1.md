I have enough context. Now writing the architecture review.

```markdown
# Architecture Review: Unit Test Coverage for InvoiceClassificationService.ClassifyInvoiceAsync

## Skip Design: true

## Architectural Fit Assessment

This change is a pure test-only addition — no production code is touched, no new types are introduced, no module boundaries are crossed. It fits cleanly into the existing test architecture:

- A test class for `InvoiceClassificationService` belongs alongside the four sibling test classes in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/` (the spec's nested `Services/` subfolder does NOT match existing layout — see Amendment 1).
- The test project already references `xunit`, `Moq`, `FluentAssertions`, and `Microsoft.Extensions.Logging.Abstractions`, which is exactly the stack the spec demands. The pattern is established by `ClassifyInvoicesHandlerTests.cs`.
- The system under test depends on six interfaces, all already abstracted behind clean repository/service boundaries — no test seams need to be added. The SUT can be constructed directly from six `Mock<T>.Object` instances and a `Mock<ILogger<InvoiceClassificationService>>`.

Risk surface is essentially zero: no DB, no HTTP, no DI container. The only architectural concern is keeping the test fixtures faithful to the real types (see Amendments 1–4 — the spec contains several factual errors about the SUT's actual surface that would cause the tests to fail to compile if implemented literally).

## Proposed Architecture

### Component Overview

```
                   ┌──────────────────────────────────────────┐
                   │ InvoiceClassificationServiceTests        │
                   │  (xUnit, Moq, FluentAssertions)          │
                   └────────────────┬─────────────────────────┘
                                    │ constructs with 6 mocks
                                    ▼
         ┌──────────────────────────────────────────────┐
         │       InvoiceClassificationService (SUT)     │
         │       public ClassifyInvoiceAsync(invoice)   │
         └──┬───────┬───────┬──────────┬───────┬─────┬──┘
            │       │       │          │       │     │
   Mock<IClassif    │  Mock<IInvoice  Mock<IRule    Mock<ICurrent
   icationRule      │  Classifications Evaluation   UserService>
   Repository>      │  Client>        Engine>       │     │
                    │                               │     │
   Mock<IClassif                                    │     Mock<ILogger<
   icationHistory                                   │     InvoiceClassif
   Repository>                                      │     icationService>>
```

### Key Design Decisions

#### Decision 1: Single test class, four `[Fact]` methods (no `[Theory]`)
**Options considered:**
- (A) Four discrete `[Fact]` methods, one per outcome path.
- (B) A single `[Theory]` parameterized over the four paths.
- (C) Subclassed test fixtures per path.

**Chosen approach:** (A) — four `[Fact]` methods named per FR-1..FR-4.

**Rationale:** The four paths exercise different mock setups, different verifications, and different return-state assertions. A `[Theory]` would require either (i) hiding the setup divergence in a discriminator that obscures intent, or (ii) passing in `Action<Mocks>` delegates that fight xUnit's data-source design. Sibling test class `ClassifyInvoicesHandlerTests` uses one `[Fact]` per scenario — consistency wins.

#### Decision 2: Shared fixture in constructor; no `IClassFixture<>`
**Options considered:**
- (A) Construct fresh mocks per test in the constructor (xUnit creates a new instance per test by design).
- (B) Reuse expensive setup via `IClassFixture<>`.

**Chosen approach:** (A).

**Rationale:** Mocks are cheap; xUnit's per-test instantiation already gives isolation for free. `ClassifyInvoicesHandlerTests` follows this exact pattern (`ClassifyInvoicesHandlerTests.cs:11-31`). No reason to deviate.

#### Decision 3: Capture `ClassificationHistory` argument with `Callback` + dedicated assertion
**Options considered:**
- (A) `Verify` with an inline `It.Is<ClassificationHistory>(h => h.Result == ... && h.ErrorMessage == ...)` predicate.
- (B) `Setup(...).Callback<ClassificationHistory>(h => captured = h)` + post-act `FluentAssertions` on `captured`.

**Chosen approach:** (B) for the history-recording assertions; (A) only for simple binary "was called once with this scalar arg" checks (e.g. `MarkInvoiceForManualReviewAsync`).

**Rationale:** `ClassificationHistory` carries 11 constructor fields. An inline predicate makes test failure messages opaque ("expected at least one call matching predicate"). Capturing the object lets `FluentAssertions` print exact diffs — vital for the FR-5 requirement that "failure messages clearly identify which path's history recording broke."

#### Decision 4: Test the actual public surface, not the brief's speculative surface
**Options considered:**
- (A) Implement the spec's acceptance criteria verbatim (e.g. mock `EvaluateAsync`, verify `GetByIdAsync` on ABRA-failure path).
- (B) Test what the SUT actually does.

**Chosen approach:** (B), with the spec amended.

**Rationale:** The brief speculated about implementation details that turned out to be wrong. `IRuleEvaluationEngine` exposes `FindMatchingRule` (synchronous), not `EvaluateAsync`. The ABRA-failure path in the current code (`InvoiceClassificationService.cs:71-84`) does NOT call `GetByIdAsync` — it reuses the already-matched rule's `AccountingTemplateCode` and `Department`. Writing tests against the spec verbatim would either fail to compile or assert behavior that doesn't exist. Tests must reflect reality. See **Specification Amendments** for the corrections.

## Implementation Guidance

### Directory / Module Structure

Create exactly one file:

```
backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs
```

**Note:** The spec proposed `…/Features/InvoiceClassification/Services/InvoiceClassificationServiceTests.cs`. Reject the `Services/` nesting — the existing five test classes (`ClassifyInvoicesHandlerTests`, `ClassificationHistoryRepositoryTests`, `GetInvoiceDetailsHandlerTests`, `GetClassificationRuleTypesHandlerTests`, `InvoiceClassificationMappingProfileTests`) all live flat in the feature folder. Consistency with FR-6 outweighs the spec's path suggestion.

No new test project, no new csproj entries, no new package references — everything is already wired.

### Interfaces and Contracts

The test class must construct the SUT exactly per `InvoiceClassificationService.cs:16-30`:

```csharp
new InvoiceClassificationService(
    ruleRepositoryMock.Object,
    historyRepositoryMock.Object,
    classificationsClientMock.Object,
    ruleEngineMock.Object,
    currentUserServiceMock.Object,
    loggerMock.Object);
```

Mock surfaces to set up per test (only the methods actually invoked on the path under test):

| Mock | Methods invoked by SUT | Setup needed in which test |
|------|------------------------|----------------------------|
| `IClassificationRuleRepository` | `GetActiveRulesOrderedAsync()` | All four (returns `List<ClassificationRule>`; empty list OK for FR-1) |
| `IRuleEvaluationEngine` | `FindMatchingRule(invoice, rules)` — **synchronous** | All four |
| `IInvoiceClassificationsClient` | `MarkInvoiceForManualReviewAsync(id, reason, ct?)` | FR-1 |
| `IInvoiceClassificationsClient` | `UpdateInvoiceClassificationAsync(id, code, dept, ct?)` | FR-2, FR-3 |
| `IClassificationHistoryRepository` | `AddAsync(history)` | All four — **verified** |
| `ICurrentUserService` | `GetCurrentUser()` → `CurrentUser(Id, Name, Email, IsAuthenticated)` | All four (must return non-null `CurrentUser` with non-null `Name`, since `ClassificationHistory` ctor throws on null `processedBy`) |
| `ILogger<InvoiceClassificationService>` | `LogError(ex, message, args)` | Used only in FR-4; no verification required (logging is a side-channel, not behavior) |

### Data Flow

The SUT's four paths, exactly as implemented today (`InvoiceClassificationService.cs:32-100`):

```
ClassifyInvoiceAsync(invoice)
  │
  ├─ _currentUserService.GetCurrentUser()
  ├─ try
  │   ├─ rules = await _ruleRepository.GetActiveRulesOrderedAsync()
  │   ├─ matchedRule = _ruleEngine.FindMatchingRule(invoice, rules)
  │   │
  │   ├─ [FR-1] matchedRule == null
  │   │   ├─ RecordClassificationHistory(…, ManualReviewRequired, …, "No matching rule found", …)
  │   │   ├─ MarkInvoiceForManualReviewAsync(invoice.InvoiceNumber, "No matching classification rule")
  │   │   └─ return { Result = ManualReviewRequired }
  │   │
  │   ├─ success = await UpdateInvoiceClassificationAsync(…)
  │   │
  │   ├─ [FR-2] success == true
  │   │   ├─ RecordClassificationHistory(…, Success, AccountingTemplateCode, Department, null, …)
  │   │   └─ return { Success, RuleId, AccountingTemplateCode, Department }
  │   │
  │   └─ [FR-3] success == false
  │       ├─ RecordClassificationHistory(…, Error, AccountingTemplateCode, Department,
  │       │                              "Failed to update invoice classification in ABRA", …)
  │       └─ return { Error, RuleId, Department, ErrorMessage }
  │           // NOTE: AccountingTemplateCode is NOT set on the returned DTO in this branch
  │
  └─ [FR-4] catch (Exception ex)
      ├─ RecordClassificationHistory(…, Error, null, null,
      │                              $"Exception during classification: {ex.Message}", …)
      ├─ _logger.LogError(ex, "Error classifying invoice {InvoiceId}", …)
      └─ return { Error, ErrorMessage = "Exception during classification: {ex.Message}" }
```

### Test fixture sketch (illustrative, not prescriptive)

```csharp
public class InvoiceClassificationServiceTests
{
    private readonly Mock<IClassificationRuleRepository> _ruleRepo = new();
    private readonly Mock<IClassificationHistoryRepository> _historyRepo = new();
    private readonly Mock<IInvoiceClassificationsClient> _classificationsClient = new();
    private readonly Mock<IRuleEvaluationEngine> _ruleEngine = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<ILogger<InvoiceClassificationService>> _logger = new();
    private readonly InvoiceClassificationService _sut;

    public InvoiceClassificationServiceTests()
    {
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "test-user", "u@test", true));
        _ruleRepo.Setup(x => x.GetActiveRulesOrderedAsync())
            .ReturnsAsync(new List<ClassificationRule>());

        _sut = new InvoiceClassificationService(
            _ruleRepo.Object, _historyRepo.Object, _classificationsClient.Object,
            _ruleEngine.Object, _currentUser.Object, _logger.Object);
    }
    // …four [Fact] methods
}
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec contains factual errors about SUT surface (`EvaluateAsync`, `GetByIdAsync` on ABRA-fail path, `ClassificationHistoryStatus` enum name) | High | See Specification Amendments — implement against actual code, not spec text. |
| Tests pass today but break the moment someone refactors the SUT (e.g. extracts a helper) | Low | Tests must assert on observable behavior (return DTO, mock invocations), not on internal call ordering. Avoid `MockSequence` / `Verifiable` chains. |
| `CurrentUser.Name` is nullable (`string?`); a default mock returning `new CurrentUser(null, null, null, false)` will trigger `ArgumentNullException` in the `ClassificationHistory` constructor | Medium | Always stub `GetCurrentUser` with a non-null `Name` in the test fixture constructor (shown in sketch above). |
| Constructing `ReceivedInvoice` test data risks omitting required fields (`InvoiceNumber`, `CompanyName`, `Description` are non-null) | Low | Use a small private helper `CreateInvoice(string? id = "INV-001")` that fills all non-null fields. Mirrors how `ClassifyInvoicesHandlerTests.cs:45` does it inline. |
| Tests over-specify `CancellationToken` parameter on `IInvoiceClassificationsClient` calls (the interface accepts optional `CancellationToken?`, the SUT passes none) | Low | Use `It.IsAny<CancellationToken?>()` in `Verify`, or omit the parameter argument matcher entirely and verify just the required args. |
| FR-3 expects "test verifies `GetByIdAsync` is called" but the current SUT does NOT call it on the ABRA-failure path | High | Remove that assertion from FR-3 (see Amendment 3). If the team wants this lookup, that's a SUT change — out of scope per the spec. |

## Specification Amendments

The following amendments are required because the spec was drafted from the brief without verifying SUT source. They are corrections, not scope changes.

**Amendment 1 — File path correction (FR-6 / API/Interface Design section):**
- **Spec says:** `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/InvoiceClassificationServiceTests.cs`
- **Should be:** `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`
- **Why:** All existing sibling test classes are flat under `Features/InvoiceClassification/`, not nested under `Services/`. The spec's own FR-6 demands matching existing conventions.

**Amendment 2 — Method name correction (FR-1 acceptance criteria):**
- **Spec says:** `Mocked IRuleEvaluationEngine.EvaluateAsync returns "no match"`
- **Should be:** `Mocked IRuleEvaluationEngine.FindMatchingRule(invoice, rules) returns null`
- **Why:** The actual interface (`IRuleEvaluationEngine.cs:7`) defines `ClassificationRule? FindMatchingRule(ReceivedInvoice invoice, List<ClassificationRule> rules)` — synchronous, not async. No `EvaluateAsync` method exists.

**Amendment 3 — Remove `GetByIdAsync` assertion from FR-3:**
- **Spec says:** `IClassificationRuleRepository.GetByIdAsync is verified called with the matched rule's ID.`
- **Should be:** *Remove this acceptance criterion.*
- **Why:** The current SUT (`InvoiceClassificationService.cs:71-84`) does not call `GetByIdAsync` on the ABRA-failure path. It reuses `matchedRule.AccountingTemplateCode` and `matchedRule.Department` from the already-matched rule. Implementing the assertion would fail. (The brief speculated about this; the spec carried the speculation forward without verifying.)

**Amendment 4 — Enum name correction (Data Model section, FR-1..FR-4 acceptance criteria):**
- **Spec says:** `ClassificationHistoryStatus.Success`, `…Error`, `…ManualReviewRequired`
- **Should be:** `ClassificationResult.Success`, `…Error`, `…ManualReviewRequired`
- **Why:** The actual enum (`ClassificationResult.cs:3`) is named `ClassificationResult`. No `ClassificationHistoryStatus` type exists. Both the SUT and `ClassificationHistory` use `ClassificationResult`.

**Amendment 5 — Repository method correction (FR-1 acceptance criteria):**
- **Spec implies:** `IClassificationHistoryRepository.AddAsync (or whichever method RecordClassificationHistory delegates to)`
- **Confirmed:** `AddAsync(ClassificationHistory history)` — see `IClassificationHistoryRepository.cs:5`. Tests should assert on this exact method.

**Amendment 6 — Returned DTO field expectations (FR-3 acceptance criteria, "rule's values"):**
- **Note for implementer:** On the ABRA-failure path, the returned `InvoiceClassificationResult` sets `RuleId`, `Department`, and `ErrorMessage` — but **not** `AccountingTemplateCode`. FR-2 sets all four. Test assertions must reflect this asymmetry (`InvoiceClassificationService.cs:77-83`).

## Prerequisites

None. The test project, all required NuGet packages (`xunit`, `Moq`, `FluentAssertions`), the SUT, and all six dependency interfaces already exist. The implementer can create the file and start writing tests immediately. `dotnet build` and `dotnet test` from `backend/test/Anela.Heblo.Tests/` are the only commands needed.
```