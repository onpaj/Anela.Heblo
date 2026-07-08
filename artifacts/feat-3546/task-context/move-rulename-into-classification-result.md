### task: move-rulename-into-classification-result

## Goal
Add `RuleName` to `InvoiceClassificationResult`, populate it inside `InvoiceClassificationService.ClassifyInvoiceAsync`
wherever `matchedRule` is already in scope, and remove `ClassifyInvoicesHandler`'s now-unnecessary
`IClassificationRuleRepository` dependency (and the per-error DB round trip it caused) by reading
`result.RuleName` directly instead. Update the two affected unit test files to match.

This eliminates one `IClassificationRuleRepository.GetByIdAsync` call per errored invoice inside
`ClassifyInvoicesHandler.Handle`'s loop, and removes a repository dependency that the handler only used
for that lookup.

## Files to touch

1. `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationResult.cs`
2. `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`
3. `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`
4. `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`
5. `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`

No other files change. No DI registration changes are needed: `IClassificationRuleRepository` stays
registered in `InvoiceClassificationModule.cs` for its other consumers (`InvoiceClassificationService`
itself, plus `UpdateClassificationRuleHandler`, `ReorderClassificationRulesHandler`,
`GetClassificationRulesHandler`, `DeleteClassificationRuleHandler`, `CreateClassificationRuleHandler`).
No OpenAPI/TypeScript client regeneration is triggered — nothing HTTP-facing changes.

## Step 1 — Add `RuleName` to `InvoiceClassificationResult`

File: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationResult.cs`

Current content (6 properties on lines 5–16):
```csharp
public class InvoiceClassificationResult
{
    public ClassificationResult Result { get; set; }

    public Guid? RuleId { get; set; }

    public string? AccountingTemplateCode { get; set; }

    public string? Department { get; set; }

    public string? ErrorMessage { get; set; }
}
```

Add a settable auto-property `RuleName` immediately after `RuleId`:
```csharp
public class InvoiceClassificationResult
{
    public ClassificationResult Result { get; set; }

    public Guid? RuleId { get; set; }

    public string? RuleName { get; set; }

    public string? AccountingTemplateCode { get; set; }

    public string? Department { get; set; }

    public string? ErrorMessage { get; set; }
}
```

This class is populated exclusively via object initializers at all four construction sites in
`InvoiceClassificationService.cs` — it has no constructor, so `RuleName` must stay a plain auto-property,
not a constructor parameter (otherwise the other three sites fail to compile).

## Step 2 — Populate `RuleName` in `InvoiceClassificationService.ClassifyInvoiceAsync`

File: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`

`ClassifyInvoiceAsync` (lines 28–94) constructs `InvoiceClassificationResult` at 4 sites. `matchedRule`
(type `ClassificationRule`, has `.Name`) is in scope at 2 of them:

- **Success branch** (currently lines 57–63): add `RuleName = matchedRule.Name` alongside the existing
  `RuleId = matchedRule.Id`:
  ```csharp
  return new InvoiceClassificationResult
  {
      Result = ClassificationResult.Success,
      RuleId = matchedRule.Id,
      RuleName = matchedRule.Name,
      AccountingTemplateCode = matchedRule.AccountingTemplateCode,
      Department = matchedRule.Department
  };
  ```

- **ABRA-update-failed `Error` branch** (currently lines 71–77): same addition:
  ```csharp
  return new InvoiceClassificationResult
  {
      Result = ClassificationResult.Error,
      RuleId = matchedRule.Id,
      RuleName = matchedRule.Name,
      Department = matchedRule.Department,
      ErrorMessage = errorMessage
  };
  ```

Leave the other two construction sites unchanged — `matchedRule` is not in scope there, so `RuleName`
(like `RuleId` today) stays unset/null:
- `ManualReviewRequired` branch (currently lines 43–46, `matchedRule == null`) — no change.
- Outer-exception `Error` branch (currently lines 88–92, inside the `catch (Exception ex)` block) — no change.

Do not touch `RecordClassificationHistory` — it takes `ruleId`, not `ruleName`, and stays exactly as is
per the spec's explicit out-of-scope note (`ClassificationHistory` persistence is unaffected).

## Step 3 — Remove `IClassificationRuleRepository` from `ClassifyInvoicesHandler` and use `result.RuleName`

File: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`

Current constructor and field (lines 9–29):
```csharp
public class ClassifyInvoicesHandler : IRequestHandler<ClassifyInvoicesRequest, ClassifyInvoicesResponse>
{
    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly IInvoiceClassificationService _classificationService;
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ClassifyInvoicesHandler> _logger;

