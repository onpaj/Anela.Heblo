using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;

public class UpdateBoMIngredientAmountResponse : BaseResponse
{
    public string? UserMessage { get; set; }

    public UpdateBoMIngredientAmountResponse() : base() { }

    public UpdateBoMIngredientAmountResponse(Exception ex) : base(ex) { }
}
