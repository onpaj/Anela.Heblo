using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Features.Pricing.UseCases.UpdatePricingScenarioProducts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Pricing;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class UpdatePricingScenarioProductsHandlerTests
{
    private static readonly Guid ScenarioId = Guid.NewGuid();

    private readonly Mock<IPricingScenarioRepository> _repository = new();
    private readonly Mock<IPricingBaselineBuilder> _baseline = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero));
    private PricingScenario? _saved;

    public UpdatePricingScenarioProductsHandlerTests()
    {
        _repository.Setup(r => r.UpdateAsync(It.IsAny<PricingScenario>(), It.IsAny<CancellationToken>()))
                   .Callback<PricingScenario, CancellationToken>((s, _) => _saved = s)
                   .Returns(Task.CompletedTask);
    }

    private UpdatePricingScenarioProductsHandler Handler() =>
        new(_repository.Object, _baseline.Object, new PricingSimulationCalculator(), _time);

    private static PricingBaselineRow Row(string code, decimal price = 420m, decimal material = 175m)
        => new(code, $"Product {code}", price, material, 70m, 1000d, true);

    private void GivenBaseline(params PricingBaselineRow[] rows)
        => _baseline.Setup(b => b.BuildAsync(It.IsAny<PricingFilterDto>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows.ToList());

    private static PricingScenarioItem Item(string code, decimal? price = null, decimal? material = null,
        decimal baselinePrice = 420m, decimal baselineMaterial = 175m) => new()
        {
            ProductCode = code,
            Price = price,
            MaterialCost = material,
            BaselinePrice = baselinePrice,
            BaselineMaterialCost = baselineMaterial,
            BaselineManufacturingCost = 70m,
            BaselineQuantity = 1000d
        };

    private void GivenScenario(params PricingScenarioItem[] items)
        => _repository.Setup(r => r.GetByIdAsync(ScenarioId, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new PricingScenario
                      {
                          Id = ScenarioId,
                          Name = "Podzim 2026",
                          Description = "Zdražení materiálu",
                          CreatedBy = "ondra@anela.cz",
                          FilterJson = "{\"ProductCode\":\"P\",\"ProductName\":null,\"ProductType\":null}",
                          Items = items.ToList()
                      });

    private static PricingEditDto Edit(string code, PricingEditField field, decimal value)
        => new() { ProductCode = code, Field = field, Value = value };

    [Fact]
    public async Task Updating_a_missing_scenario_returns_PricingScenarioNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PricingScenario?)null);

        var response = await Handler().Handle(
            new UpdatePricingScenarioProductsRequest { ScenarioId = ScenarioId, RemoveProductCodes = { "P1" } },
            CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.PricingScenarioNotFound);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<PricingScenario>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Editing_one_product_keeps_the_other_products_and_their_snapshots()
    {
        // P1's catalog material moved 175 -> 193 since saving; its old snapshot must survive
        // so the scenario keeps reporting the drift.
        GivenBaseline(Row("P1", material: 193m), Row("P2"));
        GivenScenario(Item("P1", price: 500m), Item("P2", price: 450m));

        var response = await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Edits = { Edit("P2", PricingEditField.Price, 480m) }
        }, CancellationToken.None);

        response.Success.Should().BeTrue();
        var p1 = _saved!.Items.Single(i => i.ProductCode == "P1");
        p1.Price.Should().Be(500m);
        p1.BaselineMaterialCost.Should().Be(175m);
        _saved.Items.Single(i => i.ProductCode == "P2").Price.Should().Be(480m);
    }

    [Fact]
    public async Task An_edited_product_gets_a_fresh_baseline_snapshot()
    {
        GivenBaseline(Row("P1", material: 193m));
        GivenScenario(Item("P1", price: 500m));

        await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Edits = { Edit("P1", PricingEditField.Price, 520m) }
        }, CancellationToken.None);

        _saved!.Items.Single().BaselineMaterialCost.Should().Be(193m);
    }

    [Fact]
    public async Task A_price_edit_keeps_the_products_other_overrides()
    {
        GivenBaseline(Row("P1"));
        GivenScenario(Item("P1", material: 200m));

        await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Edits = { Edit("P1", PricingEditField.Price, 520m) }
        }, CancellationToken.None);

        var item = _saved!.Items.Single();
        item.Price.Should().Be(520m);
        item.MaterialCost.Should().Be(200m);
    }

    [Fact]
    public async Task Editing_a_product_not_yet_in_the_scenario_adds_it()
    {
        GivenBaseline(Row("P1"), Row("P2"));
        GivenScenario(Item("P1", price: 500m));

        var response = await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Edits = { Edit("P2", PricingEditField.M0Percentage, 60m) }
        }, CancellationToken.None);

        _saved!.Items.Select(i => i.ProductCode).Should().BeEquivalentTo("P1", "P2");
        response.Rows.Single(r => r.ProductCode == "P2").M0Percentage.Should().Be(60m);
    }

    [Fact]
    public async Task Removing_drops_the_product_and_reports_codes_that_were_not_in_the_scenario()
    {
        GivenBaseline(Row("P1"), Row("P2"));
        GivenScenario(Item("P1", price: 500m), Item("P2", price: 450m));

        var response = await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            RemoveProductCodes = { "p1", "NOPE" }
        }, CancellationToken.None);

        _saved!.Items.Select(i => i.ProductCode).Should().BeEquivalentTo("P2");
        response.UnmatchedRemovals.Should().BeEquivalentTo("NOPE");
    }

    [Fact]
    public async Task Metadata_and_filter_are_kept_when_not_supplied()
    {
        GivenBaseline(Row("P1"));
        GivenScenario(Item("P1", price: 500m));

        await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Edits = { Edit("P1", PricingEditField.Price, 520m) }
        }, CancellationToken.None);

        _saved!.Name.Should().Be("Podzim 2026");
        _saved.Description.Should().Be("Zdražení materiálu");
        _saved.FilterJson.Should().Contain("\"ProductCode\":\"P\"");
        _saved.ModifiedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Metadata_is_changed_when_supplied_and_an_empty_description_clears_it()
    {
        GivenBaseline(Row("P1"));
        GivenScenario(Item("P1", price: 500m));

        await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Name = "Zima 2026",
            Description = ""
        }, CancellationToken.None);

        _saved!.Name.Should().Be("Zima 2026");
        _saved.Description.Should().BeNull();
        _saved.Items.Single().Price.Should().Be(500m);
    }

    [Fact]
    public async Task Renaming_to_a_name_in_use_returns_PricingScenarioNameConflict()
    {
        GivenBaseline(Row("P1"));
        GivenScenario(Item("P1", price: 500m));
        _repository.Setup(r => r.ExistsByNameAsync("Jaro 2026", ScenarioId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);

        var response = await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Name = "Jaro 2026"
        }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.PricingScenarioNameConflict);
        _saved.Should().BeNull();
    }

    [Fact]
    public async Task A_failing_edit_saves_nothing_and_names_the_edit()
    {
        GivenBaseline(Row("P1"), Row("P2"));
        GivenScenario(Item("P1", price: 500m));

        var response = await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Edits =
            {
                Edit("P1", PricingEditField.Price, 520m),
                Edit("P2", PricingEditField.M0Percentage, 150m) // implies negative material cost
            }
        }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.PricingNegativeMaterialCost);
        response.Params.Should().Contain("editNumber", "2");
        _saved.Should().BeNull();
    }

    [Fact]
    public async Task Editing_a_product_outside_the_scenario_filter_is_rejected()
    {
        GivenBaseline(Row("P1"));
        GivenScenario(Item("P1", price: 500m));

        var response = await Handler().Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            Edits = { Edit("X9", PricingEditField.Price, 100m) }
        }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.PricingProductNotInBaseline);
        _saved.Should().BeNull();
    }

    [Fact]
    public async Task Persists_through_a_real_repository_sharing_one_context_like_a_request_scope()
    {
        // GetByIdAsync tracks the scenario and UpdateAsync re-queries the same graph in the
        // same context; mocked repositories cannot show that this round-trips.
        await using var fixture = PricingPersistenceFixture.Create();
        await fixture.CreateRepository().AddAsync(new PricingScenario
        {
            Id = ScenarioId,
            Name = "Podzim 2026",
            CreatedBy = "ondra@anela.cz",
            FilterJson = "{}",
            Items = { Item("P1", price: 500m), Item("P2", price: 450m) }
        });
        GivenBaseline(Row("P1"), Row("P2"), Row("P3"));
        var handler = new UpdatePricingScenarioProductsHandler(
            fixture.CreateRepository(), _baseline.Object, new PricingSimulationCalculator(), _time);

        var response = await handler.Handle(new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = ScenarioId,
            RemoveProductCodes = { "P2" },
            Edits = { Edit("P3", PricingEditField.Price, 300m) }
        }, CancellationToken.None);

        response.Success.Should().BeTrue();
        var reloaded = await fixture.CreateRepository().GetByIdAsync(ScenarioId);
        reloaded!.Items.Select(i => (i.ProductCode, i.Price)).Should()
            .BeEquivalentTo(new[] { ("P1", (decimal?)500m), ("P3", (decimal?)300m) });
        fixture.CountItems().Should().Be(2);
    }
}
