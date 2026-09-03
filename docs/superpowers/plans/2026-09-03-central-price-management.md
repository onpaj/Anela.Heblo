# Central Retail Price Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Heblo the single place a retail price is edited, pushing it to Shoptet's default price list and ABRA Flexi's `cenik`, with three-way drift detection that stops and asks a human when someone edits a price downstream.

**Architecture:** A new vertical slice `ProductPricing` owns a `ProductPrices` table (one row per product code, price stored **with VAT**) plus a `ProductPriceSyncStates` table (one row per product × target) whose `LastPushedPriceWithVat` column enables a three-way compare. A Hangfire recurring job bulk-reads both remote systems, decides per product via a pure decision function, pushes only changed prices, and records conflicts instead of overwriting. The existing catalog read path is left untouched so it keeps reflecting observed reality — that is what drift is measured against.

**Tech Stack:** .NET 8, MediatR, EF Core + PostgreSQL, Hangfire, FluentValidation, xUnit + Moq + FluentAssertions, React + TypeScript, react-query.

**Spec:** `docs/superpowers/specs/2026-09-03-central-price-management-design.md`

## Global Constraints

- **DTOs are classes, never C# records.** The OpenAPI client generator mishandles record parameter order.
- **Every `*Response` in the Application layer must inherit `Anela.Heblo.Application.Shared.BaseResponse`**, or the reflection contract test in `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs` fails in CI.
- **New `ErrorCodes` values use the 36XX range** (35XX is the last allocated — Mind Maps). Each new value needs a bucket line in `ErrorHandlingTests.cs` **and** a Czech translation in `frontend/src/i18n.ts`, or two tests fail.
- **Validators are registered manually per module.** There is no `AddValidatorsFromAssembly`; register `IValidator<TRequest>` and `IPipelineBehavior<TRequest, TResponse>` explicitly in the module.
- **All `DateTime` columns use `HasColumnType("timestamp without time zone")`.**
- **Prices are stored with VAT.** `numeric(18,4)` in the database; all comparisons round to 2 decimals with `MidpointRounding.AwayFromZero`.
- **Never write purchase price** to either system. Flexi's `cenaNakup` and Shoptet's `buyPrice` are read-only to this feature.
- **In-scope product types:** `ProductType.Product`, `ProductType.Goods`, `ProductType.Set`. Never `Material` or `SemiProduct`.
- **Frontend API hooks build absolute URLs** as `${apiClient.baseUrl}${relativeUrl}`. A relative URL hits port 3001 instead of 5001.
- **Backend test command** (a bare `dotnet test` hangs when another worktree builds concurrently):
  ```bash
  dotnet build Anela.Heblo.sln -c Debug
  dotnet test Anela.Heblo.sln -c Debug --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
  ```
- **Frontend gate is `CI=false npm run build`**, not `npx tsc --noEmit` — `tsc` false-greens because react-i18next `.d.ts` parse errors skip all `src` checks.

---

### Task 1: Domain model and the three-way sync decision

The heart of the feature: a pure function that decides what to do with one product on one target. No database, no HTTP. Everything else in the plan is plumbing around this.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/ProductPrice.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/ProductPriceSyncState.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncTarget.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncStatus.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncAction.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncDecision.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncDecider.cs`
- Test: `backend/test/Anela.Heblo.Tests/Domain/ProductPricing/PriceSyncDeciderTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Domain/ProductPricing/ProductPriceTests.cs`

**Interfaces:**
- Consumes: `Anela.Heblo.Xcc.Domain.Entity<string>` (already used by `CatalogAggregate`).
- Produces: `PriceSyncDecider.Decide(decimal hebloPriceWithVat, decimal? lastPushedPriceWithVat, decimal? remotePriceWithVat)` returning `PriceSyncDecision { PriceSyncAction Action; decimal? PriceToPush; decimal? RemoteValue; }`. Also `ProductPrice.PriceWithoutVat` (computed), and the enums `PriceSyncTarget { Shoptet = 1, Flexi = 2 }`, `PriceSyncStatus { InSync = 0, Pending = 1, Conflict = 2, Failed = 3 }`, `PriceSyncAction { None, Push, Conflict, Seed, MissingRemote }`.

- [ ] **Step 1: Write the failing decider tests**

Create `backend/test/Anela.Heblo.Tests/Domain/ProductPricing/PriceSyncDeciderTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.ProductPricing;

public class PriceSyncDeciderTests
{
    [Fact]
    public void returns_none_when_neither_heblo_nor_remote_moved_since_last_push()
    {
        // Arrange
        const decimal heblo = 190.00m, lastPushed = 190.00m, remote = 190.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.None);
        decision.PriceToPush.Should().BeNull();
    }

    [Fact]
    public void returns_push_when_only_heblo_changed()
    {
        // Arrange
        const decimal heblo = 210.00m, lastPushed = 190.00m, remote = 190.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Push);
        decision.PriceToPush.Should().Be(210.00m);
    }

    [Fact]
    public void returns_conflict_when_only_the_remote_changed()
    {
        // Arrange
        const decimal heblo = 190.00m, lastPushed = 190.00m, remote = 175.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Conflict);
        decision.RemoteValue.Should().Be(175.00m);
        decision.PriceToPush.Should().BeNull();
    }

    [Fact]
    public void returns_conflict_when_both_heblo_and_the_remote_changed()
    {
        // Arrange
        const decimal heblo = 210.00m, lastPushed = 190.00m, remote = 175.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Conflict);
        decision.RemoteValue.Should().Be(175.00m);
        decision.PriceToPush.Should().BeNull();
    }

    [Fact]
    public void returns_seed_when_nothing_has_ever_been_pushed()
    {
        // Arrange
        const decimal heblo = 0m;
        decimal? lastPushed = null;
        const decimal remote = 190.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Seed);
        decision.RemoteValue.Should().Be(190.00m);
    }

    [Fact]
    public void returns_missing_remote_when_the_product_is_absent_downstream()
    {
        // Arrange
        const decimal heblo = 190.00m;
        decimal? lastPushed = 190.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remotePriceWithVat: null);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.MissingRemote);
    }

    [Fact]
    public void missing_remote_wins_over_never_pushed()
    {
        // Arrange & Act
        var decision = PriceSyncDecider.Decide(190.00m, lastPushedPriceWithVat: null, remotePriceWithVat: null);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.MissingRemote);
    }

    [Theory]
    [InlineData(190.001, 190.004)]
    [InlineData(189.999, 190.001)]
    public void treats_differences_below_two_decimals_as_equal(decimal heblo, decimal remote)
    {
        // Arrange & Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushedPriceWithVat: remote, remotePriceWithVat: remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.None);
    }

    [Fact]
    public void treats_a_one_haler_difference_as_a_real_change()
    {
        // Arrange & Act
        var decision = PriceSyncDecider.Decide(190.01m, lastPushedPriceWithVat: 190.00m, remotePriceWithVat: 190.00m);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Push);
        decision.PriceToPush.Should().Be(190.01m);
    }
}
```

Create `backend/test/Anela.Heblo.Tests/Domain/ProductPricing/ProductPriceTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.ProductPricing;

public class ProductPriceTests
{
    [Theory]
    [InlineData(21, 190.00, 157.02)]
    [InlineData(15, 190.00, 165.22)]
    [InlineData(0, 190.00, 190.00)]
    public void derives_price_without_vat_from_the_canonical_with_vat_value(
        decimal vatRate, decimal priceWithVat, decimal expectedWithoutVat)
    {
        // Arrange
        var price = new ProductPrice { ProductCode = "OCH001030", PriceWithVat = priceWithVat, VatRate = vatRate };

        // Act
        var withoutVat = price.PriceWithoutVat;

        // Assert
        withoutVat.Should().Be(expectedWithoutVat);
    }

