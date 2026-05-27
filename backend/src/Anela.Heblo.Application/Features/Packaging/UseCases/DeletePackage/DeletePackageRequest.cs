using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;

public class DeletePackageRequest : IRequest<DeletePackageResponse>
{
    public int Id { get; set; }
}
