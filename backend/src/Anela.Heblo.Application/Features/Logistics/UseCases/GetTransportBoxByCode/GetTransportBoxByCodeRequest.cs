using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxByCode;

public class GetTransportBoxByCodeRequest : IRequest<GetTransportBoxByCodeResponse>
{
    public string BoxCode { get; set; } = string.Empty;
}