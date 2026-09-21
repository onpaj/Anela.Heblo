using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Features.Pricing.UseCases.DeletePricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Pricing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class PricingScenarioHandlersTests
{
    private readonly Mock<IPricingScenarioRepository> _repository = new();
    private readonly Mock<IPricingBaselineBuilder> _baseline = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero));

    private static PricingBaselineRow Row(
        string code = "P1", decimal price = 420m, decimal material = 175m)
        => new(code, $"Product {code}", price, material, 70m, 1000d, true);

    private void GivenBaseline(params PricingBaselineRow[] rows)
        => _baseline.Setup(b => b.BuildAsync(It.IsAny<PricingFilterDto>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows.ToList());

    private static ICurrentUserService CurrentUserStub()
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser("id", "Name", "ondra@anela.cz", true));
        return mock.Object;
    }

    [Fact]
    public async Task Loading_a_missing_scenario_returns_PricingScenarioNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PricingScenario?)null);

        var handler = new GetPricingScenarioHandler(
            _repository.Object, _baseline.Object, new PricingSimulationCalculator());

        var response = await handler.Handle(
            new GetPricingScenarioRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PricingScenarioNotFound);
    }

    [Fact]
    public async Task Loading_a_scenario_replays_its_overrides_onto_the_current_baseline()
    {
        GivenBaseline(Row());
        var id = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ScenarioWithItem(id, price: 500m, baselinePrice: 420m, baselineMaterial: 175m));

        var handler = new GetPricingScenarioHandler(
            _repository.Object, _baseline.Object, new PricingSimulationCalculator());

        var response = await handler.Handle(
            new GetPricingScenarioRequest { Id = id }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Rows.Single().Price.Should().Be(500m);
    }

    [Fact]
    public async Task Flags_a_row_whose_baseline_has_drifted_since_the_scenario_was_saved()
    {
        // The catalog now says material costs 193; the scenario was saved when it was 175.
        GivenBaseline(Row(material: 193m));
        var id = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ScenarioWithItem(id, price: 500m, baselinePrice: 420m, baselineMaterial: 175m));

        var handler = new GetPricingScenarioHandler(
            _repository.Object, _baseline.Object, new PricingSimulationCalculator());

        var response = await handler.Handle(
            new GetPricingScenarioRequest { Id = id }, CancellationToken.None);

        response.Rows.Single().BaselineDrifted.Should().BeTrue();
    }

    [Fact]
    public async Task Saving_with_a_name_already_in_use_returns_PricingScenarioNameConflict()
    {
        _repository.Setup(r => r.ExistsByNameAsync("Podzim 2026", null, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        GivenBaseline(Row());

        var handler = new SavePricingScenarioHandler(
            _repository.Object, _baseline.Object, _time, CurrentUserStub());

        var response = await handler.Handle(new SavePricingScenarioRequest
        {
            Name = "Podzim 2026",
            Overrides = new List<PricingOverrideDto>()
        }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PricingScenarioNameConflict);
    }

    [Fact]
    public async Task Saving_snapshots_the_current_baseline_alongside_each_override()
    {
        GivenBaseline(Row());
        PricingScenario? captured = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<PricingScenario>(), It.IsAny<CancellationToken>()))
                   .Callback<PricingScenario, CancellationToken>((s, _) => captured = s)
                   .Returns(Task.CompletedTask);

        var handler = new SavePricingScenarioHandler(
            _repository.Object, _baseline.Object, _time, CurrentUserStub());

        await handler.Handle(new SavePricingScenarioRequest
        {
            Name = "Podzim 2026",
            Overrides = new List<PricingOverrideDto>
            {
                new() { ProductCode = "P1", Price = 500m, MaterialCost = 175m,
                        ManufacturingCost = 70m, ForecastQuantity = 1100d }
            }
        }, CancellationToken.None);

        captured.Should().NotBeNull();
        var item = captured!.Items.Single();
        item.Price.Should().Be(500m);
        item.BaselinePrice.Should().Be(420m);        // snapshot, not the edited value
        item.BaselineMaterialCost.Should().Be(175m);
        item.BaselineQuantity.Should().Be(1000d);
    }

    [Fact]
    public async Task Deleting_a_missing_scenario_returns_PricingScenarioNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PricingScenario?)null);

        var handler = new DeletePricingScenarioHandler(_repository.Object);

        var response = await handler.Handle(
            new DeletePricingScenarioRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PricingScenarioNotFound);
    }

    private static PricingScenario ScenarioWithItem(
        Guid id, decimal price, decimal baselinePrice, decimal baselineMaterial) => new()
        {
            Id = id,
            Name = "Podzim 2026",
            CreatedBy = "ondra@anela.cz",
            FilterJson = "{}",
            Items =
        {
            new PricingScenarioItem
            {
                ProductCode = "P1",
                Price = price,
                MaterialCost = 175m,
                ManufacturingCost = 70m,
                ForecastQuantity = 1000d,
                BaselinePrice = baselinePrice,
                BaselineMaterialCost = baselineMaterial,
                BaselineManufacturingCost = 70m,
                BaselineQuantity = 1000d
            }
        }
        };
}
