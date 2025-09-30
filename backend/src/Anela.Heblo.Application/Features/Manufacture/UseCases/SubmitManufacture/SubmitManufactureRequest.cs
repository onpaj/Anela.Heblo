using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

public class SubmitManufactureRequest : IRequest<SubmitManufactureResponse>
{
    [Required] public string ManufactureOrderNumber { get; set; } = null!;
    
    [Required] public string ManufactureInternalNumber { get; set; } = null!;
    public ManufactureType ManufactureType { get; set; }
    public DateTime Date { get; set; }
    public string? CreatedBy { get; set; }
    public List<SubmitManufactureRequestItem> Items { get; set; } = [];

    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
}