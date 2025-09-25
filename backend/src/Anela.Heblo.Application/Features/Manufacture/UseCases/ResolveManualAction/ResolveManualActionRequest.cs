using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ResolveManualAction;

public class ResolveManualActionRequest : IRequest<ResolveManualActionResponse>
{
    [Required]
    public int OrderId { get; set; }

    public string? ErpOrderNumberSemiproduct { get; set; }
    
    public string? ErpOrderNumberProduct { get; set; }

    public string? Note { get; set; }
}