    [Fact]
    public void exposes_product_code_as_the_entity_identity()
    {
        // Arrange
        var price = new ProductPrice { ProductCode = "OCH001030" };

        // Act & Assert
        price.Id.Should().Be("OCH001030");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — `The type or namespace name 'ProductPricing' does not exist`.

- [ ] **Step 3: Write the domain types**

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncTarget.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>External system a Heblo price is pushed to.</summary>
public enum PriceSyncTarget
{
    Shoptet = 1,
    Flexi = 2,
}
```

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncStatus.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

public enum PriceSyncStatus
{
    InSync = 0,
    Pending = 1,
    Conflict = 2,
    Failed = 3,
}
```

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncAction.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

public enum PriceSyncAction
{
    /// <summary>Heblo and the remote both match the last pushed value. Do nothing.</summary>
    None,

    /// <summary>Only Heblo moved. Push the new price.</summary>
    Push,

    /// <summary>The remote moved since Heblo last pushed. A human must decide.</summary>
    Conflict,

    /// <summary>Nothing has ever been pushed for this product/target. Adopt the remote value.</summary>
    Seed,

    /// <summary>The product does not exist in the remote system. Never create it.</summary>
    MissingRemote,
}
```

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncDecision.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Outcome of the three-way compare for one product on one target.</summary>
public class PriceSyncDecision
{
    public PriceSyncAction Action { get; init; }

    /// <summary>Set only when <see cref="Action"/> is <see cref="PriceSyncAction.Push"/>.</summary>
    public decimal? PriceToPush { get; init; }

    /// <summary>The remote value, set for Conflict and Seed.</summary>
    public decimal? RemoteValue { get; init; }
}
```

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/PriceSyncDecider.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>
/// Three-way compare between Heblo's price, the value Heblo last pushed, and the
/// value currently in the remote system.
///
/// Comparing Heblo to the remote only tells you *that* they differ. Comparing both
/// against the last pushed value tells you *who moved*, which is what lets a
/// downstream edit stop the sync instead of being silently overwritten.
/// </summary>
public static class PriceSyncDecider
{
    private const int PriceDecimals = 2;

    public static PriceSyncDecision Decide(
        decimal hebloPriceWithVat,
        decimal? lastPushedPriceWithVat,
        decimal? remotePriceWithVat)
    {
        if (remotePriceWithVat is null)
        {
            return new PriceSyncDecision { Action = PriceSyncAction.MissingRemote };
        }

        var remote = Normalize(remotePriceWithVat.Value);

        if (lastPushedPriceWithVat is null)
        {
            return new PriceSyncDecision { Action = PriceSyncAction.Seed, RemoteValue = remote };
        }

        var heblo = Normalize(hebloPriceWithVat);
        var lastPushed = Normalize(lastPushedPriceWithVat.Value);

        // Remote drift is checked first: when both sides moved it is still a conflict,
        // and a human decides which wins.
        if (remote != lastPushed)
        {
            return new PriceSyncDecision { Action = PriceSyncAction.Conflict, RemoteValue = remote };
        }

        if (heblo != lastPushed)
        {
            return new PriceSyncDecision { Action = PriceSyncAction.Push, PriceToPush = heblo };
        }

        return new PriceSyncDecision { Action = PriceSyncAction.None };
    }

    private static decimal Normalize(decimal value) =>
        Math.Round(value, PriceDecimals, MidpointRounding.AwayFromZero);
}
```

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/ProductPrice.cs`:

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>
/// The master retail price for one product. Heblo owns this value; Shoptet and
/// Flexi are downstream copies of it.
/// </summary>
public class ProductPrice : Entity<string>
{
    public string ProductCode
    {
        get => Id;
        set => Id = value;
    }

    /// <summary>Canonical form. This is the number a human types and rounds.</summary>
    public decimal PriceWithVat { get; set; }

    /// <summary>0, 15 or 21. Sourced from Flexi.</summary>
    public decimal VatRate { get; set; }

    public DateTime ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;

    /// <summary>Derived for Flexi's <c>cenaZakl</c>, which stores prices excluding VAT.</summary>
    public decimal PriceWithoutVat =>
        Math.Round(PriceWithVat / (1 + VatRate / 100m), 2, MidpointRounding.AwayFromZero);
}
```

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/ProductPriceSyncState.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Sync state of one product against one external system.</summary>
public class ProductPriceSyncState
{
    public string ProductCode { get; set; } = string.Empty;

    public PriceSyncTarget Target { get; set; }

    /// <summary>
    /// The value Heblo last successfully pushed. Null until the first push.
    /// This is what makes drift attributable — see <see cref="PriceSyncDecider"/>.
    /// </summary>
    public decimal? LastPushedPriceWithVat { get; set; }

    public DateTime? LastPushedAt { get; set; }

    public PriceSyncStatus Status { get; set; } = PriceSyncStatus.Pending;

    /// <summary>The downstream value that caused the conflict. Null unless Status is Conflict.</summary>
    public decimal? RemoteValueAtConflict { get; set; }

    public DateTime? ConflictDetectedAt { get; set; }

    public string? LastError { get; set; }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Debug --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ProductPricing"
```
Expected: PASS, 14 tests (10 decider + 4 entity).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/ProductPricing backend/test/Anela.Heblo.Tests/Domain/ProductPricing
git commit -m "feat: product pricing domain model and three-way sync decider"
```

---

### Task 2: Persistence

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceSyncStateConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceRepository.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/IProductPriceRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (add two `DbSet` properties near the other feature sets, around line 73)
- Create: migration `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddProductPricing.cs` (generated)
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceRepositoryTests.cs`

**Interfaces:**
- Consumes: `ProductPrice`, `ProductPriceSyncState`, `PriceSyncTarget`, `PriceSyncStatus` from Task 1.
- Produces:
  ```csharp
  public interface IProductPriceRepository
  {
      Task<IReadOnlyList<ProductPrice>> GetAllAsync(CancellationToken ct);
      Task<ProductPrice?> GetAsync(string productCode, CancellationToken ct);
      Task UpsertAsync(ProductPrice price, CancellationToken ct);
      Task<IReadOnlyList<ProductPriceSyncState>> GetSyncStatesAsync(PriceSyncTarget target, CancellationToken ct);
      Task<IReadOnlyList<ProductPriceSyncState>> GetConflictsAsync(CancellationToken ct);
      Task<ProductPriceSyncState?> GetSyncStateAsync(string productCode, PriceSyncTarget target, CancellationToken ct);
      Task UpsertSyncStateAsync(ProductPriceSyncState state, CancellationToken ct);
      Task SaveChangesAsync(CancellationToken ct);
  }
  ```

> **Note on tests:** the EF InMemory provider throws on `ExecuteDelete`/`ExecuteUpdate`. The repository must use `RemoveRange`/tracked updates only.

- [ ] **Step 1: Write the failing repository tests**

Create `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceRepositoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.ProductPricing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ProductPriceRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ProductPriceRepository _repository;

    public ProductPriceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new ProductPriceRepository(_context);
    }

    [Fact]
    public async Task upsert_inserts_a_price_that_does_not_exist_yet()
    {
        // Arrange
        var price = new ProductPrice
        {
            ProductCode = "OCH001030",
            PriceWithVat = 190.00m,
            VatRate = 21m,
            ModifiedAt = new DateTime(2026, 9, 3, 10, 0, 0),
            ModifiedBy = "ondra@anela.cz",
        };

        // Act
        await _repository.UpsertAsync(price, CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Assert
        var stored = await _repository.GetAsync("OCH001030", CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.PriceWithVat.Should().Be(190.00m);
    }

    [Fact]
    public async Task upsert_overwrites_an_existing_price_without_duplicating_the_row()
    {
        // Arrange
        await _repository.UpsertAsync(
            new ProductPrice { ProductCode = "OCH001030", PriceWithVat = 190.00m, VatRate = 21m },
            CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Act
        await _repository.UpsertAsync(
            new ProductPrice { ProductCode = "OCH001030", PriceWithVat = 210.00m, VatRate = 21m },
            CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Assert
        var all = await _repository.GetAllAsync(CancellationToken.None);
        all.Should().HaveCount(1);
        all[0].PriceWithVat.Should().Be(210.00m);
    }

    [Fact]
    public async Task sync_states_are_keyed_by_product_and_target_independently()
    {
        // Arrange
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState
            {
                ProductCode = "OCH001030",
                Target = PriceSyncTarget.Shoptet,
                Status = PriceSyncStatus.InSync,
                LastPushedPriceWithVat = 190.00m,
            },
            CancellationToken.None);
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState
            {
                ProductCode = "OCH001030",
                Target = PriceSyncTarget.Flexi,
                Status = PriceSyncStatus.Conflict,
                RemoteValueAtConflict = 175.00m,
            },
            CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Act
        var shoptet = await _repository.GetSyncStateAsync("OCH001030", PriceSyncTarget.Shoptet, CancellationToken.None);
        var flexi = await _repository.GetSyncStateAsync("OCH001030", PriceSyncTarget.Flexi, CancellationToken.None);

        // Assert
        shoptet!.Status.Should().Be(PriceSyncStatus.InSync);
        flexi!.Status.Should().Be(PriceSyncStatus.Conflict);
        flexi.RemoteValueAtConflict.Should().Be(175.00m);
    }

    [Fact]
    public async Task get_conflicts_returns_only_conflicted_states_across_both_targets()
    {
        // Arrange
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState { ProductCode = "A", Target = PriceSyncTarget.Shoptet, Status = PriceSyncStatus.InSync },
            CancellationToken.None);
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState { ProductCode = "B", Target = PriceSyncTarget.Shoptet, Status = PriceSyncStatus.Conflict },
            CancellationToken.None);
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState { ProductCode = "C", Target = PriceSyncTarget.Flexi, Status = PriceSyncStatus.Conflict },
            CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Act
        var conflicts = await _repository.GetConflictsAsync(CancellationToken.None);

        // Assert
        conflicts.Select(c => c.ProductCode).Should().BeEquivalentTo(new[] { "B", "C" });
    }

    public void Dispose() => _context.Dispose();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — `ProductPriceRepository` does not exist.

- [ ] **Step 3: Write the repository, EF configuration and DbSets**

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/IProductPriceRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

public interface IProductPriceRepository
{
    Task<IReadOnlyList<ProductPrice>> GetAllAsync(CancellationToken ct);
    Task<ProductPrice?> GetAsync(string productCode, CancellationToken ct);
    Task UpsertAsync(ProductPrice price, CancellationToken ct);
    Task<IReadOnlyList<ProductPriceSyncState>> GetSyncStatesAsync(PriceSyncTarget target, CancellationToken ct);
    Task<IReadOnlyList<ProductPriceSyncState>> GetConflictsAsync(CancellationToken ct);
    Task<ProductPriceSyncState?> GetSyncStateAsync(string productCode, PriceSyncTarget target, CancellationToken ct);
    Task UpsertSyncStateAsync(ProductPriceSyncState state, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
```

`backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        builder.ToTable("ProductPrices", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("ProductCode")
            .IsRequired()
            .HasMaxLength(50);

        builder.Ignore(e => e.ProductCode);
        builder.Ignore(e => e.PriceWithoutVat);

        builder.Property(e => e.PriceWithVat)
            .IsRequired()
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.VatRate)
            .IsRequired()
            .HasColumnType("decimal(5,2)");

        builder.Property(e => e.ModifiedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ModifiedBy)
            .IsRequired()
            .HasMaxLength(200);
    }
}
```

`backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceSyncStateConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceSyncStateConfiguration : IEntityTypeConfiguration<ProductPriceSyncState>
{
    public void Configure(EntityTypeBuilder<ProductPriceSyncState> builder)
    {
        builder.ToTable("ProductPriceSyncStates", "public");

        builder.HasKey(e => new { e.ProductCode, e.Target });

        builder.Property(e => e.ProductCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Target)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.LastPushedPriceWithVat)
            .IsRequired(false)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.RemoteValueAtConflict)
            .IsRequired(false)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.LastPushedAt)
            .IsRequired(false)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ConflictDetectedAt)
            .IsRequired(false)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastError)
            .IsRequired(false)
            .HasMaxLength(2000);

        // The conflicts worklist queries by status across both targets.
        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_ProductPriceSyncStates_Status");
    }
}
```

`backend/src/Anela.Heblo.Persistence/ProductPricing/ProductPriceRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceRepository : IProductPriceRepository
{
    private readonly ApplicationDbContext _context;

    public ProductPriceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProductPrice>> GetAllAsync(CancellationToken ct) =>
        await _context.ProductPrices.ToListAsync(ct);

    public async Task<ProductPrice?> GetAsync(string productCode, CancellationToken ct) =>
        await _context.ProductPrices.FirstOrDefaultAsync(p => p.Id == productCode, ct);

    public async Task UpsertAsync(ProductPrice price, CancellationToken ct)
    {
        var existing = await _context.ProductPrices.FirstOrDefaultAsync(p => p.Id == price.Id, ct);
        if (existing is null)
        {
            _context.ProductPrices.Add(price);
            return;
        }

        existing.PriceWithVat = price.PriceWithVat;
        existing.VatRate = price.VatRate;
        existing.ModifiedAt = price.ModifiedAt;
        existing.ModifiedBy = price.ModifiedBy;
    }

    public async Task<IReadOnlyList<ProductPriceSyncState>> GetSyncStatesAsync(
        PriceSyncTarget target, CancellationToken ct) =>
        await _context.ProductPriceSyncStates.Where(s => s.Target == target).ToListAsync(ct);

    public async Task<IReadOnlyList<ProductPriceSyncState>> GetConflictsAsync(CancellationToken ct) =>
        await _context.ProductPriceSyncStates
            .Where(s => s.Status == PriceSyncStatus.Conflict)
            .ToListAsync(ct);

    public async Task<ProductPriceSyncState?> GetSyncStateAsync(
        string productCode, PriceSyncTarget target, CancellationToken ct) =>
        await _context.ProductPriceSyncStates
            .FirstOrDefaultAsync(s => s.ProductCode == productCode && s.Target == target, ct);

    public async Task UpsertSyncStateAsync(ProductPriceSyncState state, CancellationToken ct)
    {
        var existing = await GetSyncStateAsync(state.ProductCode, state.Target, ct);
        if (existing is null)
        {
            _context.ProductPriceSyncStates.Add(state);
            return;
        }

        existing.LastPushedPriceWithVat = state.LastPushedPriceWithVat;
        existing.LastPushedAt = state.LastPushedAt;
        existing.Status = state.Status;
        existing.RemoteValueAtConflict = state.RemoteValueAtConflict;
        existing.ConflictDetectedAt = state.ConflictDetectedAt;
        existing.LastError = state.LastError;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _context.SaveChangesAsync(ct);
}
```

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, add next to the other feature `DbSet`s (near line 73) and add `using Anela.Heblo.Domain.Features.ProductPricing;` to the usings:

```csharp
    public DbSet<ProductPrice> ProductPrices { get; set; } = null!;
    public DbSet<ProductPriceSyncState> ProductPriceSyncStates { get; set; } = null!;
```

No `ApplyConfiguration` call is needed — line 197 already runs `ApplyConfigurationsFromAssembly`.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Debug --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ProductPriceRepositoryTests"
```
Expected: PASS, 4 tests.

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add AddProductPricing \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Open the generated migration and confirm it creates exactly two tables — `ProductPrices` (PK `ProductCode`) and `ProductPriceSyncStates` (composite PK `ProductCode` + `Target`, index on `Status`) — and touches nothing else.

`ProductPrice` inherits `Entity<string>` and exposes `ProductCode` as an alias over `Id`. Confirm the migration emits a single `ProductCode` column and **no** shadow `Id` column. If EF rejects `builder.Ignore(e => e.ProductCode)`, drop the `Ignore` call and keep only the `HasColumnName("ProductCode")` mapping — report which variant worked. If it contains unrelated changes, delete it, pull the latest `main`, and regenerate.

> Migrations in this project are applied **manually**, not by the deployment. Note in the PR that this migration needs running.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/ProductPricing/IProductPriceRepository.cs \
        backend/src/Anela.Heblo.Persistence/ProductPricing \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
        backend/src/Anela.Heblo.Persistence/Migrations \
        backend/test/Anela.Heblo.Tests/Features/ProductPricing
git commit -m "feat: product pricing persistence and migration"
```

---

### Task 3: Shoptet price list client

Reads the default price list via the REST API and writes single prices back. Replaces the CSV export path (retired in Task 5).

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/IEshopPriceListClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs`
- (Task 5 adds `VatRateCalculator` in the same namespace — Task 6 depends on it)
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/Model/PriceListResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/Model/PriceListSnapshotResponse.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs` (add `DefaultPriceListId`)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` (register the client)
- Modify: `docs/integrations/shoptet-api.md` (document the endpoints **before** relying on them)
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetPriceListClientTests.cs`

**Interfaces:**
- Consumes: `ShoptetApiSettings` (`BaseUrl`, `ApiToken`), the `Shoptet-Private-API-Token` header convention used by `ShoptetOrderClient`.
- Produces:
  ```csharp
  public interface IEshopPriceListClient
  {
      Task<IReadOnlyDictionary<string, decimal>> GetPricesWithVatAsync(CancellationToken ct);
      Task SetPriceWithVatAsync(string productCode, decimal priceWithVat, CancellationToken ct);
  }
  ```

- [ ] **Step 1: Document the endpoints in `docs/integrations/shoptet-api.md`**

Append a new section (this repo requires findings documented before code depends on them):

```markdown
## Price lists API

Base: `https://api.myshoptet.com`, header `Shoptet-Private-API-Token`.

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/pricelists` | All price lists configured on the e-shop. Identifies the default one. |
| GET | `/api/pricelists/{id}/snapshot` | All items of one price list. Paginated: `itemsPerPage` default and **max 100**, `page` from 1. |
| PATCH | `/api/pricelists/{id}` | Update prices of individual items. |
| PATCH | `/api/pricelists/{id}/batch` | Async bulk update, JSONL body, max 100 MB. **Not used — see below.** |

**Item price fields on PATCH:**

| Field | Meaning |
|---|---|
| `price` | Sets the stored price directly, no recalculation. Interpretation depends on the list's `includingVat`. |
| `priceWithVat` | Sets the price including VAT; Shoptet recalculates the stored form. |
| `priceWithoutVat` | Sets the price excluding VAT; Shoptet recalculates. |
| `buyPrice` | **Writable only on the default price list**; stays `null` on all others. |
| `vatRate`, `includingVat` | Optional; changing either triggers recalculation of the stored prices. |

**Zero vs null (rollout from 2026-09-14, feature-flagged per e-shop).** A literal `0`
in `data.price.price`, `data.price.commonPrice`, `data.price.buyPrice` or
`data.prices.purchasePrice.price` used to *clear* the price. After the flag flips, `0`
means a genuine zero price and only `null` clears it. Never send `0` to mean "no price" —
omit the product instead. CSV/XML import/export gained the same empty-cell vs `0`
distinction.

**Async endpoints require a webhook.** Every async endpoint — including
`PATCH /api/pricelists/{id}/batch` — returns **403 and never queues the job** unless the
`job:finished` webhook is registered for the e-shop. The response is `202` with a `jobId`;
the result is then read from `GET /api/system/jobs/{jobId}`, whose `log` attribute
identifies rows by their 1-based position in the uploaded file. A failed job is marked
failed 3 hours after creation and emits **no** `job:finished` webhook.

**Why Heblo uses per-item PATCH, not batch:** the price sync pushes only *changed*
prices — a handful per run — so the batch endpoint's inbound-webhook dependency buys
nothing. Batch remains the option if a bulk repricing is ever needed.
```

Also append to `docs/integrations/flexibee-api.md` a short section (used by Task 4, written now so both docs land before either client does):

```markdown
## Ceník price writes

`PUT /c/{firma}/cenik/{idcenik}.json` with body
`{"winstrom":{"cenik":{"cenaZakl":"157.02"}}}` updates an item's base selling price.
`cenaZakl` is **excluding VAT**; `cenanakup` is the purchase price and is computed from
the BoM — never written by Heblo.

**Addressing by `code:` is dangerous.** Flexi makes no distinction between create and
update: it decides from the identifier. `PUT /c/{firma}/cenik/code:XXX.json` with an
unknown code **creates a new price list item** rather than failing. Always address writes
by the internal numeric `idcenik` (read as `ProductPriceFlexiDto.ProductId` from user
query 41). A product with no known `idcenik` must be reported as a failure, never created.
```

- [ ] **Step 2: Write the failing client tests**

Create `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetPriceListClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Pricing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetPriceListClientTests
{
    private static ShoptetPriceListClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        List<HttpRequestMessage>? recorded = null,
        int? defaultPriceListId = 1)
    {
        var handler = new StubHandler(responder, recorded);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.myshoptet.com") };
        var settings = Options.Create(new ShoptetApiSettings
        {
            BaseUrl = "https://api.myshoptet.com",
            ApiToken = "token",
            DefaultPriceListId = defaultPriceListId,
        });
        return new ShoptetPriceListClient(httpClient, settings, NullLogger<ShoptetPriceListClient>.Instance);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task reads_all_pages_of_the_snapshot()
    {
        // Arrange
        var page1 = """
        {"data":{"pricelist":[{"code":"A","priceWithVat":"190.00"},{"code":"B","priceWithVat":"250.50"}],
         "paginator":{"page":1,"pageCount":2}},"errors":null}
        """;
        var page2 = """
        {"data":{"pricelist":[{"code":"C","priceWithVat":"99.00"}],
         "paginator":{"page":2,"pageCount":2}},"errors":null}
        """;
        var client = CreateClient(req =>
            Json(req.RequestUri!.Query.Contains("page=2") ? page2 : page1));

        // Act
        var prices = await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        prices.Should().HaveCount(3);
        prices["A"].Should().Be(190.00m);
        prices["C"].Should().Be(99.00m);
    }

    [Fact]
    public async Task requests_the_snapshot_with_the_maximum_page_size()
    {
        // Arrange
        var recorded = new List<HttpRequestMessage>();
        var client = CreateClient(
            _ => Json("""{"data":{"pricelist":[],"paginator":{"page":1,"pageCount":1}},"errors":null}"""),
            recorded);

        // Act
        await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        recorded.Should().ContainSingle();
        recorded[0].RequestUri!.ToString().Should().Contain("/api/pricelists/1/snapshot");
        recorded[0].RequestUri!.Query.Should().Contain("itemsPerPage=100");
    }

    [Fact]
    public async Task resolves_the_default_price_list_when_none_is_configured()
    {
        // Arrange
        var recorded = new List<HttpRequestMessage>();
        var client = CreateClient(req =>
                req.RequestUri!.AbsolutePath == "/api/pricelists"
                    ? Json("""{"data":{"pricelists":[{"id":7,"name":"Velkoobchod","default":false},
                               {"id":3,"name":"Základní","default":true}]},"errors":null}""")
                    : Json("""{"data":{"pricelist":[],"paginator":{"page":1,"pageCount":1}},"errors":null}"""),
            recorded, defaultPriceListId: null);

        // Act
        await client.GetPricesWithVatAsync(CancellationToken.None);

        // Assert
        recorded.Last().RequestUri!.AbsolutePath.Should().Be("/api/pricelists/3/snapshot");
    }

    [Fact]
    public async Task sends_price_with_vat_on_patch()
    {
        // Arrange
        var recorded = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var handler = new StubHandler(_ => Json("""{"data":null,"errors":null}"""), recorded, bodies);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.myshoptet.com") };
        var client = new ShoptetPriceListClient(
            httpClient,
            Options.Create(new ShoptetApiSettings { BaseUrl = "https://api.myshoptet.com", ApiToken = "t", DefaultPriceListId = 1 }),
            NullLogger<ShoptetPriceListClient>.Instance);

        // Act
        await client.SetPriceWithVatAsync("OCH001030", 210.00m, CancellationToken.None);

        // Assert
        recorded.Should().ContainSingle();
        recorded[0].Method.Should().Be(HttpMethod.Patch);
        recorded[0].RequestUri!.AbsolutePath.Should().Be("/api/pricelists/1");
        bodies[0].Should().Contain("OCH001030").And.Contain("210.00");
        bodies[0].Should().NotContain("buyPrice");
    }

    [Fact]
    public async Task throws_with_the_response_body_when_shoptet_rejects_the_patch()
    {
        // Arrange
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"errors":[{"message":"Invalid price"}]}""", Encoding.UTF8, "application/json"),
        });

        // Act
        var act = () => client.SetPriceWithVatAsync("OCH001030", 210.00m, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).And.Message.Should().Contain("Invalid price");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly List<HttpRequestMessage>? _recorded;
        private readonly List<string>? _bodies;

        public StubHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            List<HttpRequestMessage>? recorded = null,
            List<string>? bodies = null)
        {
            _responder = responder;
            _recorded = recorded;
            _bodies = bodies;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _recorded?.Add(request);
            if (_bodies is not null && request.Content is not null)
            {
                _bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return _responder(request);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — `ShoptetPriceListClient` does not exist.

- [ ] **Step 4: Write the client**

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/IEshopPriceListClient.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Read and write the e-shop's default (retail) price list.</summary>
public interface IEshopPriceListClient
{
    /// <summary>Current prices including VAT, keyed by product code.</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetPricesWithVatAsync(CancellationToken ct);

    Task SetPriceWithVatAsync(string productCode, decimal priceWithVat, CancellationToken ct);
}
```

Add to `ShoptetApiSettings`:

```csharp
    /// <summary>
    /// Shoptet price list to sync retail prices with. When null the client resolves the
    /// e-shop's default list via GET /api/pricelists. Configure as Shoptet:DefaultPriceListId.
    /// </summary>
    public int? DefaultPriceListId { get; set; }
```

`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/Model/PriceListResponse.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing.Model;

public class PriceListResponse
{
    [JsonPropertyName("data")]
    public PriceListResponseData? Data { get; set; }
}

public class PriceListResponseData
{
    [JsonPropertyName("pricelists")]
    public List<PriceListSummary> PriceLists { get; set; } = new();
}

public class PriceListSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("default")]
    public bool IsDefault { get; set; }
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/Model/PriceListSnapshotResponse.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing.Model;

public class PriceListSnapshotResponse
{
    [JsonPropertyName("data")]
    public PriceListSnapshotData? Data { get; set; }
}

public class PriceListSnapshotData
{
    [JsonPropertyName("pricelist")]
    public List<PriceListSnapshotItem> Items { get; set; } = new();

    [JsonPropertyName("paginator")]
    public PriceListPaginator? Paginator { get; set; }
}

public class PriceListSnapshotItem
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Shoptet returns prices as strings with 2 decimals, e.g. "190.00".</summary>
    [JsonPropertyName("priceWithVat")]
    public string? PriceWithVat { get; set; }
}

public class PriceListPaginator
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetPriceListClient.cs`:

```csharp
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Pricing.Model;
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing;

public class ShoptetPriceListClient : IEshopPriceListClient
{
    /// <summary>Shoptet caps the snapshot page size at 100.</summary>
    private const int MaxItemsPerPage = 100;

    private readonly HttpClient _httpClient;
    private readonly IOptions<ShoptetApiSettings> _settings;
    private readonly ILogger<ShoptetPriceListClient> _logger;
    private int? _resolvedPriceListId;

    public ShoptetPriceListClient(
        HttpClient httpClient,
        IOptions<ShoptetApiSettings> settings,
        ILogger<ShoptetPriceListClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesWithVatAsync(CancellationToken ct)
    {
        var priceListId = await ResolvePriceListIdAsync(ct);
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var page = 1;
        int pageCount;
        do
        {
            var url = $"/api/pricelists/{priceListId}/snapshot?itemsPerPage={MaxItemsPerPage}&page={page}";
            var snapshot = await GetAsync<PriceListSnapshotResponse>(url, ct);

            foreach (var item in snapshot.Data?.Items ?? new List<PriceListSnapshotItem>())
            {
                if (string.IsNullOrWhiteSpace(item.Code) || !TryParsePrice(item.PriceWithVat, out var price))
                {
                    continue;
                }

                prices[item.Code] = price;
            }

            pageCount = snapshot.Data?.Paginator?.PageCount ?? 1;
            page++;
        }
        while (page <= pageCount);

        _logger.LogInformation("Read {Count} prices from Shoptet price list {PriceListId}", prices.Count, priceListId);
        return prices;
    }

    public async Task SetPriceWithVatAsync(string productCode, decimal priceWithVat, CancellationToken ct)
    {
        var priceListId = await ResolvePriceListIdAsync(ct);

        // priceWithVat (never `price`) so Shoptet recalculates the stored form itself.
        // Never send 0 to mean "no price" — from 2026-09-14 that is a genuine zero price.
        var payload = new
        {
            data = new[]
            {
                new
                {
                    code = productCode,
                    priceWithVat = priceWithVat.ToString("F2", CultureInfo.InvariantCulture),
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/pricelists/{priceListId}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<int> ResolvePriceListIdAsync(CancellationToken ct)
    {
        if (_settings.Value.DefaultPriceListId is { } configured)
        {
            return configured;
        }

        if (_resolvedPriceListId is { } cached)
        {
            return cached;
        }

        var lists = await GetAsync<PriceListResponse>("/api/pricelists", ct);
        var defaultList = lists.Data?.PriceLists.FirstOrDefault(l => l.IsDefault)
            ?? throw new InvalidOperationException(
                "Shoptet returned no default price list. Set Shoptet:DefaultPriceListId explicitly.");

        _resolvedPriceListId = defaultList.Id;
        return defaultList.Id;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new HttpRequestException($"Shoptet returned an empty body for {url}");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Shoptet price list request failed with {(int)response.StatusCode}: {body}");
    }

    private static bool TryParsePrice(string? raw, out decimal price) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out price);
}
```

Register in `ShoptetApiAdapterServiceCollectionExtensions.AddShoptetApiAdapter`, next to the other clients:

```csharp
        services.AddHttpClient<IEshopPriceListClient, ShoptetPriceListClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });
```
(add `using Anela.Heblo.Adapters.ShoptetApi.Pricing;` and `using Anela.Heblo.Domain.Features.ProductPricing;`)

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Debug --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ShoptetPriceListClientTests"
```
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/ProductPricing/IEshopPriceListClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetPriceListClientTests.cs \
        docs/integrations/shoptet-api.md docs/integrations/flexibee-api.md
git commit -m "feat: Shoptet price list client and API documentation"
```

---

### Task 4: Flexi price writer

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPriceWriter.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceWriter.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Price/ProductPriceErp.cs` (add `ErpItemId`)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs:96-104` (map `ErpItemId` from `s.ProductId`)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` (register the writer)
- Test: `backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiProductPriceWriterTests.cs`

**Interfaces:**
- Consumes: `FlexiBeeSettings` from `Rem.FlexiBeeSDK.Client` (already injected across the Flexi adapter), `ProductPriceFlexiDto.ProductId` (`idcenik`).
- Produces:
  ```csharp
  public interface IErpPriceWriter
  {
      Task SetPriceWithoutVatAsync(int erpItemId, decimal priceWithoutVat, CancellationToken ct);
  }
  ```
  and `ProductPriceErp.ErpItemId` (`int`), which Task 6 uses to address Flexi writes.

- [ ] **Step 1: Write the failing writer tests**

Create `backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiProductPriceWriterTests.cs`:

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Flexi.Price;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Client;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

public class FlexiProductPriceWriterTests
{
    private static (FlexiProductPriceWriter Writer, List<HttpRequestMessage> Requests, List<string> Bodies) Create(
        HttpStatusCode status = HttpStatusCode.OK, string responseBody = "{}")
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var handler = new StubHandler(requests, bodies, status, responseBody);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var settings = new FlexiBeeSettings { Server = "https://petra-tesarikova.flexibee.eu", Company = "anela" };

        return (new FlexiProductPriceWriter(factory, settings, NullLogger<FlexiProductPriceWriter>.Instance),
                requests, bodies);
    }

    [Fact]
    public async Task addresses_the_write_by_internal_cenik_id_never_by_code()
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        await writer.SetPriceWithoutVatAsync(147, 157.02m, CancellationToken.None);

        // Assert
        requests.Should().ContainSingle();
        requests[0].Method.Should().Be(HttpMethod.Put);
        requests[0].RequestUri!.AbsolutePath.Should().Be("/c/anela/cenik/147.json");
        requests[0].RequestUri!.ToString().Should().NotContain("code:");
    }

    [Fact]
    public async Task sends_cena_zakl_in_invariant_culture_with_two_decimals()
    {
        // Arrange
        var (writer, _, bodies) = Create();

        // Act
        await writer.SetPriceWithoutVatAsync(147, 157.019m, CancellationToken.None);

        // Assert
        bodies[0].Should().Contain("\"cenaZakl\":\"157.02\"");
        bodies[0].Should().Contain("winstrom").And.Contain("cenik");
    }

    [Fact]
    public async Task never_writes_the_purchase_price()
    {
        // Arrange
        var (writer, _, bodies) = Create();

        // Act
        await writer.SetPriceWithoutVatAsync(147, 157.02m, CancellationToken.None);

        // Assert
        bodies[0].Should().NotContain("cenanakup").And.NotContain("cenaNakup");
    }

    [Fact]
    public async Task rejects_a_non_positive_erp_item_id_without_calling_flexi()
    {
        // Arrange
        var (writer, requests, _) = Create();

        // Act
        var act = () => writer.SetPriceWithoutVatAsync(0, 157.02m, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task throws_with_the_response_body_when_flexi_rejects_the_write()
    {
        // Arrange
        var (writer, _, _) = Create(HttpStatusCode.BadRequest, "{\"winstrom\":{\"success\":\"false\"}}");

        // Act
        var act = () => writer.SetPriceWithoutVatAsync(147, 157.02m, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).And.Message.Should().Contain("success");
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests;
        private readonly List<string> _bodies;
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public StubHandler(List<HttpRequestMessage> requests, List<string> bodies,
                           HttpStatusCode status, string responseBody)
        {
            _requests = requests;
            _bodies = bodies;
            _status = status;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            if (request.Content is not null)
            {
                _bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — `FlexiProductPriceWriter` does not exist.

> If `FlexiBeeSettings` exposes different property names than `Server`/`Company`, adjust the test and the writer to match the SDK version in use (`Rem.FlexiBeeSDK.Client` 0.1.139) — read the type before writing the implementation.

- [ ] **Step 3: Write the writer and add `ErpItemId`**

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPriceWriter.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Writes the base selling price to the ERP's price list.</summary>
public interface IErpPriceWriter
{
    /// <param name="erpItemId">Internal ceník id (<c>idcenik</c>). Addressing by code would create records.</param>
    Task SetPriceWithoutVatAsync(int erpItemId, decimal priceWithoutVat, CancellationToken ct);
}
```

Add to `backend/src/Anela.Heblo.Domain/Features/Catalog/Price/ProductPriceErp.cs`:

```csharp
    /// <summary>Internal ERP price list id (Flexi <c>idcenik</c>). 0 when unknown.</summary>
    public int ErpItemId { get; set; }
```

In `FlexiProductPriceErpClient.GetAllAsync`, add to the projection (around line 96):

```csharp
            ErpItemId = s.ProductId,
```

`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceWriter.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;

namespace Anela.Heblo.Adapters.Flexi.Price;

/// <summary>
/// Writes <c>cenaZakl</c> (base price, excluding VAT) to a Flexi ceník item.
///
/// Addressed by the internal numeric id only: Flexi does not distinguish create from
/// update, so a PUT to <c>cenik/code:UNKNOWN.json</c> silently creates a new item.
/// </summary>
public class FlexiProductPriceWriter : IErpPriceWriter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FlexiBeeSettings _connection;
    private readonly ILogger<FlexiProductPriceWriter> _logger;

    public FlexiProductPriceWriter(
        IHttpClientFactory httpClientFactory,
        FlexiBeeSettings connection,
        ILogger<FlexiProductPriceWriter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _connection = connection;
        _logger = logger;
    }

    public async Task SetPriceWithoutVatAsync(int erpItemId, decimal priceWithoutVat, CancellationToken ct)
    {
        if (erpItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(erpItemId),
                "A Flexi ceník id is required. Writing by code would create a new price list item.");
        }

        var payload = new
        {
            winstrom = new
            {
                cenik = new
                {
                    cenaZakl = priceWithoutVat.ToString("F2", CultureInfo.InvariantCulture),
                },
            },
        };

        var url = $"{_connection.Server.TrimEnd('/')}/c/{_connection.Company}/cenik/{erpItemId}.json";

        using var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Flexi ceník write failed for id {erpItemId} with {(int)response.StatusCode}: {body}");
        }

        _logger.LogInformation(
            "Updated Flexi ceník {ErpItemId} base price to {Price}", erpItemId, priceWithoutVat);
    }
}
```

Register in `FlexiAdapterServiceCollectionExtensions`, next to the other Flexi clients:

```csharp
        services.AddScoped<IErpPriceWriter, FlexiProductPriceWriter>();
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Debug --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~FlexiProductPriceWriterTests"
```
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/ProductPricing/IErpPriceWriter.cs \
        backend/src/Anela.Heblo.Domain/Features/Catalog/Price/ProductPriceErp.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi \
        backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiProductPriceWriterTests.cs
git commit -m "feat: Flexi cenik price writer addressed by internal item id"
```

---

### Task 5: Retire the CSV e-shop price path

The catalog's e-shop price read moves from the windows-1250 CSV export to the REST snapshot, so the catalog and the sync see the same numbers. The dead `SetAllAsync` CSV writer goes with it.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Price/IProductPriceEshopClient.cs` (drop `SetAllAsync`)
- Delete: `backend/src/Anela.Heblo.Domain/Features/Catalog/Price/SetProductPricesResultDto.cs`
- Delete: `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/Price/ShoptetPriceClient.cs`
- Delete: `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/Price/ProductPriceOptions.cs`
- Delete: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetPriceClientTests.cs`
- Delete: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetPriceClientIntegrationTests.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetEshopPriceClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/HebloShoptetAdapterModule.cs` (drop the old registration)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` (register the new one)
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetEshopPriceClientTests.cs`

**Interfaces:**
- Consumes: `IEshopPriceListClient` from Task 3.
- Produces: `IProductPriceEshopClient` with a single method `Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken ct)` — unchanged signature, so `CatalogDataRefreshService:274` and `CatalogMergeService` need no edits.

- [ ] **Step 1: Write the failing adapter test**

Create `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetEshopPriceClientTests.cs`:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.Pricing;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetEshopPriceClientTests
{
    [Fact]
    public async Task maps_price_list_entries_to_catalog_eshop_prices()
    {
        // Arrange
        var priceList = new Mock<IEshopPriceListClient>();
        priceList
            .Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["OCH001030"] = 190.00m });
        var vatRates = new Mock<IProductVatRateProvider>();
        vatRates
            .Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["OCH001030"] = 21m });
        var client = new ShoptetEshopPriceClient(priceList.Object, vatRates.Object);

        // Act
        var prices = (await client.GetAllAsync(CancellationToken.None)).ToList();

        // Assert
        prices.Should().ContainSingle();
        prices[0].ProductCode.Should().Be("OCH001030");
        prices[0].PriceWithVat.Should().Be(190.00m);
        prices[0].PriceWithoutVat.Should().Be(157.02m);
    }

    [Fact]
    public async Task falls_back_to_the_standard_vat_rate_when_the_erp_rate_is_unknown()
    {
        // Arrange
        var priceList = new Mock<IEshopPriceListClient>();
        priceList
            .Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["NEW001"] = 121.00m });
        var vatRates = new Mock<IProductVatRateProvider>();
        vatRates
            .Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());
        var client = new ShoptetEshopPriceClient(priceList.Object, vatRates.Object);

        // Act
        var prices = (await client.GetAllAsync(CancellationToken.None)).ToList();

        // Assert
        prices[0].PriceWithoutVat.Should().Be(100.00m);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — `ShoptetEshopPriceClient` and `IProductVatRateProvider` do not exist.

