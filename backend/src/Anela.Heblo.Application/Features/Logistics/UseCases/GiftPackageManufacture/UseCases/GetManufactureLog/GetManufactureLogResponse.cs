using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetManufactureLog;

public class GetManufactureLogResponse : BaseResponse
{
    public List<GiftPackageManufactureDto> ManufactureLogs { get; set; } = new();
}