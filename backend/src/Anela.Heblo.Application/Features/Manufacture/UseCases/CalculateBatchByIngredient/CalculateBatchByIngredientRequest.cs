using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;

public class CalculateBatchByIngredientRequest : IRequest<CalculateBatchByIngredientResponse>
{
    public string ProductCode { get; set; } = null!;
    public string IngredientCode { get; set; } = null!;
    public double DesiredIngredientAmount { get; set; }
}