- [ ] **Step 3: Write the replacement and delete the CSV path**

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/VatRateCalculator.cs` — the single home for
this formula; Task 6 uses it too, so do not re-derive it there:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

public static class VatRateCalculator
{
    public const decimal StandardVatRate = 21m;

    /// <summary>Recovers the VAT rate from a price pair, falling back to the standard rate.</summary>
    public static decimal FromPrices(decimal priceWithVat, decimal priceWithoutVat)
    {
        if (priceWithoutVat <= 0)
        {
            return StandardVatRate;
        }

        return Math.Round((priceWithVat / priceWithoutVat - 1) * 100m, 0);
    }
}
```

`backend/src/Anela.Heblo.Domain/Features/ProductPricing/IProductVatRateProvider.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>VAT rate per product code, sourced from the ERP.</summary>
public interface IProductVatRateProvider
{
    Task<IReadOnlyDictionary<string, decimal>> GetVatRatesAsync(CancellationToken ct);
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductVatRateProvider.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Adapters.Flexi.Price;

public class FlexiProductVatRateProvider : IProductVatRateProvider
{
    private readonly IProductPriceErpClient _erpClient;

    public FlexiProductVatRateProvider(IProductPriceErpClient erpClient)
    {
        _erpClient = erpClient;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetVatRatesAsync(CancellationToken ct)
    {
        var prices = await _erpClient.GetAllAsync(forceReload: false, ct);

        return prices
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode) && p.PriceWithoutVat > 0)
            .GroupBy(p => p.ProductCode)
            .ToDictionary(
                g => g.Key,
                g => VatRateCalculator.FromPrices(g.First().PriceWithVat, g.First().PriceWithoutVat),
                StringComparer.OrdinalIgnoreCase);
    }
}
```

