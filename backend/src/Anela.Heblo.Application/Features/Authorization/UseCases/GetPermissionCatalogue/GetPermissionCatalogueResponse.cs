using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;

public class GetPermissionCatalogueResponse : BaseResponse
{
    public List<string> Permissions { get; set; } = new();
    public List<CatalogueFeatureDto> Features { get; set; } = new();
    public List<CatalogueGroupDto> SystemGroups { get; set; } = new();
}

public class CatalogueFeatureDto
{
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Section { get; set; } = null!;
    public bool HasWrite { get; set; }
    public bool HasAdmin { get; set; }
}

public class CatalogueGroupDto
{
    public string Name { get; set; } = null!;
    public List<string> Permissions { get; set; } = new();
}
