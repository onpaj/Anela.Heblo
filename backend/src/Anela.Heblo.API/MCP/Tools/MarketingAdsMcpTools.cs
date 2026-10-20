using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using FluentValidation;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP surface of the marketing agents platform (spec 7.5): ad data for agents, proposals,
/// agent-run reporting and the chief's approvals. Thin wrappers over the MarketingAds MediatR
/// handlers. Approve/reject channel rules (web vs MCP vs service identity) are enforced inside
/// the handlers via IApprovalChannelResolver, so REST and MCP behave the same.
/// </summary>
[McpServerToolType]
public class MarketingAdsMcpTools
{
    private const string AdsResource = "Marketing Ads";
    private const string ApprovalsResource = "Marketing Ad Approvals";
    private const string DateFormat = "yyyy-MM-dd";
    private const int DefaultPageSize = 50;
    private const int DefaultSearchTermsTop = 200;
    private const int MaxPendingItems = 50;
    private const string ConcurrentChangeMessage =
        "Someone changed this at the same time. Reload it (GetAdProposal) and retry.";

    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<MarketingAdsMcpTools> _logger;

    public MarketingAdsMcpTools(IMediator mediator, ICurrentUserService currentUserService, ILogger<MarketingAdsMcpTools> logger)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    [McpServerTool]
    [Description("List the ad accounts Heblo manages (Google Ads, Meta Ads, Sklik): external id, name, currency, time zone. " +
                 "Use the external id as accountExternalId in the other ad tools.")]
    public async Task<string> ListAdAccounts(
        [Description("Optional platform filter: GoogleAds, MetaAds or Sklik")] AdPlatform? platform = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        var response = await SendAsync(new GetAdAccountsRequest { Platform = platform }, "list ad accounts", cancellationToken);
        return Serialize(response);
    }

    [McpServerTool]
    [Description("Daily platform-reported performance (impressions, clicks, cost net of VAT in account currency, conversions, " +
                 "conversion value) for ONE entity level of one account and date range. Never sum rows of different levels. " +
                 "Optionally restrict to children of one parent (e.g. ad groups of a campaign). Paged.")]
    public async Task<string> GetAdPerformance(
        [Description("GoogleAds, MetaAds or Sklik")] AdPlatform platform,
        [Description("Account external id from ListAdAccounts")] string accountExternalId,
        [Description("Campaign, AdGroup, Keyword or Ad")] AdEntityLevel level,
        [Description("First day, yyyy-MM-dd")] string from,
        [Description("Last day (inclusive), yyyy-MM-dd")] string to,
        [Description("Optional parent entity external id")] string? parentExternalId = null,
        [Description("Page number (default 1)")] int pageNumber = 1,
        [Description("Page size (default 50)")] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);
        var account = RequireText(accountExternalId, nameof(accountExternalId));
        var (fromDate, toDate) = ParseRange(from, to);

        var request = new GetAdPerformanceRequest
        {
            Platform = platform,
            AccountExternalId = account,
            Level = level,
            From = fromDate,
            To = toDate,
            ParentExternalId = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim(),
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
        return Serialize(await SendAsync(request, "get ad performance", cancellationToken));
    }

    [McpServerTool]
    [Description("Search terms (what people actually typed) with metrics for one campaign or ad group and date range, " +
                 "highest cost first, limited to the top N. Google Ads and Sklik only; Meta has no search terms. " +
                 "Use it to find negative-keyword candidates.")]
    public async Task<string> GetAdSearchTerms(
        [Description("GoogleAds or Sklik")] AdPlatform platform,
        [Description("Account external id from ListAdAccounts")] string accountExternalId,
        [Description("Campaign or AdGroup")] AdEntityLevel parentLevel,
        [Description("External id of that campaign / ad group")] string parentExternalId,
        [Description("First day, yyyy-MM-dd")] string from,
        [Description("Last day (inclusive), yyyy-MM-dd")] string to,
        [Description("Optional minimum cost of a search term, in account currency")] decimal? minCost = null,
        [Description("How many search terms to return (default 200)")] int top = DefaultSearchTermsTop,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);
        if (parentLevel is not (AdEntityLevel.Campaign or AdEntityLevel.AdGroup))
        {
            throw ValidationError($"parentLevel must be Campaign or AdGroup (got {parentLevel}).");
        }

