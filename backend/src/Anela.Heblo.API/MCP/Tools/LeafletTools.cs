using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
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

    public LeafletTools(IMediator mediator, ILogger<LeafletTools> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [McpServerTool]
    [Description("Generates a marketing leaflet in Czech Markdown using the company knowledge base and historical leaflets as style references.")]
    public async Task<string> GenerateLeaflet(
        [Description("Leaflet topic (1-200 characters), e.g. 'Bisabolol pro citlivou pleť'")] string topic,
        [Description("Audience: 'EndConsumer' or 'B2B'")] string audience,
        [Description("Length: 'Short', 'Medium', or 'Long'")] string length,
        CancellationToken ct = default)
    {
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

            return JsonSerializer.Serialize(response);
        }
        catch (McpException)
        {
            throw;
        }
        catch (EmptyRetrievalException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP GenerateLeaflet failed");
            throw new McpException("Leaflet generation failed. Please try again.");
        }
    }
}
