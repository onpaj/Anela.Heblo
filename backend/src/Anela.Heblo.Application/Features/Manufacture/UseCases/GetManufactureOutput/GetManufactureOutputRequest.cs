using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput;

public class GetManufactureOutputRequest : IRequest<GetManufactureOutputResponse>
{
    /// <summary>
    /// Number of months back from current date to analyze manufacture output.
    /// Must be between 1 and 60 months.
    /// </summary>
    [Range(Anela.Heblo.Application.Features.Manufacture.ManufactureConstants.MIN_MONTHS_BACK,
           Anela.Heblo.Application.Features.Manufacture.ManufactureConstants.MAX_MONTHS_BACK,
           ErrorMessage = "MonthsBack must be between 1 and 60")]
    public int MonthsBack { get; set; } = 13;
}