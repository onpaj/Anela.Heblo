namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// A known organisation user the extraction LLM can assign tasks to.
/// Internal domain type — may be a record (not exposed via OpenAPI).
/// </summary>
public sealed record MeetingUser(string Email, string DisplayName, IReadOnlyList<string> Aliases);
