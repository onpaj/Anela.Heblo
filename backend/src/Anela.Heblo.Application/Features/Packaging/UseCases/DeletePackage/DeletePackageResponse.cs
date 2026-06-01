using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;

public class DeletePackageResponse : BaseResponse
{
    public bool Deleted { get; set; }

    public DeletePackageResponse(bool deleted)
    {
        Deleted = deleted;
    }

    public DeletePackageResponse(ErrorCodes errorCode) : base(errorCode) { }
}
