using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput;

public class GetManufactureOutputRequest : IRequest<GetManufactureOutputResponse>
{
    public int MonthsBack { get; set; } = 13;
}