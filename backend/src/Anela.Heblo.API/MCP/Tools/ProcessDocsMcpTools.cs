using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.GetProcessDoc;
using Anela.Heblo.Application.Features.ProcessDocs.UseCases.ListProcesses;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

[McpServerToolType]
public class ProcessDocsMcpTools
{
    private const string FeatureLabel = "Process docs";

    private readonly IMediator _mediator;
    private readonly ILogger<ProcessDocsMcpTools> _logger;
    private readonly ICurrentUserService _currentUserService;

    public ProcessDocsMcpTools(IMediator mediator, ILogger<ProcessDocsMcpTools> logger, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    [McpServerTool]
    [Description(
        "List Heblo's documented processes — data syncs (external system -> Heblo), calculations (derived numbers " +
        "such as margins, pricing, stock-up) and feeds (Heblo -> outside). Call this FIRST whenever the user asks " +
        "where a number or dataset comes from, how something is calculated, or when/how data gets updated. " +
        "Returns name, kind, one-line summary and related processes; then call GetProcessDoc for the relevant one.")]
    public async Task<string> ListProcesses(
        [Description("Optional filter: 'sync', 'calculation' (or 'calc') or 'feed'. Omit to list all.")] string? kind = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Anela_ProcessDocs, FeatureLabel);

        var result = await _mediator.Send(new ListProcessesRequest { Kind = kind }, cancellationToken);
        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description(
        "Get the full documentation of one Heblo process: purpose, trigger/schedule, data flow, exact formulas, " +
        "configuration, runtime facts, known quirks and code entry points. Follow 'related' processes for upstream " +
        "questions (e.g. where a cost used in a margin comes from). When answering, cite the process name and its " +
        "VerifiedAt commit, and treat 'Runtime facts' as true only as of the date written next to each fact.")]
    public async Task<string> GetProcessDoc(
        [Description("Process name exactly as returned by ListProcesses, e.g. 'calc-margins'.")] string name,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Anela_ProcessDocs, FeatureLabel);

        var result = await _mediator.Send(new GetProcessDocRequest { Name = name }, cancellationToken);
        if (result.Doc is null)
        {
            _logger.LogInformation("MCP GetProcessDoc: unknown process '{Name}'", name);
            var hint = result.Suggestions.Count > 0
                ? $" Did you mean: {string.Join(", ", result.Suggestions)}?"
                : string.Empty;
            throw new McpException($"No process named '{name}'.{hint} Call ListProcesses for all names.");
        }

        return JsonSerializer.Serialize(result.Doc, McpJsonOptions.Default);
    }
}
