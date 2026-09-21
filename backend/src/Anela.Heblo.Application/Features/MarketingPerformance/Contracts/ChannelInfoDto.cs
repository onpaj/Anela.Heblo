using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Contracts;

public class ChannelInfoDto
{
    [Required] public string Code { get; set; } = string.Empty;
    [Required] public string Label { get; set; } = string.Empty;
}
