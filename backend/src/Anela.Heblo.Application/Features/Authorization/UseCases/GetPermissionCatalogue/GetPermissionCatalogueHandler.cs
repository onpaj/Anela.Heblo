using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;

public class GetPermissionCatalogueHandler
    : IRequestHandler<GetPermissionCatalogueRequest, GetPermissionCatalogueResponse>
{
    public Task<GetPermissionCatalogueResponse> Handle(GetPermissionCatalogueRequest request, CancellationToken ct)
    {
        var response = new GetPermissionCatalogueResponse
        {
            Permissions = AccessMatrix.AllRoleValues().ToList(),
            Features = AccessMatrix.Features.Select(f => new CatalogueFeatureDto
            {
                Key = f.Key,
                Label = f.Label,
                Section = f.Section,
                HasWrite = f.HasWrite,
                HasAdmin = f.HasAdmin,
            }).ToList(),
            SystemGroups = AccessMatrix.Groups.Select(g => new CatalogueGroupDto
            {
                Name = g.Name,
                Permissions = g.Roles.ToList(),
            }).ToList(),
        };
        return Task.FromResult(response);
    }
}