Register it in `FlexiAdapterServiceCollectionExtensions`:

```csharp
        services.AddScoped<IProductVatRateProvider, FlexiProductVatRateProvider>();
```

`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Pricing/ShoptetEshopPriceClient.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Adapters.ShoptetApi.Pricing;

/// <summary>
/// Catalog-facing e-shop price read. Replaces the former CSV product-export client so
/// the catalog and the price sync observe the same source.
/// </summary>
public class ShoptetEshopPriceClient : IProductPriceEshopClient
{
    private readonly IEshopPriceListClient _priceListClient;
    private readonly IProductVatRateProvider _vatRateProvider;

    public ShoptetEshopPriceClient(
        IEshopPriceListClient priceListClient,
        IProductVatRateProvider vatRateProvider)
    {
        _priceListClient = priceListClient;
        _vatRateProvider = vatRateProvider;
    }

    public async Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken cancellationToken)
    {
        var prices = await _priceListClient.GetPricesWithVatAsync(cancellationToken);
        var vatRates = await _vatRateProvider.GetVatRatesAsync(cancellationToken);

        return prices.Select(entry =>
        {
            var vatRate = vatRates.TryGetValue(entry.Key, out var rate) ? rate : VatRateCalculator.StandardVatRate;

            return new ProductPriceEshop
            {
                ProductCode = entry.Key,
                PriceWithVat = entry.Value,
                PriceWithoutVat = Math.Round(entry.Value / (1 + vatRate / 100m), 2, MidpointRounding.AwayFromZero),
                PurchasePrice = null,
            };
        }).ToList();
    }
}
```

Trim `IProductPriceEshopClient` to the read method only:

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.Price;

public interface IProductPriceEshopClient
{
    Task<IEnumerable<ProductPriceEshop>> GetAllAsync(CancellationToken cancellationToken);
}
```

Then delete the files listed under **Files**, register the replacement in `AddShoptetApiAdapter`:

```csharp
        services.AddScoped<IProductPriceEshopClient, ShoptetEshopPriceClient>();
```

and remove the `ShoptetPriceClient` / `ProductPriceOptions` registration from `HebloShoptetAdapterModule.cs`. Delete the now-orphaned `ProductPriceOptions` configuration section from `appsettings*.json` if present.

> `ProductPriceEshop.PurchasePrice` is set to `null` because Shoptet's `buyPrice` is not read. `CatalogAggregate.CurrentPurchasePrice` already falls back to `ErpPrice?.PurchasePrice`, so nothing downstream loses data.

- [ ] **Step 4: Run the full backend suite**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test Anela.Heblo.sln -c Debug --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
```
Expected: PASS. Existing catalog tests that construct `IProductPriceEshopClient` mocks with `SetAllAsync` must be updated to drop that setup — fix them here, in this task.

- [ ] **Step 5: Commit**

```bash
git add -A backend docs
git commit -m "refactor: read e-shop prices from the Shoptet REST API and drop the CSV path"
```

---

