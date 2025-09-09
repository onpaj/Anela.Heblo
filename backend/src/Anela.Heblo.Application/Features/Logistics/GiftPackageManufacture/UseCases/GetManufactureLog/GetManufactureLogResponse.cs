using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetManufactureLog;

public class GetManufactureLogResponse : BaseResponse
{
    public List<GiftPackageManufactureDto> ManufactureLogs { get; set; } = new();
}