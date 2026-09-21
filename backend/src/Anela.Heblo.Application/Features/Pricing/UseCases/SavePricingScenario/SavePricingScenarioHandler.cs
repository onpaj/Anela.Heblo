using System.Text.Json;
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Pricing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;

public class SavePricingScenarioHandler
    : IRequestHandler<SavePricingScenarioRequest, SavePricingScenarioResponse>
{
    private readonly IPricingScenarioRepository _repository;
    private readonly IPricingBaselineBuilder _baselineBuilder;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;

    public SavePricingScenarioHandler(
        IPricingScenarioRepository repository,
        IPricingBaselineBuilder baselineBuilder,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _baselineBuilder = baselineBuilder;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
    }

    public async Task<SavePricingScenarioResponse> Handle(
        SavePricingScenarioRequest request, CancellationToken cancellationToken)
    {
        var nameInUse = await _repository.ExistsByNameAsync(request.Name, request.Id, cancellationToken);
        if (nameInUse)
        {
            return new SavePricingScenarioResponse(ErrorCodes.PricingScenarioNameConflict, new Dictionary<string, string>
            {
                { "name", request.Name }
            });
        }

        var filter = new PricingFilterDto
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            ProductType = request.ProductType
        };

        var baseline = await _baselineBuilder.BuildAsync(filter, cancellationToken);
        var baselineByCode = baseline.ToDictionary(b => b.ProductCode, StringComparer.OrdinalIgnoreCase);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var currentUser = _currentUserService.GetCurrentUser();

        var scenario = new PricingScenario
        {
            Id = request.Id ?? Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            CreatedBy = currentUser.Email ?? string.Empty,
            CreatedAt = now,
            ModifiedAt = now,
            FilterJson = JsonSerializer.Serialize(filter),
            Items = request.Overrides.Select(o => ToItem(o, baselineByCode)).ToList()
        };

        if (request.Id is null)
        {
            await _repository.AddAsync(scenario, cancellationToken);
        }
        else
        {
            await _repository.UpdateAsync(scenario, cancellationToken);
        }

        return new SavePricingScenarioResponse { Id = scenario.Id };
    }

    private static PricingScenarioItem ToItem(
        PricingOverrideDto over, IReadOnlyDictionary<string, PricingBaselineRow> baselineByCode)
    {
        baselineByCode.TryGetValue(over.ProductCode, out var baseline);

        return new PricingScenarioItem
        {
            ProductCode = over.ProductCode,
            Price = over.Price,
            MaterialCost = over.MaterialCost,
            ManufacturingCost = over.ManufacturingCost,
            ForecastQuantity = over.ForecastQuantity,

            BaselinePrice = baseline?.Price ?? 0m,
            BaselineMaterialCost = baseline?.MaterialCost ?? 0m,
            BaselineManufacturingCost = baseline?.ManufacturingCost ?? 0m,
            BaselineQuantity = baseline?.Quantity ?? 0d
        };
    }
}
