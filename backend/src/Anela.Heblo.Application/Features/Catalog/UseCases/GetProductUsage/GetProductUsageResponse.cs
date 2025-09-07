using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;

public class GetProductUsageResponse : BaseResponse
{
    public List<ManufactureTemplate> ManufactureTemplates { get; set; } = new List<ManufactureTemplate>();
}