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
        "List Heblo's documentation catalog: one overview doc per module (kind 'module') plus one doc per process — " +
        "data syncs (external system -> Heblo), calculations (derived numbers such as margins), feeds (Heblo -> outside), " +
        "jobs (other scheduled/background work) and workflows (user-driven processes with side effects, e.g. packing, " +
        "manufacture orders). Call this FIRST whenever the user asks what Heblo does in some area, where a number or " +
        "dataset comes from, how something is calculated, or when/how data gets updated. To orient, list kind='module' " +
        "first, then filter by module. Returns name, kind, module, one-line summary and related processes; then call " +
        "GetProcessDoc for the relevant one.")]
    public async Task<string> ListProcesses(
        [Description("Optional filter: 'module', 'sync', 'calculation' (or 'calc'), 'feed', 'job' or 'workflow' (or 'flow'). Omit to list all.")] string? kind = null,
        [Description("Optional module slug filter, e.g. 'catalog', 'manufacture', 'bank'. Omit for all modules.")] string? module = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Anela_ProcessDocs, FeatureLabel);

        var result = await _mediator.Send(new ListProcessesRequest { Kind = kind, Module = module }, cancellationToken);
        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description(
        "Get the full documentation of one Heblo process or module. A process doc has purpose, trigger/schedule, data " +
        "flow, exact formulas, configuration, runtime facts, known quirks and code entry points; a module doc ('module-<slug>') " +
        "has purpose, screens, its processes, owned data, external systems and dependencies. Follow 'related' processes for upstream " +
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
