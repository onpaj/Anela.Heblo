using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;

public class UpdateBoMIngredientAmountRequest : IRequest<UpdateBoMIngredientAmountResponse>
{
    [Required] public string ProductCode { get; set; } = null!;
    [Required] public string IngredientCode { get; set; } = null!;
    public double NewAmount { get; set; }
}