    public ClassifyInvoicesHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService classificationService,
        IClassificationRuleRepository ruleRepository,
        ICurrentUserService currentUserService,
        ILogger<ClassifyInvoicesHandler> logger)
    {
        _invoicesClient = invoicesClient;
        _classificationService = classificationService;
        _ruleRepository = ruleRepository;
        _currentUserService = currentUserService;
        _logger = logger;
    }
```

Change to (4 params, no repository):
```csharp
public class ClassifyInvoicesHandler : IRequestHandler<ClassifyInvoicesRequest, ClassifyInvoicesResponse>
{
    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly IInvoiceClassificationService _classificationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ClassifyInvoicesHandler> _logger;

    public ClassifyInvoicesHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService classificationService,
        ICurrentUserService currentUserService,
        ILogger<ClassifyInvoicesHandler> logger)
    {
        _invoicesClient = invoicesClient;
        _classificationService = classificationService;
        _currentUserService = currentUserService;
        _logger = logger;
    }
```

Then in `Handle`, the `ClassificationResult.Error` case currently (lines 91–107):
```csharp
                        case ClassificationResult.Error:
                            response.Errors++;
                            if (!string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                // Add rule name to error message if available
                                var errorMessage = $"Invoice {invoice.InvoiceNumber}: {result.ErrorMessage}";
                                if (result.RuleId.HasValue)
                                {
                                    var rule = await _ruleRepository.GetByIdAsync(result.RuleId.Value);
                                    if (rule != null)
                                    {
                                        errorMessage = $"Invoice {invoice.InvoiceNumber} (Rule: {rule.Name}): {result.ErrorMessage}";
                                    }
                                }
                                errorMessages.Add(errorMessage);
                            }
                            break;
```

Replace with a synchronous check on `result.RuleName` — no repository call, no `await`:
```csharp
                        case ClassificationResult.Error:
                            response.Errors++;
                            if (!string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                // Add rule name to error message if available
                                var errorMessage = !string.IsNullOrEmpty(result.RuleName)
                                    ? $"Invoice {invoice.InvoiceNumber} (Rule: {result.RuleName}): {result.ErrorMessage}"
                                    : $"Invoice {invoice.InvoiceNumber}: {result.ErrorMessage}";
                                errorMessages.Add(errorMessage);
                            }
                            break;
```

Message format/shape is unchanged — only the data source changes (`result.RuleName` instead of a repo
lookup keyed by `result.RuleId`).

Leave the `using Anela.Heblo.Domain.Features.InvoiceClassification;` import in place — `ReceivedInvoice`
and `ClassificationResult` (the enum) still live in that namespace and are still used elsewhere in this
file; only the `IClassificationRuleRepository` symbol becomes unused, not the whole namespace.

After this change the constructor is exactly:
`(IReceivedInvoicesClient, IInvoiceClassificationService, ICurrentUserService, ILogger<ClassifyInvoicesHandler>)`
— 4 parameters, no `IClassificationRuleRepository` reference anywhere in the file.

No change needed to `InvoiceClassificationModule.cs` — MediatR auto-registers `ClassifyInvoicesHandler`
via its handler interface; there is no explicit constructor wiring for it to update, and
`IClassificationRuleRepository`'s registration is untouched (still needed by other consumers).

## Step 4 — Update `ClassifyInvoicesHandlerTests.cs`

File: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`

- Remove the `Mock<IClassificationRuleRepository> _ruleRepositoryMock` field (line 16) and its
  instantiation `_ruleRepositoryMock = new Mock<IClassificationRuleRepository>();` (line 25).
- Remove `_ruleRepositoryMock.Object,` from the `ClassifyInvoicesHandler` constructor call in the test
  constructor (currently lines 33–38), so it becomes:
  ```csharp
  _handler = new ClassifyInvoicesHandler(
      _invoicesClientMock.Object,
      _classificationServiceMock.Object,
      _currentUserServiceMock.Object,
      _loggerMock.Object);
  ```
- The `using Anela.Heblo.Domain.Features.InvoiceClassification;` import (line 3) stays — `ReceivedInvoice`,
  `ClassificationResult`, and `IClassificationRuleRepository` (still referenced in type position for the
  removed mock, so re-check after edits: once the mock field is gone, confirm no other symbol from that
  namespace requires the import to be dropped — `ReceivedInvoice` at minimum still needs it, e.g. line 53).
  In practice: leave the `using` alone; only remove the two `Mock<IClassificationRuleRepository>` lines and
  the constructor argument.
