### task: relocate-domain-types

**Files:**
- Move: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs` → `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs`
- Move: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs` → `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs`

**Context:** These two types currently live in `Features/InvoiceClassification/` but have zero consumers inside that module — `ClassificationRule.Department`/`ClassificationHistory.Department` are plain `string?` fields unrelated to this `Department` class. Their only real consumers are in the Flexi Accounting adapter area, so they belong in `Features/Analytics/` (an existing domain folder, no naming collision there today). This task moves the files and updates only the `namespace` line in each — the class/interface bodies are otherwise untouched.

- [ ] **Step 1: Confirm current state before moving**

Run:
```bash
cd backend
cat src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs
cat src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs
```
Expected: `Department.cs` shows namespace `Anela.Heblo.Domain.Features.InvoiceClassification;` and a class with `Id`/`Name` string properties. `IDepartmentClient.cs` shows the same namespace and an interface with `GetDepartmentsAsync`/`GetDepartmentByIdAsync`.

- [ ] **Step 2: Move `Department.cs` and update its namespace**

```bash
git mv backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs
```

Then edit the moved file's first line from:
```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;
```
to:
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;
```

The full resulting file must read exactly:
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Move `IDepartmentClient.cs` and update its namespace**

```bash
git mv backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs
```

Then edit the moved file's first line the same way. The full resulting file must read exactly:
```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

public interface IDepartmentClient
{
    Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
}
```

(`Department` here resolves within the same `Analytics` namespace — no `using` needed inside this file.)

- [ ] **Step 4: Confirm the old files are gone and new files exist**

Run:
```bash
ls backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ | grep -i department
ls backend/src/Anela.Heblo.Domain/Features/Analytics/ | grep -i department
```
Expected: first command prints nothing (no match); second command prints `Department.cs` and `IDepartmentClient.cs`.

- [ ] **Step 5: Commit**

The solution will not build after this step alone (four consumer files still reference the old namespace) — that is expected and gets fixed by Tasks 2–4. Commit anyway to keep history bite-sized; do not run `dotnet build` yet.

```bash
git add backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs
git add backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs
git commit -m "refactor(domain): move Department and IDepartmentClient to Analytics module"
```

---