### Task 6: Sync service and seeding

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/IProductPriceSyncService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/ProductPriceSyncService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/PriceSyncRunResult.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceSyncServiceTests.cs`

**Interfaces:**
- Consumes: `IProductPriceRepository` (Task 2), `IEshopPriceListClient` (Task 3), `IErpPriceWriter` + `ProductPriceErp.ErpItemId` (Task 4), `PriceSyncDecider` (Task 1), `ICatalogRepository.GetAllAsync` for the in-scope product list (assumption A3), `IProductPriceErpClient` for ERP prices and VAT.
- Produces:
  ```csharp
  public interface IProductPriceSyncService
  {
      Task<PriceSyncRunResult> SyncAsync(CancellationToken ct);
  }

  public class PriceSyncRunResult
  {
      public int Pushed { get; set; }
      public int Conflicts { get; set; }
      public int Failed { get; set; }
      public int Seeded { get; set; }
      public int Unchanged { get; set; }
  }
  ```

- [ ] **Step 1: Write the failing sync tests**

Create `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceSyncServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
// ProductType and CatalogAggregate come from Anela.Heblo.Domain.Features.Catalog
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ProductPriceSyncServiceTests
{
    private readonly Mock<IProductPriceRepository> _repository = new();
    private readonly Mock<IEshopPriceListClient> _eshop = new();
    private readonly Mock<IErpPriceWriter> _erpWriter = new();
    private readonly Mock<IProductPriceErpClient> _erpReader = new();
    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly List<ProductPriceSyncState> _savedStates = new();

    private bool _inScopeConfigured;

    private ProductPriceSyncService CreateService()
    {
        if (!_inScopeConfigured)
        {
            GivenInScope(("A", ProductType.Product), ("B", ProductType.Product));
        }

        _repository
            .Setup(r => r.UpsertSyncStateAsync(It.IsAny<ProductPriceSyncState>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceSyncState, CancellationToken>((s, _) => _savedStates.Add(s))
            .Returns(Task.CompletedTask);

        return new ProductPriceSyncService(
            _repository.Object,
            _eshop.Object,
            _erpWriter.Object,
            _erpReader.Object,
            _catalog.Object,
            NullLogger<ProductPriceSyncService>.Instance);
    }

    private void GivenInScope(params (string Code, ProductType Type)[] products)
    {
        _inScopeConfigured = true;
        _catalog
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products
                .Select(p => new CatalogAggregate { ProductCode = p.Code, Type = p.Type })
                .ToList());
    }

    private void GivenHebloPrice(string code, decimal priceWithVat) =>
        _repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>
            {
                new() { ProductCode = code, PriceWithVat = priceWithVat, VatRate = 21m },
            });

    private void GivenSyncState(string code, PriceSyncTarget target, decimal? lastPushed) =>
        _repository
            .Setup(r => r.GetSyncStatesAsync(target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>
            {
                new() { ProductCode = code, Target = target, LastPushedPriceWithVat = lastPushed },
            });

    private void GivenErp(string code, int erpItemId, decimal priceWithVat) =>
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new()
                {
                    ProductCode = code,
                    ErpItemId = erpItemId,
                    PriceWithVat = priceWithVat,
                    PriceWithoutVat = Math.Round(priceWithVat / 1.21m, 2, MidpointRounding.AwayFromZero),
                },
            });

    [Fact]
    public async Task pushes_to_both_targets_when_only_heblo_changed()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Pushed.Should().Be(2);
        _eshop.Verify(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()), Times.Once);
        _erpWriter.Verify(w => w.SetPriceWithoutVatAsync(147, 173.55m, It.IsAny<CancellationToken>()), Times.Once);
        _savedStates.Should().OnlyContain(s => s.Status == PriceSyncStatus.InSync);
        _savedStates.Should().OnlyContain(s => s.LastPushedPriceWithVat == 210.00m);
    }

    [Fact]
    public async Task records_a_conflict_and_pushes_nothing_when_the_remote_moved()
    {
        // Arrange
        GivenHebloPrice("A", 190.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 175.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Conflicts.Should().Be(1);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        var shoptetState = _savedStates.Single(s => s.Target == PriceSyncTarget.Shoptet);
        shoptetState.Status.Should().Be(PriceSyncStatus.Conflict);
        shoptetState.RemoteValueAtConflict.Should().Be(175.00m);
    }

    [Fact]
    public async Task a_conflict_on_one_target_does_not_block_the_other()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 175.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Conflicts.Should().Be(1);
        result.Pushed.Should().Be(1);
        _erpWriter.Verify(w => w.SetPriceWithoutVatAsync(147, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task seeds_heblo_from_shoptet_and_conflicts_flexi_when_the_two_disagree()
    {
        // Arrange
        _repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProductPrice>());
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: null);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: null);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 175.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Seeded.Should().Be(1);
        result.Conflicts.Should().Be(1);
        _repository.Verify(
            r => r.UpsertAsync(It.Is<ProductPrice>(p => p.ProductCode == "A" && p.PriceWithVat == 190.00m),
                               It.IsAny<CancellationToken>()),
            Times.Once);
        _savedStates.Single(s => s.Target == PriceSyncTarget.Flexi).Status.Should().Be(PriceSyncStatus.Conflict);
    }

    [Fact]
    public async Task marks_failed_when_flexi_has_no_internal_item_id_and_never_creates_the_record()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 210.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 210.00m });
        GivenErp("A", erpItemId: 0, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Failed.Should().Be(1);
        _erpWriter.Verify(
            w => w.SetPriceWithoutVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _savedStates.Single(s => s.Target == PriceSyncTarget.Flexi).LastError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task one_products_push_failure_does_not_abort_the_run()
    {
        // Arrange
        _repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>
            {
                new() { ProductCode = "A", PriceWithVat = 210.00m, VatRate = 21m },
                new() { ProductCode = "B", PriceWithVat = 310.00m, VatRate = 21m },
            });
        // Mimic production: GetSyncStatesAsync(target) returns only that target's rows.
        // A single shared list would hand the SAME instances to both passes, so the Flexi
        // pass would mutate the very objects the Shoptet assertion inspects.
        _repository
            .Setup(r => r.GetSyncStatesAsync(It.IsAny<PriceSyncTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PriceSyncTarget target, CancellationToken _) => new List<ProductPriceSyncState>
            {
                new() { ProductCode = "A", Target = target, LastPushedPriceWithVat = 190.00m },
                new() { ProductCode = "B", Target = target, LastPushedPriceWithVat = 290.00m },
            });
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m, ["B"] = 290.00m });
        _eshop.Setup(c => c.SetPriceWithVatAsync("A", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("422 Invalid price"));
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>());
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        _eshop.Verify(c => c.SetPriceWithVatAsync("B", 310.00m, It.IsAny<CancellationToken>()), Times.Once);
        var failed = _savedStates.Single(s => s.ProductCode == "A" && s.Target == PriceSyncTarget.Shoptet);
        failed.Status.Should().Be(PriceSyncStatus.Failed);
        failed.LastError.Should().Contain("422");
    }

    [Fact]
    public async Task never_syncs_materials_or_semi_products()
    {
        // Arrange
        GivenInScope(("MAT001", ProductType.Material), ("SEMI001", ProductType.SemiProduct));
        _repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>
            {
                new() { ProductCode = "MAT001", PriceWithVat = 10.00m, VatRate = 21m },
            });
        _repository
            .Setup(r => r.GetSyncStatesAsync(It.IsAny<PriceSyncTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>());
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["SEMI001"] = 5.00m });
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>());
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        _savedStates.Should().BeEmpty();
        _eshop.Verify(
            c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.Pushed.Should().Be(0);
        result.Seeded.Should().Be(0);
    }

    [Fact]
    public async Task leaves_states_untouched_when_the_bulk_read_of_a_target_fails()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        _savedStates.Should().NotContain(s => s.Target == PriceSyncTarget.Shoptet);
        result.Pushed.Should().Be(1); // Flexi still ran
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — `ProductPriceSyncService` does not exist.

- [ ] **Step 3: Write the sync service**

`backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/PriceSyncRunResult.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ProductPricing.Services;

public class PriceSyncRunResult
{
    public int Pushed { get; set; }
    public int Conflicts { get; set; }
    public int Failed { get; set; }
    public int Seeded { get; set; }
    public int Unchanged { get; set; }
}
```

`backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/IProductPriceSyncService.cs`:

```csharp
namespace Anela.Heblo.Application.Features.ProductPricing.Services;

public interface IProductPriceSyncService
{
    Task<PriceSyncRunResult> SyncAsync(CancellationToken ct);
}
```

`backend/src/Anela.Heblo.Application/Features/ProductPricing/Services/ProductPriceSyncService.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ProductPricing.Services;

public class ProductPriceSyncService : IProductPriceSyncService
{
    private const string SeedModifiedBy = "price-sync";

    /// <summary>Assumption A3: only sellable types carry a retail price.</summary>
    private static readonly ProductType[] PricedProductTypes =
    {
        ProductType.Product, ProductType.Goods, ProductType.Set,
    };

    private readonly IProductPriceRepository _repository;
    private readonly IEshopPriceListClient _eshopClient;
    private readonly IErpPriceWriter _erpWriter;
    private readonly IProductPriceErpClient _erpReader;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<ProductPriceSyncService> _logger;

    public ProductPriceSyncService(
        IProductPriceRepository repository,
        IEshopPriceListClient eshopClient,
        IErpPriceWriter erpWriter,
        IProductPriceErpClient erpReader,
        ICatalogRepository catalogRepository,
        ILogger<ProductPriceSyncService> logger)
    {
        _repository = repository;
        _eshopClient = eshopClient;
        _erpWriter = erpWriter;
        _erpReader = erpReader;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<PriceSyncRunResult> SyncAsync(CancellationToken ct)
    {
        var result = new PriceSyncRunResult();
        var prices = (await _repository.GetAllAsync(ct)).ToDictionary(p => p.ProductCode, StringComparer.OrdinalIgnoreCase);

        var erpPrices = await ReadErpPricesAsync(ct);
        var inScope = await ReadInScopeProductCodesAsync(ct);

        await SyncTargetAsync(PriceSyncTarget.Shoptet, prices, erpPrices, inScope, result, ct);
        await SyncTargetAsync(PriceSyncTarget.Flexi, prices, erpPrices, inScope, result, ct);

        await _repository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Price sync finished: {Pushed} pushed, {Conflicts} conflicts, {Failed} failed, {Seeded} seeded, {Unchanged} unchanged",
            result.Pushed, result.Conflicts, result.Failed, result.Seeded, result.Unchanged);

        return result;
    }

    private async Task<IReadOnlySet<string>> ReadInScopeProductCodesAsync(CancellationToken ct)
    {
        var catalog = await _catalogRepository.GetAllAsync(ct);

        return catalog
            .Where(p => PricedProductTypes.Contains(p.Type))
            .Select(p => p.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyDictionary<string, ProductPriceErp>> ReadErpPricesAsync(CancellationToken ct)
    {
        var erpPrices = await _erpReader.GetAllAsync(forceReload: false, ct);

        return erpPrices
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task SyncTargetAsync(
        PriceSyncTarget target,
        IDictionary<string, ProductPrice> prices,
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices,
        IReadOnlySet<string> inScopeProductCodes,
        PriceSyncRunResult result,
        CancellationToken ct)
    {
        IReadOnlyDictionary<string, decimal> remotePrices;
        try
        {
            remotePrices = target == PriceSyncTarget.Shoptet
                ? await _eshopClient.GetPricesWithVatAsync(ct)
                : erpPrices.ToDictionary(e => e.Key, e => e.Value.PriceWithVat, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // A failed bulk read tells us nothing about individual products. Leave every
            // state untouched rather than mass-marking them Failed.
            _logger.LogError(ex, "Price sync skipped for {Target}: bulk read failed", target);
            return;
        }

        var states = (await _repository.GetSyncStatesAsync(target, ct))
            .ToDictionary(s => s.ProductCode, StringComparer.OrdinalIgnoreCase);

        // Materials and semi-products have no selling price and are never synced (A3).
        var productCodes = prices.Keys
            .Union(remotePrices.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(inScopeProductCodes.Contains)
            .ToList();

        foreach (var productCode in productCodes)
        {
            ct.ThrowIfCancellationRequested();

            states.TryGetValue(productCode, out var state);
            state ??= new ProductPriceSyncState { ProductCode = productCode, Target = target };

            // The row came from a query already filtered to this target; asserting it keeps
            // the target correct even if a caller hands back a loosely-populated row.
            state.Target = target;

            prices.TryGetValue(productCode, out var hebloPrice);
            remotePrices.TryGetValue(productCode, out var remoteValue);
            var remote = remotePrices.ContainsKey(productCode) ? remoteValue : (decimal?)null;

            var decision = PriceSyncDecider.Decide(
                hebloPrice?.PriceWithVat ?? 0m, state.LastPushedPriceWithVat, remote);

            await ApplyDecisionAsync(decision, target, productCode, hebloPrice, prices, erpPrices, state, result, ct);
        }
    }

    private async Task ApplyDecisionAsync(
        PriceSyncDecision decision,
        PriceSyncTarget target,
        string productCode,
        ProductPrice? hebloPrice,
        IDictionary<string, ProductPrice> prices,
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices,
        ProductPriceSyncState state,
        PriceSyncRunResult result,
        CancellationToken ct)
    {
        switch (decision.Action)
        {
            case PriceSyncAction.None:
                result.Unchanged++;
                return;

            case PriceSyncAction.MissingRemote:
                result.Failed++;
                await FailAsync(state, $"Product {productCode} does not exist in {target}.", ct);
                return;

            case PriceSyncAction.Seed:
                await SeedAsync(decision, target, productCode, hebloPrice, prices, erpPrices, state, result, ct);
                return;

            case PriceSyncAction.Conflict:
                result.Conflicts++;
                state.Status = PriceSyncStatus.Conflict;
                state.RemoteValueAtConflict = decision.RemoteValue;
                state.ConflictDetectedAt = DateTime.UtcNow;
                state.LastError = null;
                await _repository.UpsertSyncStateAsync(state, ct);
                return;

            case PriceSyncAction.Push:
                await PushAsync(decision, target, productCode, hebloPrice, erpPrices, state, result, ct);
                return;
        }
    }

    private async Task SeedAsync(
        PriceSyncDecision decision,
        PriceSyncTarget target,
        string productCode,
        ProductPrice? hebloPrice,
        IDictionary<string, ProductPrice> prices,
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices,
        ProductPriceSyncState state,
        PriceSyncRunResult result,
        CancellationToken ct)
    {
        // Shoptet is today's retail truth, so it seeds the master value. Flexi only ever
        // adopts the seed when it already agrees; otherwise it becomes a conflict for a
        // human to reconcile.
        if (target == PriceSyncTarget.Shoptet)
        {
            result.Seeded++;
            erpPrices.TryGetValue(productCode, out var erp);

            var seeded = new ProductPrice
            {
                ProductCode = productCode,
                PriceWithVat = decision.RemoteValue!.Value,
                VatRate = DeriveVatRate(erp),
                ModifiedAt = DateTime.UtcNow,
                ModifiedBy = SeedModifiedBy,
            };

            await _repository.UpsertAsync(seeded, ct);

            // Shoptet is synced first, so the seeded master value must be visible to the
            // Flexi pass in this same run — otherwise Flexi would silently adopt its own
            // value instead of raising the reconciliation conflict.
            prices[productCode] = seeded;

            state.LastPushedPriceWithVat = decision.RemoteValue;
            state.LastPushedAt = DateTime.UtcNow;
            state.Status = PriceSyncStatus.InSync;
            await _repository.UpsertSyncStateAsync(state, ct);
            return;
        }

        var seededPrice = hebloPrice?.PriceWithVat;
        if (seededPrice is null || Math.Round(seededPrice.Value, 2) == Math.Round(decision.RemoteValue!.Value, 2))
        {
            state.LastPushedPriceWithVat = decision.RemoteValue;
            state.LastPushedAt = DateTime.UtcNow;
            state.Status = PriceSyncStatus.InSync;
            await _repository.UpsertSyncStateAsync(state, ct);
            return;
        }

        result.Conflicts++;
        state.Status = PriceSyncStatus.Conflict;
        state.RemoteValueAtConflict = decision.RemoteValue;
        state.ConflictDetectedAt = DateTime.UtcNow;
        await _repository.UpsertSyncStateAsync(state, ct);
    }

    private async Task PushAsync(
        PriceSyncDecision decision,
        PriceSyncTarget target,
        string productCode,
        ProductPrice? hebloPrice,
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices,
        ProductPriceSyncState state,
        PriceSyncRunResult result,
        CancellationToken ct)
    {
        try
        {
            if (target == PriceSyncTarget.Shoptet)
            {
                await _eshopClient.SetPriceWithVatAsync(productCode, decision.PriceToPush!.Value, ct);
            }
            else
            {
                if (!erpPrices.TryGetValue(productCode, out var erp) || erp.ErpItemId <= 0)
                {
                    result.Failed++;
                    await FailAsync(
                        state,
                        $"No Flexi ceník id known for {productCode}; refusing to write by code (Flexi would create a new item).",
                        ct);
                    return;
                }

                var priceWithoutVat = hebloPrice?.PriceWithoutVat
                    ?? Math.Round(decision.PriceToPush!.Value / (1 + DeriveVatRate(erp) / 100m), 2, MidpointRounding.AwayFromZero);

                await _erpWriter.SetPriceWithoutVatAsync(erp.ErpItemId, priceWithoutVat, ct);
            }

            result.Pushed++;
            state.LastPushedPriceWithVat = decision.PriceToPush;
            state.LastPushedAt = DateTime.UtcNow;
            state.Status = PriceSyncStatus.InSync;
            state.RemoteValueAtConflict = null;
            state.ConflictDetectedAt = null;
            state.LastError = null;
            await _repository.UpsertSyncStateAsync(state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push {ProductCode} to {Target}", productCode, target);
            result.Failed++;
            await FailAsync(state, ex.Message, ct);
        }
    }

    private async Task FailAsync(ProductPriceSyncState state, string error, CancellationToken ct)
    {
        state.Status = PriceSyncStatus.Failed;
        state.LastError = error;
        await _repository.UpsertSyncStateAsync(state, ct);
    }

    private static decimal DeriveVatRate(ProductPriceErp? erp) =>
        erp is null
            ? VatRateCalculator.StandardVatRate
            : VatRateCalculator.FromPrices(erp.PriceWithVat, erp.PriceWithoutVat);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Debug --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ProductPriceSyncServiceTests"
```
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ProductPricing \
        backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceSyncServiceTests.cs
