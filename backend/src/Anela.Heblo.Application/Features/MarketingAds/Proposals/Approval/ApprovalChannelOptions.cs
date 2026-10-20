namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Which client applications count as the web UI and which as MCP (Claude) when a
/// person approves or rejects an ad proposal (spec 7.4). Client ids are not secrets.</summary>
public sealed class ApprovalChannelOptions
{
    public const string SectionName = "Ads:Approval";
    public const string ApiClientIdConfigKey = "AzureAd:ClientId";

    /// <summary>azp/appid values of the Heblo web SPA.</summary>
    public List<string> WebClientIds { get; set; } = new();

    /// <summary>Extra azp/appid values that are MCP clients (e.g. HebloMCP). The API's own
    /// client id is always added — Claude connectors to /mcp carry it.</summary>
    public List<string> McpClientIds { get; set; } = new();

    /// <summary>Filled from <c>AzureAd:ClientId</c> by <see cref="ApprovalChannelRegistration"/>.</summary>
    public string? ApiClientId { get; set; }
}
