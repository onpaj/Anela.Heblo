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

