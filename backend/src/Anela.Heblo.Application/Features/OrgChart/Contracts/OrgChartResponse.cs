using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.OrgChart.Contracts;

/// <summary>
/// Response wrapper for organizational chart data
/// </summary>
public class OrgChartResponse : BaseResponse
{
    /// <summary>
    /// The complete organizational structure
    /// </summary>
    public OrganizationDto Organization { get; set; } = new();

    public OrgChartResponse() : base() { }

    public OrgChartResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
