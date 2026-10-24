namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// Thrown when the Plaud CLI reports that Plaud's own upstream API answered with a 5xx status.
/// Distinct from an auth failure: the CLI ran and authenticated, the remote service errored.
/// </summary>
public sealed class PlaudUpstreamUnavailableException : InvalidOperationException
{
    public int StatusCode { get; }

    public PlaudUpstreamUnavailableException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
