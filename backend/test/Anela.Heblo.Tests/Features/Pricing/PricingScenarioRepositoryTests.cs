using Anela.Heblo.Domain.Features.Pricing;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Pricing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

/// <summary>
/// Backs each test with a fresh <see cref="ApplicationDbContext"/> per <see cref="CreateRepository"/>
/// call, all pointed at the same named EF InMemory database, so a round-trip test cannot pass
/// merely because the repository under test still has the entity in its own change tracker.
/// </summary>
public sealed class PricingPersistenceFixture : IAsyncDisposable
{
    private readonly string _databaseName;

    private PricingPersistenceFixture(string databaseName)
    {
        _databaseName = databaseName;
    }

    public static PricingPersistenceFixture Create() =>
        new($"PricingScenarioRepositoryTests_{Guid.NewGuid()}");

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: _databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }

    public PricingScenarioRepository CreateRepository() => new(CreateContext());

    public int CountItems()
    {
        using var context = CreateContext();
        return context.PricingScenarioItems.Count();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class PricingScenarioRepositoryTests
{
    private static PricingScenario NewScenario(string name = "Podzim 2026") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = "Návrh nového ceníku",
        CreatedBy = "ondra@anela.cz",
        CreatedAt = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc),
        ModifiedAt = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc),
        FilterJson = "{}",
        Items =
        {
            new PricingScenarioItem
            {
                ProductCode = "DEO002050",
                Price = 500m,
                MaterialCost = 175m,
                ManufacturingCost = 70m,
                ForecastQuantity = 1100d,
                BaselinePrice = 420m,
                BaselineMaterialCost = 175m,
                BaselineManufacturingCost = 70m,
                BaselineQuantity = 1240d
            }
        }
    };

    [Fact]
    public async Task Round_trips_a_scenario_with_its_items()
    {
        await using var fixture = PricingPersistenceFixture.Create();
        var sut = fixture.CreateRepository();
        var scenario = NewScenario();

        await sut.AddAsync(scenario, CancellationToken.None);

        var loaded = await fixture.CreateRepository().GetByIdAsync(scenario.Id, CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.Items.Should().HaveCount(1);
        loaded.Items.Single().Price.Should().Be(500m);
        loaded.Items.Single().BaselinePrice.Should().Be(420m);
    }

    [Fact]
    public async Task Persists_a_baseline_snapshot_so_a_reopened_scenario_can_detect_drift()
    {
        await using var fixture = PricingPersistenceFixture.Create();
        var scenario = NewScenario();
        await fixture.CreateRepository().AddAsync(scenario, CancellationToken.None);

        var loaded = await fixture.CreateRepository().GetByIdAsync(scenario.Id, CancellationToken.None);

        loaded!.Items.Single().BaselineMaterialCost.Should().Be(175m);
        loaded.Items.Single().BaselineQuantity.Should().Be(1240d);
    }

    [Fact]
    public async Task Deleting_a_scenario_removes_its_items()
    {
        await using var fixture = PricingPersistenceFixture.Create();
        var scenario = NewScenario();
        await fixture.CreateRepository().AddAsync(scenario, CancellationToken.None);

        await fixture.CreateRepository().DeleteAsync(scenario.Id, CancellationToken.None);

        var loaded = await fixture.CreateRepository().GetByIdAsync(scenario.Id, CancellationToken.None);
        loaded.Should().BeNull();
        fixture.CountItems().Should().Be(0);
    }

    [Fact]
    public async Task Detects_a_duplicate_name_and_ignores_the_scenario_being_edited()
    {
        await using var fixture = PricingPersistenceFixture.Create();
        var scenario = NewScenario();
        await fixture.CreateRepository().AddAsync(scenario, CancellationToken.None);
        var sut = fixture.CreateRepository();

        (await sut.ExistsByNameAsync("Podzim 2026", null, CancellationToken.None)).Should().BeTrue();
        (await sut.ExistsByNameAsync("Podzim 2026", scenario.Id, CancellationToken.None)).Should().BeFalse();
        (await sut.ExistsByNameAsync("Jaro 2027", null, CancellationToken.None)).Should().BeFalse();
    }
}