git commit -m "feat: product price sync service with drift detection and seeding"
```

---

### Task 7: Recurring job and error codes

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Infrastructure/Jobs/ProductPriceSyncJob.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/ProductPricingModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (call `AddProductPricingModule()` next to line 103)
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (add the 36XX block)
- Modify: `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs` (add the 36XX bucket line)
- Modify: `frontend/src/i18n.ts` (Czech translations for the new codes)
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceSyncJobTests.cs`

**Interfaces:**
- Consumes: `IProductPriceSyncService` (Task 6), `IRecurringJob`, `IRecurringJobStatusChecker`.
- Produces: job name `"product-price-sync"`; `ErrorCodes.ProductPriceNotFound = 3601`, `ProductPriceSyncConflict = 3602`, `ProductPriceInvalidValue = 3603`, `ProductPriceConflictNotFound = 3604`.

- [ ] **Step 1: Write the failing job test**

Create `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ProductPriceSyncJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ProductPriceSyncJobTests
{
    private readonly Mock<IProductPriceSyncService> _syncService = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private ProductPriceSyncJob CreateJob() =>
        new(_syncService.Object, _statusChecker.Object, NullLogger<ProductPriceSyncJob>.Instance);

    [Fact]
    public async Task runs_the_sync_when_the_job_is_enabled()
    {
        // Arrange
        _statusChecker.Setup(c => c.IsJobEnabledAsync("product-price-sync")).ReturnsAsync(true);
        _syncService.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new PriceSyncRunResult());

        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        _syncService.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task skips_the_sync_when_the_job_is_disabled()
    {
        // Arrange
        _statusChecker.Setup(c => c.IsJobEnabledAsync("product-price-sync")).ReturnsAsync(false);

        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        _syncService.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task rethrows_so_hangfire_can_retry()
    {
        // Arrange
        _statusChecker.Setup(c => c.IsJobEnabledAsync("product-price-sync")).ReturnsAsync(true);
        _syncService.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var act = () => CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void exposes_stable_job_metadata()
    {
        // Arrange & Act
        var metadata = CreateJob().Metadata;

        // Assert
        metadata.JobName.Should().Be("product-price-sync");
        metadata.CronExpression.Should().NotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — `ProductPriceSyncJob` does not exist.

- [ ] **Step 3: Write the job, the module and the error codes**

`backend/src/Anela.Heblo.Application/Features/ProductPricing/Infrastructure/Jobs/ProductPriceSyncJob.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ProductPricing.Infrastructure.Jobs;

public class ProductPriceSyncJob : IRecurringJob
{
    private readonly IProductPriceSyncService _syncService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<ProductPriceSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "product-price-sync",
        DisplayName = "Product Price Sync",
        Description = "Pushes Heblo retail prices to Shoptet and Flexi and detects downstream drift",
        CronExpression = "0 * * * *", // Hourly — a price edit reaches both systems within the hour
        DefaultIsEnabled = true,
    };

    public ProductPriceSyncJob(
        IProductPriceSyncService syncService,
        IRecurringJobStatusChecker statusChecker,
        ILogger<ProductPriceSyncJob> logger)
    {
        _syncService = syncService;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);

        try
        {
            var result = await _syncService.SyncAsync(cancellationToken);

            _logger.LogInformation(
                "{JobName} completed: {Pushed} pushed, {Conflicts} conflicts, {Failed} failed",
                Metadata.JobName, result.Pushed, result.Conflicts, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw; // Re-throw to let Hangfire handle retry logic
        }
    }
}
```

`backend/src/Anela.Heblo.Application/Features/ProductPricing/ProductPricingModule.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Persistence.ProductPricing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ProductPricing;

public static class ProductPricingModule
{
    public static IServiceCollection AddProductPricingModule(this IServiceCollection services)
    {
        services.AddScoped<IProductPriceRepository, ProductPriceRepository>();
        services.AddScoped<IProductPriceSyncService, ProductPriceSyncService>();

        services.AddScoped<ProductPriceSyncJob>();

        // Validator registrations are added by Tasks 8 and 9 as their use cases land.
        // There is no AddValidatorsFromAssembly in this project — each one is explicit.

        return services;
    }
}
```

> This module registers no validators yet, so the tree compiles at this commit. Tasks 8 and 9 add their own validator + `ValidationBehavior` pairs here as they create the use cases.

Add to `ApplicationModule.cs` beside the other module calls:

```csharp
        services.AddProductPricingModule();
```

Add to `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, after the 35XX block:

```csharp
    // Product Pricing (36XX)
    ProductPriceNotFound = 3601,
    ProductPriceSyncConflict = 3602,
    ProductPriceInvalidValue = 3603,
    ProductPriceConflictNotFound = 3604,
```

Add to `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs`, after the `mindMapErrors` line:

```csharp
        var productPricingErrors = errorCodes.Where(code => code >= 3600 && code < 3700).ToList(); // 36XX range (Product Pricing)
```

and an assertion beside the others:

```csharp
        Assert.True(productPricingErrors.Count > 0, "Should have Product Pricing errors in 36XX range");
```

Add to `frontend/src/i18n.ts`, beside the other error-code translations (around line 345):

```typescript
        ProductPriceNotFound: "Cena produktu nebyla nalezena",
        ProductPriceSyncConflict: "Cena byla mezitím změněna v jiném systému, vyřešte konflikt",
        ProductPriceInvalidValue: "Neplatná cena",
        ProductPriceConflictNotFound: "Konflikt cen nebyl nalezen",
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Debug --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ProductPriceSyncJobTests|FullyQualifiedName~ErrorHandlingTests"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests frontend/src/i18n.ts
git commit -m "feat: product price sync recurring job and error codes"
```

---

### Task 8: Read and edit use cases

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/ProductPriceDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/GetProductPrices/{GetProductPricesRequest,GetProductPricesResponse,GetProductPricesHandler}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/SetProductPrice/{SetProductPriceRequest,SetProductPriceResponse,SetProductPriceHandler,SetProductPriceRequestValidator}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/TriggerPriceSync/{TriggerPriceSyncRequest,TriggerPriceSyncResponse,TriggerPriceSyncHandler}.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/ProductPricingController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/SetProductPriceHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/GetProductPricesHandlerTests.cs`

**Interfaces:**
- Consumes: `IProductPriceRepository`, `IProductPriceSyncService`, `ICatalogRepository.GetAllAsync` (product names for the grid), `ICurrentUserService` (follow whatever the neighbouring handlers use for the current user id — read `PackingMaterials` handlers first).
- Produces: `SetProductPriceRequest { string ProductCode; decimal PriceWithVat; }`, `SetProductPriceResponse : BaseResponse`, `GetProductPricesResponse : BaseResponse { List<ProductPriceDto> Prices; }`, `TriggerPriceSyncResponse : BaseResponse { int Pushed; int Conflicts; int Failed; }`.

- [ ] **Step 1: Write the failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/ProductPricing/SetProductPriceHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class SetProductPriceHandlerTests
{
    private readonly Mock<IProductPriceRepository> _repository = new();

    [Fact]
    public async Task stores_the_new_price_and_marks_both_targets_pending()
    {
        // Arrange
        _repository
            .Setup(r => r.GetAsync("OCH001030", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPrice { ProductCode = "OCH001030", PriceWithVat = 190.00m, VatRate = 21m });
        var savedStates = new List<ProductPriceSyncState>();
        _repository
            .Setup(r => r.UpsertSyncStateAsync(It.IsAny<ProductPriceSyncState>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceSyncState, CancellationToken>((s, _) => savedStates.Add(s))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.GetSyncStateAsync(It.IsAny<string>(), It.IsAny<PriceSyncTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPriceSyncState { ProductCode = "OCH001030", Status = PriceSyncStatus.InSync });
        var handler = new SetProductPriceHandler(_repository.Object);

        // Act
        var response = await handler.Handle(
            new SetProductPriceRequest { ProductCode = "OCH001030", PriceWithVat = 210.00m },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repository.Verify(
            r => r.UpsertAsync(It.Is<ProductPrice>(p => p.PriceWithVat == 210.00m), It.IsAny<CancellationToken>()),
            Times.Once);
        savedStates.Should().HaveCount(2);
        savedStates.Should().OnlyContain(s => s.Status == PriceSyncStatus.Pending);
    }

    [Fact]
    public async Task returns_not_found_when_the_product_has_no_price_record()
    {
        // Arrange
        _repository
            .Setup(r => r.GetAsync("NOPE", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductPrice?)null);
        var handler = new SetProductPriceHandler(_repository.Object);

        // Act
        var response = await handler.Handle(
            new SetProductPriceRequest { ProductCode = "NOPE", PriceWithVat = 210.00m },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceNotFound);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void validator_rejects_non_positive_prices(decimal price)
    {
        // Arrange
        var validator = new SetProductPriceRequestValidator();

        // Act
        var result = validator.Validate(new SetProductPriceRequest { ProductCode = "A", PriceWithVat = price });

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void validator_rejects_a_blank_product_code()
    {
        // Arrange
        var validator = new SetProductPriceRequestValidator();

        // Act
        var result = validator.Validate(new SetProductPriceRequest { ProductCode = "  ", PriceWithVat = 210.00m });

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
```

Create `backend/test/Anela.Heblo.Tests/Features/ProductPricing/GetProductPricesHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class GetProductPricesHandlerTests
{
    [Fact]
    public async Task returns_each_price_with_its_per_target_sync_status()
    {
        // Arrange
        var repository = new Mock<IProductPriceRepository>();
        repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>
            {
                new() { ProductCode = "A", PriceWithVat = 190.00m, VatRate = 21m },
            });
        repository
            .Setup(r => r.GetSyncStatesAsync(PriceSyncTarget.Shoptet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>
            {
                new() { ProductCode = "A", Target = PriceSyncTarget.Shoptet, Status = PriceSyncStatus.InSync },
            });
        repository
            .Setup(r => r.GetSyncStatesAsync(PriceSyncTarget.Flexi, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>
            {
                new()
                {
                    ProductCode = "A", Target = PriceSyncTarget.Flexi,
                    Status = PriceSyncStatus.Conflict, RemoteValueAtConflict = 175.00m,
                },
            });
        var catalog = new Mock<ICatalogRepository>();
        catalog
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>
            {
                new() { ProductCode = "A", ProductName = "Olej na obličej" },
            });
        var handler = new GetProductPricesHandler(repository.Object, catalog.Object);

        // Act
        var response = await handler.Handle(new GetProductPricesRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        var price = response.Prices.Should().ContainSingle().Subject;
        price.ProductName.Should().Be("Olej na obličej");
        price.PriceWithoutVat.Should().Be(157.02m);
        price.ShoptetStatus.Should().Be(PriceSyncStatus.InSync);
        price.FlexiStatus.Should().Be(PriceSyncStatus.Conflict);
        price.FlexiRemoteValue.Should().Be(175.00m);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — the use case types do not exist.

- [ ] **Step 3: Write the contracts, handlers and controller**

`Contracts/ProductPriceDto.cs` (a **class**, never a record):

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

public class ProductPriceDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal PriceWithVat { get; set; }
    public decimal PriceWithoutVat { get; set; }
    public decimal VatRate { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;

    public PriceSyncStatus ShoptetStatus { get; set; }
    public decimal? ShoptetRemoteValue { get; set; }
    public PriceSyncStatus FlexiStatus { get; set; }
    public decimal? FlexiRemoteValue { get; set; }
}
```

`UseCases/GetProductPrices/GetProductPricesRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;

public class GetProductPricesRequest : IRequest<GetProductPricesResponse>
{
}
```

`UseCases/GetProductPrices/GetProductPricesResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;

public class GetProductPricesResponse : BaseResponse
{
    public List<ProductPriceDto> Prices { get; set; } = new();
}
```

`UseCases/GetProductPrices/GetProductPricesHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.ProductPricing;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;

public class GetProductPricesHandler : IRequestHandler<GetProductPricesRequest, GetProductPricesResponse>
{
    private readonly IProductPriceRepository _repository;
    private readonly ICatalogRepository _catalogRepository;

    public GetProductPricesHandler(
        IProductPriceRepository repository,
        ICatalogRepository catalogRepository)
    {
        _repository = repository;
        _catalogRepository = catalogRepository;
    }

    public async Task<GetProductPricesResponse> Handle(
        GetProductPricesRequest request, CancellationToken cancellationToken)
    {
        var prices = await _repository.GetAllAsync(cancellationToken);
        var productNames = (await _catalogRepository.GetAllAsync(cancellationToken))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ProductName, StringComparer.OrdinalIgnoreCase);
        var shoptetStates = (await _repository.GetSyncStatesAsync(PriceSyncTarget.Shoptet, cancellationToken))
            .ToDictionary(s => s.ProductCode, StringComparer.OrdinalIgnoreCase);
        var flexiStates = (await _repository.GetSyncStatesAsync(PriceSyncTarget.Flexi, cancellationToken))
            .ToDictionary(s => s.ProductCode, StringComparer.OrdinalIgnoreCase);

        return new GetProductPricesResponse
        {
            Prices = prices.Select(price =>
            {
                shoptetStates.TryGetValue(price.ProductCode, out var shoptet);
                flexiStates.TryGetValue(price.ProductCode, out var flexi);

                productNames.TryGetValue(price.ProductCode, out var productName);

                return new ProductPriceDto
                {
                    ProductCode = price.ProductCode,
                    ProductName = productName ?? string.Empty,
                    PriceWithVat = price.PriceWithVat,
                    PriceWithoutVat = price.PriceWithoutVat,
                    VatRate = price.VatRate,
                    ModifiedAt = price.ModifiedAt,
                    ModifiedBy = price.ModifiedBy,
                    ShoptetStatus = shoptet?.Status ?? PriceSyncStatus.Pending,
                    ShoptetRemoteValue = shoptet?.RemoteValueAtConflict,
                    FlexiStatus = flexi?.Status ?? PriceSyncStatus.Pending,
                    FlexiRemoteValue = flexi?.RemoteValueAtConflict,
                };
            }).ToList(),
        };
    }
}
```

