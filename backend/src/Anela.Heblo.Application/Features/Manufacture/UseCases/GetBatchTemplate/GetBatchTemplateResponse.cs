using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetBatchTemplate;

public class GetBatchTemplateResponse : BaseResponse
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double BatchSize { get; set; }
    public List<BatchIngredientDto> Ingredients { get; set; } = new List<BatchIngredientDto>();

    public GetBatchTemplateResponse() : base() { }

    public GetBatchTemplateResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}

public class BatchIngredientDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public decimal Price { get; set; }
}