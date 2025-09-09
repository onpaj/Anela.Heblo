using Anela.Heblo.Application.Features.Manufacture.UseCases.GetBatchTemplate;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;

public class CalculateBatchBySizeResponse : BaseResponse
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double OriginalBatchSize { get; set; }
    public double NewBatchSize { get; set; }
    public double ScaleFactor { get; set; }
    public List<CalculatedIngredientDto> Ingredients { get; set; } = new List<CalculatedIngredientDto>();

    public CalculateBatchBySizeResponse() : base() { }

    public CalculateBatchBySizeResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}

public class CalculatedIngredientDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double OriginalAmount { get; set; }
    public double CalculatedAmount { get; set; }
    public decimal Price { get; set; }
}