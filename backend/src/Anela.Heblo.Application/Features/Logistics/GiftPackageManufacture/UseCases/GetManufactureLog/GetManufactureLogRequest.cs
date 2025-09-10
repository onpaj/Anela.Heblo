using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetManufactureLog;

public class GetManufactureLogRequest : IRequest<GetManufactureLogResponse>
{
    public int Count { get; set; } = 10;
}