using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;

public class GetProductUsageHandler : IRequestHandler<GetProductUsageRequest, GetProductUsageResponse>
{
    private readonly IManufactureRepository _manufactureRepository;

    public GetProductUsageHandler(IManufactureRepository manufactureRepository)
    {
        _manufactureRepository = manufactureRepository;
    }

    public async Task<GetProductUsageResponse> Handle(GetProductUsageRequest request, CancellationToken cancellationToken)
    {
        var manufactureTemplates = await _manufactureRepository.FindByIngredientAsync(request.ProductCode, cancellationToken);

        return new GetProductUsageResponse
        {
            ManufactureTemplates = manufactureTemplates
        };
    }
}