using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;

public class GetManufactureProtocolRequest : IRequest<GetManufactureProtocolResponse>
{
    public int Id { get; set; }
}
