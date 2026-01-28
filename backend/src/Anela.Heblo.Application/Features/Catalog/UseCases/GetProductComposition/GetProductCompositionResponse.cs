using System.Collections.Generic;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionResponse : BaseResponse
{
    [JsonPropertyName("ingredients")]
    public List<IngredientDto> Ingredients { get; set; } = new List<IngredientDto>();
}
