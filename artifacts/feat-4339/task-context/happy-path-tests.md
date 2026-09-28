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
