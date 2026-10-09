# UpdateClassificationRuleHandler Unit Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cover the not-found and happy paths of `UpdateClassificationRuleHandler` with unit tests (line coverage >= 60%).

**Architecture:** One new xUnit test class using Moq for `IClassificationRuleRepository`, `IMapper`, `ICurrentUserService` and a real `ClassificationRule` entity. No production code changes.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions.

---

### task: not-found-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/UpdateClassificationRuleHandlerTests.cs`

- [ ] **Step 1: Create the test class with mocks, `CreateHandler()`, `CreateExistingRule()` and `CreateRequest(Guid id)` helpers**

Usings: `Anela.Heblo.Application.Features.InvoiceClassification.Contracts`, `...UseCases.UpdateClassificationRule`, `Anela.Heblo.Domain.Features.InvoiceClassification`, `Anela.Heblo.Domain.Features.Users`, `AutoMapper`, `FluentAssertions`, `Moq`, `Xunit`. Namespace `Anela.Heblo.Tests.Features.InvoiceClassification`. Mocks: `Mock<IClassificationRuleRepository>`, `Mock<IMapper>`, `Mock<ICurrentUserService>`; `CurrentUser` is a record: `new CurrentUser("id", "Test User", "t@x.cz", true)`.

- [ ] **Step 2: Write test `Handle_WhenRuleNotFound_ThrowsArgumentExceptionWithId`**

```csharp
var id = Guid.NewGuid();
_repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((ClassificationRule?)null);
var act = () => CreateHandler().Handle(CreateRequest(id), CancellationToken.None);
(await act.Should().ThrowAsync<ArgumentException>())
    .WithMessage($"Classification rule with ID {id} not found");
```

- [ ] **Step 3: Write test `Handle_WhenRuleNotFound_DoesNotCallUpdateAsync`** -- same setup, swallow exception, then `_repo.Verify(r => r.UpdateAsync(It.IsAny<ClassificationRule>()), Times.Never);`

- [ ] **Step 4: Run** `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~UpdateClassificationRuleHandlerTests` -- expect PASS.

- [ ] **Step 5: Commit** `test(invoice-classification): cover UpdateClassificationRuleHandler not-found path`

### task: happy-path-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/UpdateClassificationRuleHandlerTests.cs`

- [ ] **Step 1: Test `Handle_WhenRuleExists_UpdatesAllFieldsAndSetsUpdatedByFromCurrentUser`**

Arrange: existing rule from `CreateExistingRule()`; `GetByIdAsync(rule.Id)` returns it (use `existing.Id`, request Id = `existing.Id`); `GetCurrentUser()` returns user "Test User"; `UpdateAsync(It.IsAny<ClassificationRule>())` returns the argument (`.ReturnsAsync((ClassificationRule r) => r)`); mapper `Map<ClassificationRuleDto>(It.IsAny<object>())` returns `new ClassificationRuleDto()`. Act, then assert `existing.Name/RuleTypeIdentifier/Pattern/AccountingTemplateCode/Department/IsActive` equal request values and `existing.UpdatedBy == "Test User"`.

- [ ] **Step 2: Test `Handle_WhenRuleExists_PersistsAndReturnsMappedDtoOfUpdatedRule`**

`UpdateAsync` returns a *distinct* `ClassificationRule updated`; mapper setup `Map<ClassificationRuleDto>(updated)` returns `sentinelDto`. Assert `response.Rule.Should().BeSameAs(sentinelDto)`, `_repo.Verify(UpdateAsync(existing), Times.Once)`, `_mapper.Verify(m => m.Map<ClassificationRuleDto>(updated), Times.Once)`.

- [ ] **Step 3: Test `Handle_WithNullDepartmentAndInactive_PropagatesValues`** -- request `Department = null`, `IsActive = false`; existing rule has department "old-dept"; assert entity `Department` is null and `IsActive` false.

- [ ] **Step 4: Test `Handle_WhenCurrentUserNameIsNull_ThrowsArgumentNullExceptionAndDoesNotPersist`** -- user `new CurrentUser("id", null, null, true)`; expect `ArgumentNullException` with `ParamName == "updatedBy"`; verify `UpdateAsync` Times.Never.

- [ ] **Step 5: Run** `dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~UpdateClassificationRuleHandlerTests` -- all PASS. Optionally sanity-check by temporarily removing the null check in the handler (tests 1-2 must fail), then revert.

- [ ] **Step 6: Format and full-class validation** -- `dotnet format backend/Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/UpdateClassificationRuleHandlerTests.cs` (adjust solution path if different) and `dotnet build`; run `dotnet test --filter FullyQualifiedName~InvoiceClassification` to confirm no regressions.

- [ ] **Step 7: Commit** `test(invoice-classification): cover UpdateClassificationRuleHandler happy path`