        var (fromDate, toDate) = ParseRange(from, to);
        var parentId = RequireText(parentExternalId, nameof(parentExternalId));
        var request = new GetAdSearchTermsRequest
        {
            Platform = platform,
            AccountExternalId = RequireText(accountExternalId, nameof(accountExternalId)),
            CampaignExternalId = parentLevel == AdEntityLevel.Campaign ? parentId : null,
            AdGroupExternalId = parentLevel == AdEntityLevel.AdGroup ? parentId : null,
            From = fromDate,
            To = toDate,
            MinCost = minCost,
            Top = top,
        };
        return Serialize(await SendAsync(request, "get ad search terms", cancellationToken));
    }

    [McpServerTool]
    [Description("Blended reality check: real e-shop revenue (net of VAT, cancelled orders excluded) divided by total ad cost " +
                 "of all platforms, daily or monthly. Platforms over-count conversions; use this to sanity-check them.")]
    public async Task<string> GetBlendedPerformance(
        [Description("First day, yyyy-MM-dd")] string from,
        [Description("Last day (inclusive), yyyy-MM-dd")] string to,
        [Description("Daily or Monthly (default Monthly)")] BlendedGranularity granularity = BlendedGranularity.Monthly,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);
        var (fromDate, toDate) = ParseRange(from, to);

        var request = new GetBlendedPerformanceRequest { From = fromDate, To = toDate, Granularity = granularity };
        return Serialize(await SendAsync(request, "get blended performance", cancellationToken));
    }

    [McpServerTool]
    [Description("Changes made to ad accounts — by Heblo, the agency, people in the platform UI or platform auto-applied " +
                 "recommendations — each flagged Heblo or OutOfBand. Filter by platform, account, time, origin or actor kind. Paged.")]
    public async Task<string> GetAdChangeHistory(
        [Description("Optional platform filter")] AdPlatform? platform = null,
        [Description("Optional account external id")] string? accountExternalId = null,
        [Description("Optional start, ISO-8601 (e.g. 2026-09-01T00:00:00Z); default 30 days before 'until'")] string? since = null,
        [Description("Optional end, ISO-8601; default now")] string? until = null,
        [Description("Optional origin filter: Heblo or OutOfBand")] AdChangeOrigin? origin = null,
        [Description("Optional actor filter: Heblo, User, PlatformAutomation, Unknown")] AdChangeActorKind? actorKind = null,
        [Description("Page number (default 1)")] int pageNumber = 1,
        [Description("Page size (default 50)")] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        var request = new GetAdChangeHistoryRequest
        {
            Platform = platform,
            AccountExternalId = string.IsNullOrWhiteSpace(accountExternalId) ? null : accountExternalId.Trim(),
            From = ParseInstant(since, nameof(since)),
            To = ParseInstant(until, nameof(until)),
            Origin = origin,
            ActorKind = actorKind,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
        return Serialize(await SendAsync(request, "get ad change history", cancellationToken));
    }

    [McpServerTool]
    [Description("The rules a proposal must fit: per-proposal and per-week limits (including the stricter limits for " +
                 "approvals via Claude), autonomy modes per action type, kill switches and whether execution is switched on. " +
                 "Read this before proposing so proposals are not refused.")]
    public async Task<string> GetAdGuardrails(CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        return Serialize(await SendAsync(new GetAdSettingsRequest(), "get ad guardrails", cancellationToken));
    }

    [McpServerTool]
    [Description("Submit a proposal: actions of ONE action type on ONE account, plus your reasoning (shown to the " +
                 "approver separately; the human-readable diff is rendered by Heblo from the actions, never from your text). " +
                 "Returns the proposal id, version and the limits evaluation. Nothing is executed until a human approves. " +
                 "Call GetAdGuardrails first.")]
    public async Task<string> SubmitAdProposal(
        [Description("GoogleAds, MetaAds or Sklik")] AdPlatform platform,
        [Description("Account external id from ListAdAccounts")] string accountExternalId,
        [Description("AddNegativeKeyword or PauseAd")] AdActionType actionType,
        [Description("Actions, each: targetLevel (Campaign | AdGroup | Ad), targetExternalId, and for AddNegativeKeyword " +
                     "also keywordText and matchType (Exact | Phrase | Broad); PauseAd needs only targetLevel and targetExternalId")]
        List<AdActionInputDto> actions,
        [Description("Why — evidence and expected effect")] string reasoning,
        [Description("Optional id from StartAgentRun")] Guid? agentRunId = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource, AccessLevel.Write);

        var request = new SubmitAdProposalRequest
        {
            Platform = platform,
            AccountExternalId = RequireText(accountExternalId, nameof(accountExternalId)),
            ActionType = actionType,
            Actions = RequireActions(actions),
            Reasoning = RequireText(reasoning, nameof(reasoning)),
            AgentRunId = agentRunId,
        };
        return Serialize(await SendAsync(request, "submit ad proposal", cancellationToken));
    }

    [McpServerTool]
    [Description("Revise a pending proposal: creates version+1 and voids earlier approvals. expectedVersion must be the " +
                 "current version, otherwise the call fails and you should re-read the proposal.")]
    public async Task<string> ReviseAdProposal(
        [Description("Proposal id")] int proposalId,
        [Description("The version you are revising (the current one)")] int expectedVersion,
        [Description("The complete new action list (same rules as SubmitAdProposal)")] List<AdActionInputDto> actions,
        [Description("Updated reasoning")] string reasoning,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource, AccessLevel.Write);

        var request = new ReviseAdProposalRequest
        {
            Id = proposalId,
            ExpectedVersion = expectedVersion,
            Actions = RequireActions(actions),
            Reasoning = RequireText(reasoning, nameof(reasoning)),
        };
        return Serialize(await SendAsync(request, "revise ad proposal", cancellationToken));
    }

    [McpServerTool]
    [Description("List proposals with their status, newest first. Filter by status " +
                 "(Pending, Approved, Executing, Executed, PartiallyExecuted, Failed, Rejected, Expired, Reverted), " +
                 "platform, and onlyMine=true for proposals you created. Paged.")]
    public async Task<string> ListAdProposals(
        [Description("Optional status filter")] AdProposalStatus? status = null,
        [Description("Optional platform filter")] AdPlatform? platform = null,
        [Description("Only proposals created by the caller (default false)")] bool onlyMine = false,
        [Description("Page number (default 1)")] int pageNumber = 1,
        [Description("Page size (default 50)")] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        var request = new ListAdProposalsRequest
        {
            Status = status,
            Platform = platform,
            Mine = onlyMine,
            Page = pageNumber,
            PageSize = pageSize,
        };
        return Serialize(await SendAsync(request, "list ad proposals", cancellationToken));
    }

    [McpServerTool]
    [Description("One proposal in full: every version with its rendered diff, the reasoning, approvals, " +
                 "limits evaluation and per-action execution results.")]
    public async Task<string> GetAdProposal(
        [Description("Proposal id")] int proposalId,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource);

        return Serialize(await SendAsync(new GetAdProposalRequest { Id = proposalId }, "get ad proposal", cancellationToken));
    }

    [McpServerTool]
    [Description("Report the start of an agent run (observability of the agents themselves). Returns agentRunId; pass it " +
                 "to SubmitAdProposal and to FinishAgentRun.")]
    public async Task<string> StartAgentRun(
        [Description("Stable agent name, e.g. paid-search-agent")] string agentName,
        [Description("Optional data scopes the run reads, e.g. performance, search_terms, change_history")] List<string>? dataScopesRead = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource, AccessLevel.Write);

        var request = new StartAgentRunRequest
        {
            AgentName = RequireText(agentName, nameof(agentName)),
            DataScopesRead = dataScopesRead ?? new List<string>(),
        };
        return Serialize(await SendAsync(request, "start agent run", cancellationToken));
    }

    [McpServerTool]
    [Description("Report the end of an agent run: status, short summary, which data scopes were read and the approximate " +
                 "cost (input/output tokens, CZK).")]
    public async Task<string> FinishAgentRun(
        [Description("Id from StartAgentRun")] Guid agentRunId,
        [Description("Final status: Succeeded or Failed")] AgentRunStatus status,
        [Description("Optional short summary")] string? summary = null,
        [Description("Optional data scopes read, e.g. performance, search_terms, change_history")] List<string>? dataScopesRead = null,
        [Description("Optional input tokens used")] long? inputTokens = null,
        [Description("Optional output tokens used")] long? outputTokens = null,
        [Description("Optional approximate cost in CZK")] decimal? costCzk = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_Ads, AdsResource, AccessLevel.Write);

        var request = new FinishAgentRunRequest
        {
            AgentRunId = agentRunId,
            Status = status,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            DataScopesRead = dataScopesRead ?? new List<string>(),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostCzk = costCzk,
        };
        return Serialize(await SendAsync(request, "finish agent run", cancellationToken));
    }

    [McpServerTool]
    [Description("The chief's inbox: pending ad proposals, oldest first, numbered 1..n, each with id, version and the diff " +
                 "Heblo rendered from the structured actions. To decide, call ApproveAdProposal / RejectAdProposal with the " +
                 "item's Id and Version (e.g. 'approve 1-4, reject 5' = four approvals and one rejection).")]
    public async Task<string> ListPendingAdProposals(
        [Description("Maximum items to show (1-50, default 50)")] int maxItems = MaxPendingItems,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_AdApprovals, ApprovalsResource, AccessLevel.Write);

        var request = new ListAdProposalsRequest
        {
            Status = AdProposalStatus.Pending,
            Mine = false,
            OldestFirst = true,
            Page = 1,
            PageSize = Math.Clamp(maxItems, 1, MaxPendingItems),
        };
        var response = await SendAsync(request, "list pending ad proposals", cancellationToken);

        var pending = response.Items
            .OrderBy(p => p.CreatedAt)
            .Select((p, index) => new
            {
                Number = index + 1,
                p.Id,
                Version = p.CurrentVersion,
                p.Platform,
                p.AccountExternalId,
                p.ActionType,
                p.ActionCount,
                CreatedBy = p.CreatedByPrincipal,
                p.CreatedByKind,
                p.CreatedAt,
                p.ExpiresAt,
                Diff = p.DiffLines,
            })
            .ToList();

        return Serialize(new
        {
            Pending = pending,
            Shown = pending.Count,
            TotalPending = response.TotalCount,
            HowToDecide = "Approve or reject by Id and Version. A version that is no longer current is refused; list again.",
        });
    }

    [McpServerTool]
    [Description("Approve exactly one version of a pending proposal. Allowed only for a signed-in person holding the " +
                 "approve-via-Claude permission; service identities are always refused. Larger proposals may need the web UI " +
                 "or a second approver — the response says so. Execution is done by Heblo afterwards.")]
    public async Task<string> ApproveAdProposal(
        [Description("Proposal id")] int proposalId,
        [Description("The version being approved (as listed)")] int version,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_AdApprovals, ApprovalsResource, AccessLevel.Write);

        var request = new ApproveAdProposalRequest { Id = proposalId, Version = version };
        return Serialize(await SendAsync(request, "approve ad proposal", cancellationToken));
    }

    [McpServerTool]
    [Description("Reject exactly one version of a pending proposal, with a reason the agent can learn from. Same caller " +
                 "rules as ApproveAdProposal.")]
    public async Task<string> RejectAdProposal(
        [Description("Proposal id")] int proposalId,
        [Description("The version being rejected (as listed)")] int version,
        [Description("Why it is rejected")] string reason,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Marketing_AdApprovals, ApprovalsResource, AccessLevel.Write);

        var request = new RejectAdProposalRequest
        {
            Id = proposalId,
            Version = version,
            Reason = RequireText(reason, nameof(reason)),
        };
        return Serialize(await SendAsync(request, "reject ad proposal", cancellationToken));
    }

    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, string operation, CancellationToken cancellationToken)
        where TResponse : BaseResponse
    {
        TResponse response;
        try
        {
            response = await _mediator.Send(request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            var details = string.Join(" | ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            throw new McpException($"[{ErrorCodes.ValidationError}] {details}");
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "MCP marketing ads call failed: {Operation}", operation);
            throw new McpException($"Failed to {operation}: {ex.Message}");
        }

        EnsureSuccess(response);
        return response;
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, McpJsonOptions.Default);

    private static void EnsureSuccess(BaseResponse response)
    {
        if (response.Success)
        {
            return;
        }

        var code = response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR";
        if (response.ErrorCode == ErrorCodes.MarketingAdsConcurrentChange)
        {
            throw new McpException($"[{code}] {ConcurrentChangeMessage}");
        }

        // FullError() dereferences Params, which is null when a handler reports a bare error code.
        var detail = response.Params is { Count: > 0 } ? response.FullError() : code;
        throw new McpException($"[{code}] {detail}");
    }

    private static McpException ValidationError(string message) => new($"[{ErrorCodes.ValidationError}] {message}");

    private static string RequireText(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw ValidationError($"{parameterName} must not be empty.")
            : value.Trim();

    private static List<AdActionInputDto> RequireActions(List<AdActionInputDto>? actions) =>
        actions is { Count: > 0 }
            ? actions
            : throw ValidationError("actions must contain at least one action.");

    private static DateOnly ParseDate(string? value, string parameterName) =>
        DateOnly.TryParseExact(value?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw ValidationError($"{parameterName} must be a date in {DateFormat} format (got '{value}').");

    private static (DateOnly From, DateOnly To) ParseRange(string? from, string? to)
    {
        var fromDate = ParseDate(from, nameof(from));
        var toDate = ParseDate(to, nameof(to));
        return fromDate <= toDate
            ? (fromDate, toDate)
            : throw ValidationError($"from ({from}) must not be after to ({to}).");
    }

    private static DateTimeOffset? ParseInstant(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
            ? instant
            : throw ValidationError($"{parameterName} must be an ISO-8601 date-time (got '{value}').");
    }
}
