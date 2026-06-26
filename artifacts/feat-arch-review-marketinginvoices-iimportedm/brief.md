## Module
MarketingInvoices

## Finding
`IImportedMarketingTransactionRepository` declares `AddAsync` returning `Task` (void):

```csharp
// Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:5
Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
```

But the base `BaseRepository<TEntity, TKey>` (which the concrete class inherits) declares:

```csharp
// Persistence/Repositories/BaseRepository.cs:55
public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
```

The return types differ (`Task` vs `Task<TEntity>`). C# cannot override with a different return type, so the concrete class must use `new` to hide the base method:

```csharp
// Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs:21
public new async Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct)
{
    await base.AddAsync(entity, ct);
}
```

`new` shadowing (not overriding) means: if code holds a `BaseRepository<ImportedMarketingTransaction, int>` reference and calls `AddAsync`, it silently dispatches to the base `Task<TEntity>` version, bypassing the shadowed method. This is a Liskov Substitution Principle issue: the subtype behaves differently depending on the static reference type.

## Why it matters
Shadowed methods are a known source of subtle bugs when the concrete type is stored as a base type reference. The inconsistency also makes it unclear whether callers should expect the added entity back or not. The current workaround is a one-liner that adds cognitive noise without benefit.

## Suggested fix
Align the domain interface with the base `IRepository<>` contract — return `Task<ImportedMarketingTransaction>`:

```csharp
// Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs
Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
```

Then the concrete class can use a proper `override`:

```csharp
// Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs
public override async Task<ImportedMarketingTransaction> AddAsync(
    ImportedMarketingTransaction entity, CancellationToken ct)
{
    return await base.AddAsync(entity, ct);
}
```

Or, since the base implementation is sufficient, remove the method from the concrete class entirely and let the base handle it. Update the one call site in `MarketingInvoiceImportService` to discard the return value if not needed.

---
_Filed by daily arch-review routine on 2026-05-26._