- The existing 5 tests construct `InvoiceClassificationResult` without `RuleName`/`RuleId` (e.g. lines 63,
  97, 126, 155, 184) — these remain valid unchanged, since `RuleName` is optional and defaults to null.
- Add two new tests covering FR-3 from the spec, exercising the `ClassificationResult.Error` branch
  (which none of the current 5 tests do):
  1. **`Handle_WhenErrorResultHasRuleName_IncludesRuleNameInErrorMessage`**: set up
     `_classificationServiceMock` to return
     `new InvoiceClassificationResult { Result = ClassificationResult.Error, RuleId = someGuid, RuleName = "My Rule", ErrorMessage = "boom" }`
     for one invoice (use the `InvoiceIds`-driven single-invoice path, mirroring the existing
     `GetInvoiceByIdAsync` setup pattern, or the unclassified-invoices path — either is fine as long as
     exactly one invoice with a known `InvoiceNumber` flows through). Assert
     `response.ErrorMessages` contains a message equal to
     `$"Invoice {invoiceNumber} (Rule: My Rule): boom"`. Also assert `_ruleRepositoryMock` no longer
     exists (i.e. this compiles without any repository mock in scope) — the absence of the mock is itself
     the proof no DB call is made.
  2. **`Handle_WhenErrorResultHasNoRuleName_OmitsRuleSegmentFromErrorMessage`**: same shape but
     `RuleName = null` (and optionally `RuleId = null`, matching the exception/no-match path). Assert
     `response.ErrorMessages` contains `$"Invoice {invoiceNumber}: boom"` with no `(Rule: ...)` segment.
- Run `grep -n "_ruleRepositoryMock" backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`
  after editing — it must return no matches.

## Step 5 — Update `InvoiceClassificationServiceTests.cs`

File: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`

Add `RuleName` assertions alongside the existing `RuleId` assertions in the tests that already exercise
`matchedRule`-in-scope branches:

- `ClassifyInvoiceAsync_RuleMatchedAndAbraSucceeds_RecordsSuccessAndReturnsRuleResult` (Success branch):
  after the existing `result.RuleId.Should().Be(ruleWithId.Id);` (line 148), add:
  ```csharp
  result.RuleName.Should().Be(ruleWithId.Name);
  ```
- `ClassifyInvoiceAsync_RuleMatchedAndAbraFails_RecordsErrorAndReturnsRuleIdForDisplay` (ABRA-failure
  `Error` branch): after the existing `result.RuleId.Should().Be(matchedRule.Id);` (line 228), add:
  ```csharp
  result.RuleName.Should().Be(matchedRule.Name);
  ```
- `ClassifyInvoiceAsync_NoMatchingRule_MarksForManualReviewAndRecordsHistory` (no-match branch): after
  the existing `result.RuleId.Should().BeNull();` (line 69), add:
  ```csharp
  result.RuleName.Should().BeNull();
  ```
- `ClassifyInvoiceAsync_ExceptionThrown_RecordsErrorWithMessageAndReturnsErrorResult` (outer-exception
  branch): after the existing `result.RuleId.Should().BeNull();` (line 288), add:
  ```csharp
  result.RuleName.Should().BeNull();
  ```

No other changes to this file — `InvoiceClassificationService`'s constructor signature and the
`_ruleRepositoryMock` it legitimately depends on (`GetActiveRulesOrderedAsync`) are unaffected by this
refactor (the service keeps its `IClassificationRuleRepository` dependency; only the handler loses its
separate one).

## Verification

Run from the repo root of this worktree:

```bash
cd backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"
```

Both commands must succeed:
- `dotnet build` must show zero errors (confirms `ClassifyInvoicesHandler`'s new 4-arg constructor,
  `InvoiceClassificationResult.RuleName`, and both test files all compile together).
- The filtered `dotnet test` run must show all tests passing in
  `Anela.Heblo.Tests.Features.InvoiceClassification.ClassifyInvoicesHandlerTests` (7 tests: the original 5
  plus the 2 new ones) and `Anela.Heblo.Tests.Features.InvoiceClassification.InvoiceClassificationServiceTests`
  (4 tests, all with new `RuleName` assertions), with 0 failures.

If time allows, also run the full test project (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`)
to confirm no unrelated regression, though the filtered run above is the minimum bar for this task.

Do not run `dotnet format` changes beyond what the edits above require; match existing file style
(4-space indent, existing brace/blank-line conventions already visible in both handler and service files).
