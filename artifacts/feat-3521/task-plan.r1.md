# Task Plan: Move user-identity resolution out of InvoiceClassificationService (ADR-005 compliance)

## Overview
This is a small, mechanical ADR-005 convergence fix confined to three source files and two test files, all within the `InvoiceClassification` vertical slice, with no schema, contract, controller, or UI changes (per arch-review's `Skip Design: true`). The interface signature change, the service-side removal of `ICurrentUserService`, and the handler-side identity resolution are tightly coupled — the interface change breaks the service until the handler is updated to match, and vice versa — so splitting them into separate tasks would leave the codebase non-compiling between steps. This is one task.

---

### task: move-identity-resolution-to-classify-invoices-handler

**Goal:** Move `ICurrentUserService` resolution out of `InvoiceClassificationService` and into `ClassifyInvoicesHandler`, making `processedBy` an explicit parameter, so that scheduled (non-HTTP) classification runs write an accurate `ProcessedBy` value instead of relying on a service-internal HTTP-context-dependent lookup.

**Context (from spec.r1.md / arch-review.r1.md):**
- `InvoiceClassificationService.cs:13,21,28,34` — constructor takes `ICurrentUserService currentUserService` (field `_currentUserService`); `ClassifyInvoiceAsync` (line 32) calls `_currentUserService.GetCurrentUser()` at line 34 and uses `currentUser.Name` at four `RecordClassificationHistory` call sites: line 45 (no-match branch), line 61 (success branch), line 75 (ABRA-update-failure branch), line 90 (catch block).
- `IInvoiceClassificationService.cs:7` — signature is `Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice);` and must become `Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy);`.
- `ClassifyInvoicesHandler.cs` — currently has no `ICurrentUserService` dependency; `Handle` calls `_classificationService.ClassifyInvoiceAsync(invoice)` at line 72 inside the `foreach` loop starting at line 68, with no identity resolution anywhere.
- Runtime bug being fixed: `InvoiceClassificationJob` invokes `IMediator.Send(ClassifyInvoicesRequest)` hourly with no HTTP context, so today's `_currentUserService.GetCurrentUser()` call (inside the service) returns `IsAuthenticated == false`, `Name == null`, and the concrete `CurrentUserService.GetCurrentUser()` implementation falls through to `Name = "Anonymous"` — misleading audit history for automated runs.
- Precedent pattern (already correct, do not modify): `CreateClassificationRuleHandler.cs:12,14-22,26` — injects `ICurrentUserService _currentUserService` via constructor, calls `var currentUser = _currentUserService.GetCurrentUser();` once at the top of `Handle` (line 26), and passes `currentUser.Name` into the domain constructor call (line 38). `UpdateClassificationRuleHandler` follows the identical shape. `ClassifyInvoicesHandler` must mirror this.
- `CurrentUser` is `public record CurrentUser(string? Id, string? Name, string? Email, bool IsAuthenticated);` (`backend/src/Anela.Heblo.Domain/Features/Users/CurrentUser.cs`). `ICurrentUserService` (`backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`) exposes `CurrentUser GetCurrentUser()` and `bool IsInRole(string role)`.
- Fallback rule (spec FR-3 / design.r1.md): `processedBy = currentUser.IsAuthenticated ? (string.IsNullOrEmpty(currentUser.Name) ? "system" : currentUser.Name) : "system"`. Must be computed exactly once per `Handle` invocation, before the `foreach` loop, and the same value passed into every `ClassifyInvoiceAsync` call in that batch.
- `InvoiceClassificationModule.cs` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationModule.cs`) does **not** register `ICurrentUserService` today (confirmed by reading the file — it registers `IInvoiceClassificationService`, `IRuleEvaluationEngine`, repositories, and rule implementations only) — no change needed there; `ICurrentUserService` is registered once at the API composition root by `UsersModule.AddUsersModule()` and resolved via standard constructor injection in both `InvoiceClassificationService` (removed by this task) and `ClassifyInvoicesHandler` (added by this task).
- Out of scope (per spec): changing `ADR-005` itself, auditing other modules, adding identity to `ClassifyInvoicesRequest`/`ClassifyInvoicesResponse`, modifying `InvoiceClassificationJob`, or backfilling historical `ClassificationHistory.ProcessedBy = "Anonymous"` rows.

**Files to create/modify:**
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/IInvoiceClassificationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`
- No change (verify only): `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationModule.cs`

**Implementation steps:**
1. In `IInvoiceClassificationService.cs`, change the method signature to `Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy);`.
2. In `InvoiceClassificationService.cs`:
   - Remove the `ICurrentUserService _currentUserService` field (line 13), the `currentUserService` constructor parameter (line 21) and its assignment (line 28).
   - Remove the `using Anela.Heblo.Domain.Features.Users;` line (line 3) since `ICurrentUserService`/`CurrentUser` will no longer be referenced (verify no other type from that namespace is used in the file before removing).
   - Change `ClassifyInvoiceAsync(ReceivedInvoice invoice)` to `ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy)` and delete the `var currentUser = _currentUserService.GetCurrentUser();` line (line 34).
   - Replace `currentUser.Name` with `processedBy` at all four call sites (lines 45, 61, 75, 90).
3. In `ClassifyInvoicesHandler.cs`:
   - Add `using Anela.Heblo.Domain.Features.Users;`.
   - Add `private readonly ICurrentUserService _currentUserService;` field, add `ICurrentUserService currentUserService` constructor parameter (after `IClassificationRuleRepository ruleRepository`, before `logger`, matching the existing parameter-then-logger ordering convention), and assign it in the constructor body.
   - At the top of `Handle`, immediately after `response.TotalInvoicesProcessed = invoicesToClassify.Count;` (line 66) and before the `foreach` loop (line 68), add:
     ```csharp
     var currentUser = _currentUserService.GetCurrentUser();
     var processedBy = currentUser.IsAuthenticated
         ? (string.IsNullOrEmpty(currentUser.Name) ? "system" : currentUser.Name)
         : "system";
     ```
   - Change the call at line 72 from `_classificationService.ClassifyInvoiceAsync(invoice)` to `_classificationService.ClassifyInvoiceAsync(invoice, processedBy)`.
4. Open `InvoiceClassificationModule.cs` and confirm it still has no `ICurrentUserService` registration (it shouldn't need one — `ICurrentUserService` is registered at the API composition root). No edit expected; if a registration is somehow found, leave a note rather than removing it silently, since spec/arch-review both assert none exists.
5. Run `dotnet build` to catch any stale `currentUser`/`_currentUserService` references left in `InvoiceClassificationService.cs` per arch-review's identified risk.

**Testing:**
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`:
  - Remove `Mock<ICurrentUserService> _currentUserServiceMock` field and its argument from the `_sut = new InvoiceClassificationService(...)` construction (drop the `_currentUserServiceMock.Object` argument at line 28).
  - In each of the four tests (`ClassifyInvoiceAsync_NoMatchingRule_MarksForManualReviewAndRecordsHistory`, `ClassifyInvoiceAsync_RuleMatchedAndAbraSucceeds_RecordsSuccessAndReturnsRuleResult`, `ClassifyInvoiceAsync_RuleMatchedAndAbraFails_RecordsErrorAndReturnsRuleIdForDisplay`, `ClassifyInvoiceAsync_ExceptionThrown_RecordsErrorWithMessageAndReturnsErrorResult`): remove the `var currentUser = new CurrentUser(...)` arrangement and the `_currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(currentUser);` setup; introduce an explicit `var processedBy = "test-user";` (or reuse a similarly named literal per test) and call `await _sut.ClassifyInvoiceAsync(invoice, processedBy)`; change each `capturedHistory.ProcessedBy.Should().Be(currentUser.Name);` assertion to `capturedHistory.ProcessedBy.Should().Be(processedBy);`. Remove the now-unused `using Anela.Heblo.Domain.Features.Users;` import only if `CurrentUser`/`ICurrentUserService` are no longer referenced anywhere in the file.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`:
  - Add `Mock<ICurrentUserService> _currentUserServiceMock` field, instantiate it in the constructor, and pass `_currentUserServiceMock.Object` into the `ClassifyInvoicesHandler` construction (matching the new constructor parameter order). Add `using Anela.Heblo.Domain.Features.Users;`.
  - Update all three existing tests' `_classificationServiceMock.Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>()))` setups to `ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), It.IsAny<string>())` (new signature), and set up `_currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("id", "test-user", "test@test.com", true));` (or similar) in each so they keep passing.
  - Add a new test asserting the unauthenticated/background-job path: mock `GetCurrentUser()` to return a `CurrentUser` with `IsAuthenticated == false`, run `Handle`, and verify `_classificationServiceMock.Verify(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), "system"), Times.Once)` (or `Times.AtLeastOnce` depending on invoice count in that test's fixture).
  - Add a new test asserting the authenticated path: mock `GetCurrentUser()` to return `IsAuthenticated == true` with a concrete `Name` (e.g., `"jane.doe"`), run `Handle`, and verify `_classificationServiceMock.Verify(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>(), "jane.doe"), Times.Once)`.
  - Leave the existing parallelism/error-counting/fetch assertions (`Handle_WithMultipleInvoiceIds_FetchesAllInvoicesInParallel`, `Handle_WhenSomeInvoicesNotFound_CountsThemAsErrors`, `Handle_WithNoInvoiceIds_FetchesAllUnclassifiedInvoices`) otherwise unmodified aside from the mock setup/signature updates above.
- Run: `dotnet build` (from `backend/`) followed by `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"` to scope to this module's tests; then the full suite (`dotnet test`) to confirm no other module referenced the old `ClassifyInvoiceAsync(ReceivedInvoice)` overload.

**Acceptance criteria:**
- `IInvoiceClassificationService.ClassifyInvoiceAsync` has signature `Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy)` (FR-1).
- `InvoiceClassificationService` no longer has an `ICurrentUserService` constructor parameter, field, or `GetCurrentUser()` call anywhere in the file (FR-2).
- `ClassifyInvoicesHandler`'s constructor accepts `ICurrentUserService`; `Handle` calls `_currentUserService.GetCurrentUser()` exactly once per invocation, outside the `foreach` loop, and the resulting `processedBy` value is passed unchanged into every `ClassifyInvoiceAsync` call in that batch (FR-3).
- When `IsAuthenticated == false` (background-job scenario), `processedBy == "system"`; when `IsAuthenticated == true` with a non-empty `Name`, `processedBy == currentUser.Name`; when `IsAuthenticated == true` with a null/empty `Name`, `processedBy == "system"` (FR-3).
- `InvoiceClassificationServiceTests` and `ClassifyInvoicesHandlerTests` are updated to the new signatures and pass, including the two new handler tests covering the authenticated and unauthenticated identity-resolution paths (FR-4).
- `InvoiceClassificationModule.cs` requires no edit (confirmed no `ICurrentUserService` registration exists there before or after the change).
- `dotnet build` succeeds and the full `Anela.Heblo.Tests` suite passes.
