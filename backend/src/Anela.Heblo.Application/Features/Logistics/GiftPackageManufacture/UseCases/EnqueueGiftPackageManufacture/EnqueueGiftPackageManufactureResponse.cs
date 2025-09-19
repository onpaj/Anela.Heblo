using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.EnqueueGiftPackageManufacture;

public class EnqueueGiftPackageManufactureResponse : BaseResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}