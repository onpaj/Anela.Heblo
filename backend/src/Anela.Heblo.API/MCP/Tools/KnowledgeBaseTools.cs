using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

[McpServerToolType]
public class KnowledgeBaseTools
{
    private readonly IMediator _mediator;
    private readonly ILogger<KnowledgeBaseTools> _logger;

    public KnowledgeBaseTools(IMediator mediator, ILogger<KnowledgeBaseTools> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [McpServerTool]
    [Description("Search the knowledge base for relevant document chunks using semantic similarity. Returns raw chunks with source references.")]
    public async Task<string> SearchKnowledgeBase(
        [Description("Natural language search query")] string query,
        [Description("Number of chunks to return (default: 5)")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _mediator.Send(new SearchDocumentsRequest
            {
                Query = query,
                TopK = topK
            }, cancellationToken);
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP SearchKnowledgeBase failed for query '{Query}'", query);
            throw new McpException($"Failed to search knowledge base: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Ask a question and get an AI-generated answer grounded in company documents. Returns a prose answer with cited sources.")]
    public async Task<string> AskKnowledgeBase(
        [Description("Question to answer using the knowledge base")] string question,
        [Description("Number of context chunks to retrieve (default: 5)")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _mediator.Send(new AskQuestionRequest
            {
                Question = question,
                TopK = topK
            }, cancellationToken);
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP AskKnowledgeBase failed for question '{Question}'", question);
            throw new McpException($"Failed to answer question: {ex.Message}");
        }
    }
}
