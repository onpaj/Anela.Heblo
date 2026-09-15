### task: update-flexi-department-query-service-and-tests

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs`

**Context:** `FlexiDepartmentQueryService` consumes `IDepartmentClient`/`Department` via a plain `using`. Its unit test does the same to construct a `Mock<IDepartmentClient>` and build `List<Department>` fixtures. Both need only their `using` directive swapped — no logic or assertions change.

- [ ] **Step 1: View current `FlexiDepartmentQueryService.cs`**

Run:
```bash
cat backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs
```
Expected top of file:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;
```

- [ ] **Step 2: Update `FlexiDepartmentQueryService.cs`'s `using`**

Change:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;
```
to:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Analytics;
```
The rest of the file (`FlexiDepartmentQueryService` class body, constructor, `GetDepartmentsAsync` mapping to `DepartmentDto`) is unchanged.

- [ ] **Step 3: View current `FlexiDepartmentQueryServiceTests.cs`**

Run:
```bash
cat backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs
```
Expected top of file:
```csharp
using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Moq;
using Xunit;
```

- [ ] **Step 4: Update `FlexiDepartmentQueryServiceTests.cs`'s `using`**

Change:
```csharp
using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Moq;
using Xunit;
```
to:
```csharp
using Anela.Heblo.Adapters.Flexi.Accounting.Departments;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Moq;
using Xunit;
```
The rest of the test file — `Mock<IDepartmentClient>` setup, `List<Department>` fixtures, both `[Fact]` methods and their assertions — is unchanged; it now resolves `IDepartmentClient`/`Department` from the new namespace.

- [ ] **Step 5: Verify no remaining reference to the old namespace in either file**

Run:
```bash
grep -n "InvoiceClassification" \
  backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs \
  backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs
```
Expected: no output (empty).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs
git commit -m "refactor(flexi): point FlexiDepartmentQueryService and its tests at Domain.Features.Analytics"
```

---
