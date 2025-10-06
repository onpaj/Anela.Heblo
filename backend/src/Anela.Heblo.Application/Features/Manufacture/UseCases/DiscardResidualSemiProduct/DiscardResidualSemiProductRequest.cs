using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DiscardResidualSemiProduct;

public class DiscardResidualSemiProductRequest : IRequest<DiscardResidualSemiProductResponse>
{
    [Required] public string ManufactureOrderCode { get; set; } = null!;

    [Required] public string ProductCode { get; set; } = null!;

    public string? ProductName { get; set; }

    public DateTime CompletionDate { get; set; }

    public string? CompletedBy { get; set; }
    public double BatchSize { get; set; }
}