using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionResponse
{
    [JsonPropertyName("ingredients")]
    public List<IngredientDto> Ingredients { get; set; } = new List<IngredientDto>();
}
