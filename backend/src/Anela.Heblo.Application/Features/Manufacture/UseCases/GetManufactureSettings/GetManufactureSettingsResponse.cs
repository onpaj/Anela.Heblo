using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;

public class GetManufactureSettingsResponse : BaseResponse
{
    public string? ManufactureGroupId { get; set; }
}
