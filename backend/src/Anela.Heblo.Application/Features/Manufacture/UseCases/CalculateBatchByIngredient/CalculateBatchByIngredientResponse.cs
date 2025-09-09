using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;

public class CalculateBatchByIngredientResponse : BaseResponse
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double OriginalBatchSize { get; set; }
    public double NewBatchSize { get; set; }
    public double ScaleFactor { get; set; }
    public string ScaledIngredientCode { get; set; } = string.Empty;
    public string ScaledIngredientName { get; set; } = string.Empty;
    public double ScaledIngredientOriginalAmount { get; set; }
    public double ScaledIngredientNewAmount { get; set; }
    public List<CalculatedIngredientDto> Ingredients { get; set; } = new List<CalculatedIngredientDto>();

    public CalculateBatchByIngredientResponse() : base() { }

    public CalculateBatchByIngredientResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}