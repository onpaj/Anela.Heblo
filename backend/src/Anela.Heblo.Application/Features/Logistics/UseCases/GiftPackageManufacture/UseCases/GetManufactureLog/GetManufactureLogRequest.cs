using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetManufactureLog;

public class GetManufactureLogRequest : IRequest<GetManufactureLogResponse>
{
    public int Count { get; set; } = 10;
}