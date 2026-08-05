using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

[McpServerToolType]
public class LeafletTools
{
    private readonly IMediator _mediator;
    private readonly ILogger<LeafletTools> _logger;
    private readonly ICurrentUserService _currentUserService;

    public LeafletTools(IMediator mediator, ILogger<LeafletTools> logger, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    [McpServerTool]
    [Description("Generates a marketing leaflet in Czech Markdown using the company knowledge base and historical leaflets as style references.")]
    public async Task<string> GenerateLeaflet(
        [Description("Leaflet topic (1-200 characters), e.g. 'Bisabolol pro citlivou pleť'")] string topic,
        [Description("Audience: 'EndConsumer' or 'B2B'")] string audience,
        [Description("Length: 'Short', 'Medium', or 'Long'")] string length,
        CancellationToken ct = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Leaflet, "Leaflet Generator");

        try
        {
            if (!Enum.TryParse<AudienceType>(audience, ignoreCase: true, out var audienceEnum))
                throw new McpException($"Invalid audience '{audience}'");

            if (!Enum.TryParse<LeafletLength>(length, ignoreCase: true, out var lengthEnum))
                throw new McpException($"Invalid length '{length}'");

            var response = await _mediator.Send(new GenerateLeafletRequest
            {
                Topic = topic,
                Audience = audienceEnum,
                Length = lengthEnum
            }, ct);

            if (!response.Success)
            {
                var message = response.ErrorCode == ErrorCodes.LeafletEmptyRetrieval
                    ? "Knowledge Base does not yet cover this topic; try a broader phrasing"
                    : "Leaflet generation failed. Please try again.";
                throw new McpException(message);
            }

            return JsonSerializer.Serialize(response, McpJsonOptions.Default);
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP GenerateLeaflet failed");
            throw new McpException("Leaflet generation failed. Please try again.");
        }
    }
}
