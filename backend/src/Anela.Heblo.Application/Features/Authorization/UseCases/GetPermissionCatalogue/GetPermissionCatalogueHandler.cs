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
                Key = PermissionString.Format(f.Key, AccessLevel.Read)[..^".read".Length],
                Label = f.Label,
                Section = f.Key.ToString().Split('_')[0],
                HasWrite = f.HasWrite,
                HasAdmin = f.HasAdmin,
            }).ToList(),
        };
        return Task.FromResult(response);
    }
}
