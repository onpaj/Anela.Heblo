# Push MaxOrder Computation Into the Database Implementation Plan

**Goal:** Replace `CreateClassificationRuleHandler`'s full-table `GetAllAsync()` + in-memory `Max()` with a single targeted `GetMaxOrderAsync()` repository call that executes as a `SELECT MAX([Order])` database aggregate.
**Architecture:** Extends the existing `IClassificationRuleRepository` / `ClassificationRuleRepository` triad (Domain interface → Persistence implementation → Application consumer) already used for `GetAllAsync`, `GetActiveRulesOrderedAsync`, etc. No new abstractions, no DI changes, no contract/schema changes — a same-shape extension of one existing interface.
**Tech Stack:** .NET 8, EF Core (`MaxAsync`), MediatR, xUnit with EF Core InMemory provider.

---

### task: add-get-max-order-repository-method

**Context:** This is a small, self-contained tech-debt fix from an automated arch-review finding (feat-3545). `CreateClassificationRuleHandler.Handle` currently loads every row of the `ClassificationRules` table via `GetAllAsync()` just to compute the next `Order` value in memory (lines 29-30). This task adds a targeted `GetMaxOrderAsync()` method to the repository interface and implementation, and updates the handler to use it instead — eliminating the unnecessary full-table transfer while preserving identical behavior (new rule gets `Order = maxOrder + 1`, where `maxOrder` is `0` if no rules exist).

This task covers all three edits (interface, implementation, call site) plus a repository-level test, because they are one cohesive change with no independently-testable/committable sub-steps.

**Files:**
- Modify: `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRuleRepository.cs`
- Modify: `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs`
- Modify: `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/CreateClassificationRule/CreateClassificationRuleHandler.cs`
- Create: `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassificationRuleRepositoryTests.cs`

#### Step 1 — Add `GetMaxOrderAsync()` to the repository interface

Open `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRuleRepository.cs`. Its current full contents are:

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IClassificationRuleRepository
{
    Task<List<ClassificationRule>> GetAllAsync();

    Task<List<ClassificationRule>> GetActiveRulesOrderedAsync();

    Task<ClassificationRule?> GetByIdAsync(Guid id);

    Task<ClassificationRule> AddAsync(ClassificationRule rule);

    Task<ClassificationRule> UpdateAsync(ClassificationRule rule);

    Task DeleteAsync(Guid id);

    Task ReorderRulesAsync(List<Guid> ruleIds);
}
```

Replace it with (adds `Task<int> GetMaxOrderAsync();` right after `GetActiveRulesOrderedAsync`):

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IClassificationRuleRepository
{
    Task<List<ClassificationRule>> GetAllAsync();

    Task<List<ClassificationRule>> GetActiveRulesOrderedAsync();

    Task<int> GetMaxOrderAsync();

    Task<ClassificationRule?> GetByIdAsync(Guid id);

    Task<ClassificationRule> AddAsync(ClassificationRule rule);

    Task<ClassificationRule> UpdateAsync(ClassificationRule rule);

    Task DeleteAsync(Guid id);

    Task ReorderRulesAsync(List<Guid> ruleIds);
}
```

Use the Edit tool with `old_string` matching the block from `Task<List<ClassificationRule>> GetActiveRulesOrderedAsync();` through the blank line before `Task<ClassificationRule?> GetByIdAsync(Guid id);`, inserting the new line in between.

#### Step 2 — Implement `GetMaxOrderAsync()` in the repository

Open `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs`. Find the `GetActiveRulesOrderedAsync` method (lines 22-28):

```csharp
    public async Task<List<ClassificationRule>> GetActiveRulesOrderedAsync()
    {
        return await _context.ClassificationRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Order)
            .ToListAsync();
    }
```

Immediately after it (before `GetByIdAsync`), insert the new method:

```csharp
    public async Task<List<ClassificationRule>> GetActiveRulesOrderedAsync()
    {
        return await _context.ClassificationRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Order)
            .ToListAsync();
    }

    public async Task<int> GetMaxOrderAsync()
    {
        return await _context.ClassificationRules.MaxAsync(r => (int?)r.Order) ?? 0;
    }
```

Use the Edit tool with `old_string` set to the `GetActiveRulesOrderedAsync` method body shown above (unique in the file) and `new_string` set to that same body plus the new `GetMaxOrderAsync` method appended after it, as shown.

No new `using` is required — `Microsoft.EntityFrameworkCore` (which provides `MaxAsync`) is already imported at the top of this file (line 1).

#### Step 3 — Update `CreateClassificationRuleHandler` to use the new method

Open `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/CreateClassificationRule/CreateClassificationRuleHandler.cs`. Find lines 29-30:

```csharp
        var allRules = await _ruleRepository.GetAllAsync();
        var maxOrder = allRules.Count > 0 ? allRules.Max(r => r.Order) : 0;
```

Replace with:

```csharp
        var maxOrder = await _ruleRepository.GetMaxOrderAsync();
```

Use the Edit tool with `old_string`:
```
        var allRules = await _ruleRepository.GetAllAsync();
        var maxOrder = allRules.Count > 0 ? allRules.Max(r => r.Order) : 0;
```
and `new_string`:
```
        var maxOrder = await _ruleRepository.GetMaxOrderAsync();
```

Do not change anything else in this file — `rule.SetOrder(maxOrder + 1)`, the `AddAsync` call, and the response mapping (lines 32-49) remain exactly as they are. `GetAllAsync()` is not removed from the interface or implementation; it remains unchanged and is still used elsewhere (rule listing).

#### Step 4 — Build to confirm the interface/implementation/call-site are consistent

Run from the backend directory:

```bash
cd /home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend && dotnet build
```

