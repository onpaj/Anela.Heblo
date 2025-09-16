using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;

public class UpdateManufactureOrderStatusResponse : BaseResponse
{
    public string? OldState { get; set; }
    public string? NewState { get; set; }
    public DateTime StateChangedAt { get; set; }
    public string? StateChangedByUser { get; set; }

    public UpdateManufactureOrderStatusResponse() : base() { }

    public UpdateManufactureOrderStatusResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}