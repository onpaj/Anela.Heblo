### task: add-execute-in-transaction-repository-method

**Context:** This is the foundational change (FR-3). `IRepository<TEntity,TKey>` (in the `Anela.Heblo.Xcc` project) is the generic repository abstraction used across the codebase. Almost every concrete repository derives from `BaseRepository<TEntity,TKey>` (in `Anela.Heblo.Persistence`), which is backed by `ApplicationDbContext`. There is also a vestigial `EmptyRepository<TEntity,TKey>` stub with no production consumers. Adding a new member to `IRepository<TEntity,TKey>` requires every class that implements the interface to implement the new member too, or the solution fails to build.

**IMPORTANT — verified by actually building the solution after adding the interface member:** four **test-only** helper classes implement `IRepository<TEntity,TKey>` directly (via a more specific interface such as `IPackingMaterialRepository : IRepository<PackingMaterial,int>`) without deriving from `BaseRepository<,>`. All four must also get the new member or `dotnet build` fails with `CS0535 ... does not implement interface member 'IRepository<...>.ExecuteInTransactionAsync<TResult>(...)'`:
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialAllocationRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs` (nested private class `CountingRepositoryWrapper`)
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs` (nested private class `CountingRepositoryWrapper`)

**Files:**
- Modify: `backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`
- Modify: `backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialAllocationRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Xcc/EmptyRepositoryExecuteInTransactionTests.cs`

- [ ] **Step 1: Add the interface member to `IRepository<TEntity,TKey>`**

In `backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs`, the current end of the file reads:

```csharp
    // Unit of Work operations
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

Replace it with:

```csharp
    // Unit of Work operations
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single all-or-nothing database transaction and
    /// returns its result on success; on any exception the transaction is rolled back and the
    /// original exception is rethrown unchanged. This wraps the entire underlying DbContext, not
    /// just <typeparamref name="TEntity"/> — calling it on one repository also covers writes made
    /// through any other repository sharing the same DbContext instance in the current DI scope
    /// (the same whole-context semantics <see cref="SaveChangesAsync"/> already has).
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Implement it in `BaseRepository<TEntity,TKey>`**

In `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`, the current end of the file reads:

```csharp
    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await Context.SaveChangesAsync(cancellationToken);
    }
}
```

Replace it with:

```csharp
    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await Context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single all-or-nothing database transaction,
    /// opened via <see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/> so it is
    /// safe under a retrying execution strategy (e.g. PollyExecutionStrategy) — EF Core forbids
    /// calling BeginTransactionAsync directly when the configured strategy retries on failure.
    /// Everything the transaction must cover (entity adds, SaveChangesAsync calls) must happen
    /// inside <paramref name="operation"/>, since a retry re-runs the whole delegate from a clean
    /// change tracker. This wraps the entire underlying DbContext, not just <typeparamref name="TEntity"/>
    /// — the same whole-context semantics <see cref="SaveChangesAsync"/> already has.
    /// </summary>
    public virtual async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = Context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
```

- [ ] **Step 3: Add the no-op pass-through in `EmptyRepository<TEntity,TKey>`**

In `backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs`, the current end of the file reads:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Return 0 as no changes are saved
        return Task.FromResult(0);
    }
}
```

Replace it with:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Return 0 as no changes are saved
        return Task.FromResult(0);
    }

    /// <summary>
    /// No-op pass-through: invokes <paramref name="operation"/> directly with no real transaction.
    /// Provides no isolation if ever mixed with real repositories in the same logical operation —
    /// it only "succeeds" because it has nothing of its own to roll back.
    /// </summary>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}
```

- [ ] **Step 4: Fix the four test-only direct `IRepository<,>` implementers**

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`, find:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_saveChangesException != null)
            throw _saveChangesException;
        return Task.FromResult(0);
    }
```

Replace it with:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_saveChangesException != null)
            throw _saveChangesException;
        return Task.FromResult(0);
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => operation(cancellationToken);
```

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialAllocationRepository.cs`, find:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
```

Replace it with:

```csharp
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}
```

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs`, inside the nested `CountingRepositoryWrapper` class, find:

```csharp
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);
```

Replace it with:

```csharp
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
            => _inner.ExecuteInTransactionAsync(operation, cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);
```

(This delegates to the wrapped real `PackingMaterialRepository` — `_inner` — exactly like every other member on this wrapper.)

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`, inside its own nested `CountingRepositoryWrapper` class, find the identical snippet:

```csharp
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);
```

Replace it with:

```csharp
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
            => _inner.ExecuteInTransactionAsync(operation, cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);
```

- [ ] **Step 5: Build the whole solution and confirm it compiles**

Run (from the repo root):

```bash
dotnet build Anela.Heblo.sln
```

Expected: build output ends with `0 Error(s)` (pre-existing warnings are unrelated and unaffected by this change — do not try to fix them).

- [ ] **Step 6: Write a characterization test for `EmptyRepository.ExecuteInTransactionAsync`**

Create `backend/test/Anela.Heblo.Tests/Xcc/EmptyRepositoryExecuteInTransactionTests.cs`:

```csharp
using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Xcc;

public class EmptyRepositoryExecuteInTransactionTests
{
    private sealed class TestEntity : Entity<int>
    {
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_InvokesOperationAndReturnsItsResult()
    {
        var repository = new EmptyRepository<TestEntity, int>();
        var called = false;

        var result = await repository.ExecuteInTransactionAsync(ct =>
        {
            called = true;
            return Task.FromResult(42);
        });

        called.Should().BeTrue();
        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_PropagatesOperationExceptionUnchanged()
    {
        var repository = new EmptyRepository<TestEntity, int>();

        Func<Task> act = () => repository.ExecuteInTransactionAsync<int>(ct =>
            throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
```

- [ ] **Step 7: Run the new test**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~EmptyRepositoryExecuteInTransactionTests"
```

Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs \
        backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs \
        backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialAllocationRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs \
        backend/test/Anela.Heblo.Tests/Xcc/EmptyRepositoryExecuteInTransactionTests.cs
git commit -m "feat: add ExecuteInTransactionAsync to IRepository abstraction

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01LkwnHn1TLfFi9jP5okeVwP"
```

---
