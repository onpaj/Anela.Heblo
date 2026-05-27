using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ConfirmSemiProductManufactureRequest : IRequest<ConfirmSemiProductManufactureResponse>
{
    [Required]
    public int Id { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "ActualQuantity must be greater than 0")]
    public decimal ActualQuantity { get; set; }

    public string? ChangeReason { get; set; }
}