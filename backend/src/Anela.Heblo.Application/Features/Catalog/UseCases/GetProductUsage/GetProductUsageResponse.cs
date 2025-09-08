using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;

public class GetProductUsageResponse : BaseResponse
{
    public List<ManufactureTemplate> ManufactureTemplates { get; set; } = new List<ManufactureTemplate>();

    /// <summary>
    /// Creates a successful response
    /// </summary>
    public GetProductUsageResponse() : base()
    {
    }

    /// <summary>
    /// Creates an error response
    /// </summary>
    public GetProductUsageResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters)
    {
    }
}