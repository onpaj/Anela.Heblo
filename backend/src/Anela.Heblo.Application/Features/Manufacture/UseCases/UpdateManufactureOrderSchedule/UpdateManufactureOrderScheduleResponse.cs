using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderSchedule;

public class UpdateManufactureOrderScheduleResponse : BaseResponse
{
    public string? Message { get; set; }

    public UpdateManufactureOrderScheduleResponse() : base()
    {
        Message = "Schedule updated successfully";
    }

    public UpdateManufactureOrderScheduleResponse(ErrorCodes errorCode, string message = "") : base(errorCode)
    {
        Message = message;
    }
}