Expected output: `Build succeeded.` with `0 Error(s)` (warnings unrelated to this change, if any, are pre-existing and not introduced by this fix). If it fails with a CS0535 "does not implement interface member" error, it means `ClassificationRuleRepository` is missing `GetMaxOrderAsync()` — re-check Step 2. If it fails with CS1061 on `_ruleRepository.GetMaxOrderAsync()`, re-check Step 1.

#### Step 5 — Add a repository-level test for `GetMaxOrderAsync()`

The project's existing sibling pattern for this kind of test is `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassificationHistoryRepositoryTests.cs`, which instantiates the repository directly against an `ApplicationDbContext` backed by `UseInMemoryDatabase(Guid.NewGuid())` — no mocking framework, no `CreateClassificationRuleHandlerTests` exists to indirectly cover this path, and no existing `ClassificationRuleRepositoryTests.cs` exists yet. Per the arch-review's explicit guidance, follow that same pattern: create a new minimal test file.

Create `/home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassificationRuleRepositoryTests.cs` with the following full contents:

```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.InvoiceClassification;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class ClassificationRuleRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ClassificationRuleRepository _repository;

    public ClassificationRuleRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ClassificationRuleTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ClassificationRuleRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task GetMaxOrderAsync_WithNoRules_ReturnsZero()
    {
        // Act
        var maxOrder = await _repository.GetMaxOrderAsync();

        // Assert
        Assert.Equal(0, maxOrder);
    }

    [Fact]
    public async Task GetMaxOrderAsync_WithMultipleRules_ReturnsHighestOrder()
    {
        // Arrange
        var rule1 = new ClassificationRule(
            name: "Rule A",
            ruleTypeIdentifier: "CompanyName",
            pattern: "Acme",
            accountingTemplateCode: "TPL1",
            department: null,
            createdBy: "tester");
        rule1.SetOrder(1);

        var rule2 = new ClassificationRule(
            name: "Rule B",
            ruleTypeIdentifier: "CompanyName",
            pattern: "Globex",
            accountingTemplateCode: "TPL2",
            department: null,
            createdBy: "tester");
        rule2.SetOrder(5);

        var rule3 = new ClassificationRule(
            name: "Rule C",
            ruleTypeIdentifier: "CompanyName",
            pattern: "Initech",
            accountingTemplateCode: "TPL3",
            department: null,
            createdBy: "tester");
        rule3.SetOrder(3);

        _context.ClassificationRules.AddRange(rule1, rule2, rule3);
        await _context.SaveChangesAsync();

        // Act
        var maxOrder = await _repository.GetMaxOrderAsync();

        // Assert
        Assert.Equal(5, maxOrder);
    }
}
```

This mirrors `ClassificationHistoryRepositoryTests.cs`'s constructor/dispose pattern exactly and uses the real `ClassificationRule` constructor signature (`name, ruleTypeIdentifier, pattern, accountingTemplateCode, department, createdBy`) plus the existing public `SetOrder(int)` method to set each rule's `Order` after construction (the constructor always initializes `Order = 0`).

#### Step 6 — Run the new and existing InvoiceClassification tests

Run from the backend directory:

```bash
cd /home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend && dotnet test --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.InvoiceClassification"
```

Expected output: all tests pass, including the two new `ClassificationRuleRepositoryTests` tests (`GetMaxOrderAsync_WithNoRules_ReturnsZero`, `GetMaxOrderAsync_WithMultipleRules_ReturnsHighestOrder`) and the pre-existing `ClassificationHistoryRepositoryTests`, `ClassifyInvoicesHandlerTests`, `GetClassificationRuleTypesHandlerTests`, `GetInvoiceDetailsHandlerTests`, `InvoiceClassificationMappingProfileTests`, `InvoiceClassificationServiceTests`, and the `Rules/` and `Services/` subfolder tests — none of which are touched by this change and should be unaffected. Look for a summary line like `Passed! - Failed: 0, Passed: N, Skipped: 0`.

If any pre-existing test in this filter fails, stop and investigate before proceeding — it indicates this change had an unintended side effect (it should not, since `GetAllAsync()` is untouched and no other method was modified).

#### Step 7 — Full solution build (final confirmation)

Run from the backend directory:

```bash
cd /home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica/backend && dotnet build
```

Expected output: `Build succeeded.` with `0 Error(s)`. This is a final sanity check that no other project in the solution references `IClassificationRuleRepository` in a way broken by the new interface member (e.g., a hand-rolled mock elsewhere implementing the interface without a mocking framework). If this fails with a CS0535 in a file outside the three modified above, locate that file, add a `GetMaxOrderAsync()` implementation/mock consistent with its existing style, and re-run this build step.

#### Step 8 — Commit

Stage exactly the four files touched/created in this task and commit:

```bash
cd /home/user/worktrees/feature-3545-Arch-Review-Invoiceclassification-Createclassifica && git add backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRuleRepository.cs backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/CreateClassificationRule/CreateClassificationRuleHandler.cs backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassificationRuleRepositoryTests.cs
```

```bash
git commit -m "$(cat <<'EOF'
Push MaxOrder computation into the database for ClassificationRule creation

CreateClassificationRuleHandler loaded the entire ClassificationRules
table via GetAllAsync() just to compute Max(Order) in memory. Add a
targeted GetMaxOrderAsync() repository method that issues a single
SELECT MAX([Order]) aggregate query instead, and switch the handler
to use it. Behavior is unchanged (new rule gets maxOrder + 1, or 1 if
no rules exist).
EOF
)"
```

Verify the commit succeeded:

```bash
git status
```

Expected output: `nothing to commit, working tree clean` (aside from any unrelated pre-existing changes in the worktree, if present).
