using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionRequest : IRequest<GetProductCompositionResponse>
{
    [Required]
    public string ProductCode { get; set; }
}