`UseCases/SetProductPrice/SetProductPriceRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequest : IRequest<SetProductPriceResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal PriceWithVat { get; set; }
}
```

`UseCases/SetProductPrice/SetProductPriceResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceResponse : BaseResponse
{
    public SetProductPriceResponse() { }

    public SetProductPriceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }

    public decimal PriceWithVat { get; set; }
}
```

`UseCases/SetProductPrice/SetProductPriceRequestValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequestValidator : AbstractValidator<SetProductPriceRequest>
{
    private const decimal MaxPriceWithVat = 1_000_000m;

    public SetProductPriceRequestValidator()
    {
        RuleFor(r => r.ProductCode).NotEmpty();
        RuleFor(r => r.PriceWithVat).GreaterThan(0).LessThanOrEqualTo(MaxPriceWithVat);
    }
}
```

`UseCases/SetProductPrice/SetProductPriceHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ProductPricing;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceHandler : IRequestHandler<SetProductPriceRequest, SetProductPriceResponse>
{
    private static readonly PriceSyncTarget[] AllTargets = { PriceSyncTarget.Shoptet, PriceSyncTarget.Flexi };

    private readonly IProductPriceRepository _repository;

    public SetProductPriceHandler(IProductPriceRepository repository)
    {
        _repository = repository;
    }

    public async Task<SetProductPriceResponse> Handle(
        SetProductPriceRequest request, CancellationToken cancellationToken)
    {
        var price = await _repository.GetAsync(request.ProductCode, cancellationToken);
        if (price is null)
        {
            return new SetProductPriceResponse(
                ErrorCodes.ProductPriceNotFound,
                new Dictionary<string, string> { ["ProductCode"] = request.ProductCode });
        }

        price.PriceWithVat = request.PriceWithVat;
        price.ModifiedAt = DateTime.UtcNow;
        await _repository.UpsertAsync(price, cancellationToken);

        // The push itself is the job's work — Flexi's p95 is ~6.7s and must not block a save.
        foreach (var target in AllTargets)
        {
            var state = await _repository.GetSyncStateAsync(request.ProductCode, target, cancellationToken)
                ?? new ProductPriceSyncState { ProductCode = request.ProductCode, Target = target };

            state.Status = PriceSyncStatus.Pending;
            await _repository.UpsertSyncStateAsync(state, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return new SetProductPriceResponse { PriceWithVat = request.PriceWithVat };
    }
}
```

`UseCases/TriggerPriceSync/TriggerPriceSyncRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.TriggerPriceSync;

public class TriggerPriceSyncRequest : IRequest<TriggerPriceSyncResponse>
{
}
```

`UseCases/TriggerPriceSync/TriggerPriceSyncResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.TriggerPriceSync;

public class TriggerPriceSyncResponse : BaseResponse
{
    public int Pushed { get; set; }
    public int Conflicts { get; set; }
    public int Failed { get; set; }
    public int Seeded { get; set; }
    public int Unchanged { get; set; }
}
```

`UseCases/TriggerPriceSync/TriggerPriceSyncHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.TriggerPriceSync;

public class TriggerPriceSyncHandler : IRequestHandler<TriggerPriceSyncRequest, TriggerPriceSyncResponse>
{
    private readonly IProductPriceSyncService _syncService;

    public TriggerPriceSyncHandler(IProductPriceSyncService syncService)
    {
        _syncService = syncService;
    }

    public async Task<TriggerPriceSyncResponse> Handle(
        TriggerPriceSyncRequest request, CancellationToken cancellationToken)
    {
        var result = await _syncService.SyncAsync(cancellationToken);

        return new TriggerPriceSyncResponse
        {
            Pushed = result.Pushed,
            Conflicts = result.Conflicts,
            Failed = result.Failed,
            Seeded = result.Seeded,
            Unchanged = result.Unchanged,
        };
    }
}
```

`backend/src/Anela.Heblo.API/Controllers/ProductPricingController.cs`:

```csharp
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.TriggerPriceSync;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Products_Catalog)]
[ApiController]
[Route("api/product-pricing")]
public class ProductPricingController : BaseApiController
{
    private readonly IMediator _mediator;

    public ProductPricingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("prices")]
    public async Task<ActionResult<GetProductPricesResponse>> GetPrices(CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetProductPricesRequest(), cancellationToken));

    [HttpPut("prices/{productCode}")]
    [FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
    public async Task<ActionResult<SetProductPriceResponse>> SetPrice(
        string productCode,
        [FromBody] SetProductPriceRequest request,
        CancellationToken cancellationToken = default)
    {
        request.ProductCode = productCode;
        return Ok(await _mediator.Send(request, cancellationToken));
    }

    [HttpPost("sync")]
    [FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
    public async Task<ActionResult<TriggerPriceSyncResponse>> TriggerSync(CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new TriggerPriceSyncRequest(), cancellationToken));
}
```

> Pricing is gated behind the existing `Products_Catalog` feature. If prices later need their own permission, add a `Products_Pricing` entry to `access-matrix.json` and regenerate — do **not** hand-edit `Feature.generated.cs`.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Debug --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ProductPricing"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: product price read, edit and manual sync endpoints"
```

---

### Task 9: Conflict resolution

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/GetPriceSyncConflicts/{Request,Response,Handler}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/UseCases/ResolvePriceSyncConflict/{Request,Response,Handler,Validator}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/PriceConflictResolution.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ProductPricing/Contracts/PriceSyncConflictDto.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/ProductPricingController.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ProductPricing/ProductPricingModule.cs` (register the new validator + behavior)
- Test: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ResolvePriceSyncConflictHandlerTests.cs`

**Interfaces:**
- Consumes: `IProductPriceRepository`, `ErrorCodes.ProductPriceConflictNotFound`.
- Produces: `PriceConflictResolution { KeepHebloPrice = 1, AcceptRemotePrice = 2 }`, `ResolvePriceSyncConflictRequest { string ProductCode; PriceSyncTarget Target; PriceConflictResolution Resolution; }`, `GetPriceSyncConflictsResponse { List<PriceSyncConflictDto> Conflicts; }`.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/ProductPricing/ResolvePriceSyncConflictHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ResolvePriceSyncConflictHandlerTests
{
    private readonly Mock<IProductPriceRepository> _repository = new();
    private readonly List<ProductPriceSyncState> _savedStates = new();
    private readonly List<ProductPrice> _savedPrices = new();

    private ResolvePriceSyncConflictHandler CreateHandler()
    {
        _repository
            .Setup(r => r.UpsertSyncStateAsync(It.IsAny<ProductPriceSyncState>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceSyncState, CancellationToken>((s, _) => _savedStates.Add(s))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.UpsertAsync(It.IsAny<ProductPrice>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPrice, CancellationToken>((p, _) => _savedPrices.Add(p))
            .Returns(Task.CompletedTask);

        return new ResolvePriceSyncConflictHandler(_repository.Object);
    }

    private void GivenConflict(decimal hebloPrice, decimal remoteValue)
    {
        _repository
            .Setup(r => r.GetSyncStateAsync("A", PriceSyncTarget.Shoptet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPriceSyncState
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Status = PriceSyncStatus.Conflict,
                LastPushedPriceWithVat = 190.00m,
                RemoteValueAtConflict = remoteValue,
            });
        _repository
            .Setup(r => r.GetAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPrice { ProductCode = "A", PriceWithVat = hebloPrice, VatRate = 21m });
    }

    [Fact]
    public async Task keeping_heblos_price_rebases_last_pushed_so_the_next_run_overwrites()
    {
        // Arrange
        GivenConflict(hebloPrice: 210.00m, remoteValue: 175.00m);
        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(
            new ResolvePriceSyncConflictRequest
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Resolution = PriceConflictResolution.KeepHebloPrice,
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        var state = _savedStates.Should().ContainSingle().Subject;
        state.Status.Should().Be(PriceSyncStatus.Pending);
        state.LastPushedPriceWithVat.Should().Be(175.00m);
        state.RemoteValueAtConflict.Should().BeNull();
        _savedPrices.Should().BeEmpty();
    }

    [Fact]
    public async Task accepting_the_remote_price_writes_it_into_heblo_and_marks_in_sync()
    {
        // Arrange
        GivenConflict(hebloPrice: 210.00m, remoteValue: 175.00m);
        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(
            new ResolvePriceSyncConflictRequest
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Resolution = PriceConflictResolution.AcceptRemotePrice,
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _savedPrices.Should().ContainSingle().Which.PriceWithVat.Should().Be(175.00m);
        var state = _savedStates.Should().ContainSingle().Subject;
        state.Status.Should().Be(PriceSyncStatus.InSync);
        state.LastPushedPriceWithVat.Should().Be(175.00m);
    }

    [Fact]
    public async Task returns_not_found_when_the_state_is_not_in_conflict()
    {
        // Arrange
        _repository
            .Setup(r => r.GetSyncStateAsync("A", PriceSyncTarget.Shoptet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPriceSyncState { ProductCode = "A", Status = PriceSyncStatus.InSync });
        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(
            new ResolvePriceSyncConflictRequest
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Resolution = PriceConflictResolution.KeepHebloPrice,
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceConflictNotFound);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln -c Debug
```
Expected: FAIL — `ResolvePriceSyncConflictHandler` does not exist.

- [ ] **Step 3: Write the resolution use case**

`Contracts/PriceConflictResolution.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PriceConflictResolution
{
    /// <summary>Heblo's price wins; the next sync run overwrites the downstream edit.</summary>
    KeepHebloPrice = 1,

    /// <summary>The downstream edit wins and becomes Heblo's master value.</summary>
    AcceptRemotePrice = 2,
}
```

`UseCases/ResolvePriceSyncConflict/ResolvePriceSyncConflictHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ProductPricing;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;

public class ResolvePriceSyncConflictHandler
    : IRequestHandler<ResolvePriceSyncConflictRequest, ResolvePriceSyncConflictResponse>
{
    private const string ResolvedBy = "conflict-resolution";

    private readonly IProductPriceRepository _repository;

    public ResolvePriceSyncConflictHandler(IProductPriceRepository repository)
    {
        _repository = repository;
    }

    public async Task<ResolvePriceSyncConflictResponse> Handle(
        ResolvePriceSyncConflictRequest request, CancellationToken cancellationToken)
    {
        var state = await _repository.GetSyncStateAsync(request.ProductCode, request.Target, cancellationToken);
        if (state is null || state.Status != PriceSyncStatus.Conflict)
        {
            return new ResolvePriceSyncConflictResponse(
                ErrorCodes.ProductPriceConflictNotFound,
                new Dictionary<string, string>
                {
                    ["ProductCode"] = request.ProductCode,
                    ["Target"] = request.Target.ToString(),
                });
        }

        var remoteValue = state.RemoteValueAtConflict;

        if (request.Resolution == PriceConflictResolution.AcceptRemotePrice)
        {
            var price = await _repository.GetAsync(request.ProductCode, cancellationToken);
            if (price is null)
            {
                return new ResolvePriceSyncConflictResponse(
                    ErrorCodes.ProductPriceNotFound,
                    new Dictionary<string, string> { ["ProductCode"] = request.ProductCode });
            }

            price.PriceWithVat = remoteValue!.Value;
            price.ModifiedAt = DateTime.UtcNow;
            price.ModifiedBy = ResolvedBy;
            await _repository.UpsertAsync(price, cancellationToken);

            state.Status = PriceSyncStatus.InSync;
        }
        else
        {
            // Rebasing LastPushed onto the remote value turns the next run's compare into
            // "Heblo changed, remote didn't", which pushes and overwrites the downstream edit.
            state.Status = PriceSyncStatus.Pending;
        }

        state.LastPushedPriceWithVat = remoteValue;
        state.RemoteValueAtConflict = null;
        state.ConflictDetectedAt = null;
        state.LastError = null;
        await _repository.UpsertSyncStateAsync(state, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new ResolvePriceSyncConflictResponse();
    }
}
```

`UseCases/ResolvePriceSyncConflict/ResolvePriceSyncConflictRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Domain.Features.ProductPricing;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;

public class ResolvePriceSyncConflictRequest : IRequest<ResolvePriceSyncConflictResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public PriceSyncTarget Target { get; set; }
    public PriceConflictResolution Resolution { get; set; }
}
```

`UseCases/ResolvePriceSyncConflict/ResolvePriceSyncConflictResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;

public class ResolvePriceSyncConflictResponse : BaseResponse
{
    public ResolvePriceSyncConflictResponse() { }

    public ResolvePriceSyncConflictResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`UseCases/ResolvePriceSyncConflict/ResolvePriceSyncConflictRequestValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;

public class ResolvePriceSyncConflictRequestValidator : AbstractValidator<ResolvePriceSyncConflictRequest>
{
    public ResolvePriceSyncConflictRequestValidator()
    {
        RuleFor(r => r.ProductCode).NotEmpty();
        RuleFor(r => r.Target).IsInEnum();
        RuleFor(r => r.Resolution).IsInEnum();
    }
}
```

`UseCases/GetPriceSyncConflicts/GetPriceSyncConflictsRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceSyncConflicts;

