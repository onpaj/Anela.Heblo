using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetProductUsageResponse
{
    public List<ManufactureTemplate> ManufactureTemplates { get; set; } = new List<ManufactureTemplate>();
}