using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder;

public class DuplicateManufactureOrderResponse : BaseResponse
{
    public int Id { get; set; }
    public string? OrderNumber { get; set; }

    public DuplicateManufactureOrderResponse() : base() { }
    public DuplicateManufactureOrderResponse(ErrorCodes errorCode) : base(errorCode) { }
}