public class GetPriceSyncConflictsRequest : IRequest<GetPriceSyncConflictsResponse>
{
}
```

`UseCases/GetPriceSyncConflicts/GetPriceSyncConflictsResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceSyncConflicts;

public class GetPriceSyncConflictsResponse : BaseResponse
{
    public List<PriceSyncConflictDto> Conflicts { get; set; } = new();
}
```

`Contracts/PriceSyncConflictDto.cs` (a **class**, never a record):

```csharp
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

public class PriceSyncConflictDto
{
    public string ProductCode { get; set; } = string.Empty;
    public PriceSyncTarget Target { get; set; }
    public decimal HebloPriceWithVat { get; set; }
    public decimal? RemotePriceWithVat { get; set; }
    public DateTime? ConflictDetectedAt { get; set; }
}
```

`UseCases/GetPriceSyncConflicts/GetPriceSyncConflictsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Domain.Features.ProductPricing;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceSyncConflicts;

public class GetPriceSyncConflictsHandler
    : IRequestHandler<GetPriceSyncConflictsRequest, GetPriceSyncConflictsResponse>
{
    private readonly IProductPriceRepository _repository;

    public GetPriceSyncConflictsHandler(IProductPriceRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPriceSyncConflictsResponse> Handle(
        GetPriceSyncConflictsRequest request, CancellationToken cancellationToken)
    {
        var conflicts = await _repository.GetConflictsAsync(cancellationToken);
        var prices = (await _repository.GetAllAsync(cancellationToken))
            .ToDictionary(p => p.ProductCode, StringComparer.OrdinalIgnoreCase);

        return new GetPriceSyncConflictsResponse
        {
            Conflicts = conflicts.Select(state =>
            {
                prices.TryGetValue(state.ProductCode, out var price);

                return new PriceSyncConflictDto
                {
                    ProductCode = state.ProductCode,
                    Target = state.Target,
                    HebloPriceWithVat = price?.PriceWithVat ?? 0m,
                    RemotePriceWithVat = state.RemoteValueAtConflict,
                    ConflictDetectedAt = state.ConflictDetectedAt,
                };
            }).ToList(),
        };
    }
}
```

Add the controller endpoints:

```csharp
    [HttpGet("conflicts")]
    public async Task<ActionResult<GetPriceSyncConflictsResponse>> GetConflicts(CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetPriceSyncConflictsRequest(), cancellationToken));

    [HttpPost("conflicts/resolve")]
    [FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
    public async Task<ActionResult<ResolvePriceSyncConflictResponse>> ResolveConflict(
        [FromBody] ResolvePriceSyncConflictRequest request,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(request, cancellationToken));
```

and register the validator + `ValidationBehavior` in `ProductPricingModule`.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Debug --no-build \
  -p:UseSharedCompilation=false --filter "FullyQualifiedName~ProductPricing"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: price sync conflict resolution"
```

---

### Task 10: Frontend price grid and conflicts view

**Files:**
- Create: `frontend/src/api/hooks/useProductPricing.ts`
- Create: `frontend/src/pages/ProductPricingPage.tsx`
- Create: `frontend/src/components/pricing/ProductPriceGrid.tsx`
- Create: `frontend/src/components/pricing/PriceConflictBanner.tsx`
- Modify: the app router and navigation (follow how `PackingMaterialsPage` is registered)
- Test: `frontend/src/pages/__tests__/ProductPricingPage.test.tsx`

**Interfaces:**
- Consumes: the generated OpenAPI client (regenerate after the backend builds: `dotnet msbuild -t:GenerateFrontendClientManual`), endpoints `GET /api/product-pricing/prices`, `PUT /api/product-pricing/prices/{productCode}`, `GET /api/product-pricing/conflicts`, `POST /api/product-pricing/conflicts/resolve`, `POST /api/product-pricing/sync`.
- Produces: `useProductPrices()`, `useSetProductPrice()`, `usePriceSyncConflicts()`, `useResolvePriceConflict()`, `useTriggerPriceSync()`.

> **Two traps in this codebase:**
> 1. The generated client **throws** on any non-200, so `if (!response.success)` branches are dead code. Read `errorCode` off the caught `SwaggerException` — it is a **string**, not a number.
> 2. Hooks that return raw `response.json()` deliver dates as ISO **strings**; calling `.getTime()` on `modifiedAt` directly blanks the screen. Convert explicitly.

- [ ] **Step 1: Write the failing page test**

Create `frontend/src/pages/__tests__/ProductPricingPage.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent, within } from "@testing-library/react";
import ProductPricingPage from "../ProductPricingPage";

const mockSetPrice = jest.fn();
const mockResolveConflict = jest.fn();
const mockTriggerSync = jest.fn();
let mockPrices: any[] = [];

jest.mock("../../api/hooks/useProductPricing", () => ({
  useProductPrices: () => ({ data: mockPrices, isLoading: false, error: null }),
  useSetProductPrice: () => ({ mutate: mockSetPrice, isPending: false }),
  usePriceSyncConflicts: () => ({ data: [], isLoading: false, error: null }),
  useResolvePriceConflict: () => ({ mutate: mockResolveConflict, isPending: false }),
  useTriggerPriceSync: () => ({ mutate: mockTriggerSync, isPending: false }),
}));

// Shell components read these contexts; without mocks the page fails to render.
jest.mock("../../auth/useAuth", () => ({ useAuth: () => ({ user: { name: "Test" } }) }));
jest.mock("../../auth/usePermissionsContext", () => ({
  usePermissionsContext: () => ({ hasPermission: () => true }),
}));

const inSyncRow = {
  productCode: "OCH001030",
  productName: "Olej na obličej",
  priceWithVat: 190,
  priceWithoutVat: 157.02,
  vatRate: 21,
  modifiedAt: "2026-09-03T10:00:00",
  shoptetStatus: "InSync",
  shoptetRemoteValue: null,
  flexiStatus: "InSync",
  flexiRemoteValue: null,
};

const conflictedRow = {
  ...inSyncRow,
  productCode: "TON002030",
  productName: "Tonikum",
  priceWithVat: 210,
  flexiStatus: "Conflict",
  flexiRemoteValue: 175,
};

beforeEach(() => {
  jest.clearAllMocks();
  mockPrices = [inSyncRow];
});

test("renders a row per product with its price and both sync statuses", () => {
  // Arrange
  mockPrices = [inSyncRow, conflictedRow];

  // Act
  render(<ProductPricingPage />);

  // Assert
  expect(screen.getByText("OCH001030")).toBeInTheDocument();
  expect(screen.getByText("TON002030")).toBeInTheDocument();
  expect(screen.getAllByTestId("sync-status-shoptet")).toHaveLength(2);
  expect(screen.getAllByTestId("sync-status-flexi")).toHaveLength(2);
});

test("saving an inline edit sends the new price", () => {
  // Arrange
  render(<ProductPricingPage />);
  const input = screen.getByLabelText("Cena s DPH pro OCH001030");

  // Act
  fireEvent.change(input, { target: { value: "210" } });
  fireEvent.blur(input);

  // Assert
  expect(mockSetPrice).toHaveBeenCalledWith(
    expect.objectContaining({ productCode: "OCH001030", priceWithVat: 210 }),
  );
});

test("a conflicted row shows both values and the two resolution actions", () => {
  // Arrange
  mockPrices = [conflictedRow];

  // Act
  render(<ProductPricingPage />);
  const banner = screen.getByTestId("price-conflict-TON002030-Flexi");

  // Assert
  expect(within(banner).getByText(/210/)).toBeInTheDocument();
  expect(within(banner).getByText(/175/)).toBeInTheDocument();
  expect(within(banner).getByRole("button", { name: "Ponechat cenu z Hebla", exact: true })).toBeInTheDocument();
  expect(within(banner).getByRole("button", { name: "Převzít externí cenu", exact: true })).toBeInTheDocument();
});

test("accepting the remote price resolves the conflict with AcceptRemotePrice", () => {
  // Arrange
  mockPrices = [conflictedRow];
  render(<ProductPricingPage />);
  const banner = screen.getByTestId("price-conflict-TON002030-Flexi");

  // Act
  fireEvent.click(within(banner).getByRole("button", { name: "Převzít externí cenu", exact: true }));

  // Assert
  expect(mockResolveConflict).toHaveBeenCalledWith({
    productCode: "TON002030",
    target: "Flexi",
    resolution: "AcceptRemotePrice",
  });
});

test("does not crash on the modifiedAt string returned by the API", () => {
  // Arrange & Act
  render(<ProductPricingPage />);

  // Assert — a raw ISO string passed to .getTime() would blank the page
  expect(screen.getByText("OCH001030")).toBeInTheDocument();
});
```

> `getByRole` matches the accessible name as a **substring**, so short Czech labels collide with longer aria-labels. Pass `exact: true` as above.

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern=ProductPricingPage
```
Expected: FAIL — module not found.

> Use `react-scripts test`, not `npx jest` — bare `jest` produces TypeScript parse errors here.

- [ ] **Step 3: Implement the hooks, grid and page**

`frontend/src/api/hooks/useProductPricing.ts`:

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export type PriceSyncStatus = "InSync" | "Pending" | "Conflict" | "Failed";
export type PriceSyncTarget = "Shoptet" | "Flexi";
export type PriceConflictResolution = "KeepHebloPrice" | "AcceptRemotePrice";

export interface ProductPrice {
  productCode: string;
  productName: string;
  priceWithVat: number;
  priceWithoutVat: number;
  vatRate: number;
  /** ISO string, NOT a Date — never call .getTime() on it directly. */
  modifiedAt: string;
  modifiedBy: string;
  shoptetStatus: PriceSyncStatus;
  shoptetRemoteValue: number | null;
  flexiStatus: PriceSyncStatus;
  flexiRemoteValue: number | null;
}

const PRICES_KEY = ["product-pricing", "prices"] as const;

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const apiClient = await getAuthenticatedApiClient();

  // Absolute URL: a relative one hits port 3001 instead of 5001.
  const response = await fetch(`${apiClient.baseUrl}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) },
  });

  if (!response.ok) {
    throw new Error(`${response.status} ${await response.text()}`);
  }

  return (await response.json()) as T;
}

export function useProductPrices() {
  return useQuery({
    queryKey: PRICES_KEY,
    queryFn: async () => {
      const data = await request<{ prices: ProductPrice[] }>("/api/product-pricing/prices");
      return data.prices;
    },
  });
}

export function useSetProductPrice() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: { productCode: string; priceWithVat: number }) =>
      request<unknown>(`/api/product-pricing/prices/${encodeURIComponent(input.productCode)}`, {
        method: "PUT",
        body: JSON.stringify({ priceWithVat: input.priceWithVat }),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PRICES_KEY }),
  });
}

export function useResolvePriceConflict() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: {
      productCode: string;
      target: PriceSyncTarget;
      resolution: PriceConflictResolution;
    }) =>
      request<unknown>("/api/product-pricing/conflicts/resolve", {
        method: "POST",
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PRICES_KEY }),
  });
}

export function useTriggerPriceSync() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => request<unknown>("/api/product-pricing/sync", { method: "POST" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PRICES_KEY }),
  });
}

export function usePriceSyncConflicts() {
  return useQuery({
    queryKey: ["product-pricing", "conflicts"],
    queryFn: async () => {
      const data = await request<{ conflicts: unknown[] }>("/api/product-pricing/conflicts");
      return data.conflicts;
    },
  });
}
```

> Match `getAuthenticatedApiClient` to whatever the neighbouring hooks in `frontend/src/api/hooks/` actually import — read `usePackingMaterials.ts` first and follow it exactly.

`ProductPriceGrid.tsx` renders one row per product: code, name, price with VAT (inline editable, `aria-label={\`Cena s DPH pro ${productCode}\`}`), derived price without VAT, VAT rate, and a status chip per target carrying `data-testid="sync-status-shoptet"` / `"sync-status-flexi"`. A row whose status is `Conflict` renders `PriceConflictBanner` beneath it.

`PriceConflictBanner.tsx` takes `productCode`, `target`, `hebloPrice`, `remotePrice`, renders `data-testid={\`price-conflict-${productCode}-${target}\`}`, shows both values, and offers two buttons — **Ponechat cenu z Hebla** (`KeepHebloPrice`) and **Převzít externí cenu** (`AcceptRemotePrice`) — each calling `useResolvePriceConflict`.

`ProductPricingPage.tsx` composes the grid with a **Synchronizovat** button wired to `useTriggerPriceSync`, showing the returned counts after a run.

- [ ] **Step 4: Run the test and the build**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern=ProductPricingPage
CI=false npm run build
npm run lint
```
Expected: tests PASS, build succeeds.

> Gate on `CI=false npm run build`. `npx tsc --noEmit` false-greens because react-i18next `.d.ts` parse errors cause it to skip all `src` checks.

- [ ] **Step 5: Full validation before opening the PR**

```bash
dotnet build Anela.Heblo.sln -c Debug
dotnet format --verify-no-changes
dotnet test Anela.Heblo.sln -c Debug --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
cd frontend && CI=false npm run build && npm run lint
```

- [ ] **Step 6: Commit**

```bash
git add frontend
git commit -m "feat: product pricing page with inline edit and conflict resolution"
```

---

## Deployment notes for the PR

- **The `AddProductPricing` migration must be run manually.** Migrations are not applied by the deployment in this project.
- **First run seeds and reconciles.** Expect a batch of Flexi conflicts on the first sync — that is the pre-existing double-entry drift surfacing, not a bug. Work the conflicts list down before treating the sync as steady-state.
- **`Shoptet:DefaultPriceListId`** is optional; leave it unset to let the client resolve the default list. Set it explicitly if the e-shop has an unusual price list layout.
- **Watch the 2026-09-14 Shoptet zero/null rollout.** The client never sends `0`, so the flag flip is a no-op for Heblo — but if a future change starts sending zero prices, re-read the documented semantics first.
