namespace Anela.Heblo.Application.Features.UserManagement.Contracts;

/// <summary>
/// Thrown by <see cref="IGraphService"/> implementations when the remote
/// directory service returns an error response (e.g. an OData error from Microsoft Graph).
/// Wraps infrastructure-specific service exceptions so that Application-layer consumers
/// remain decoupled from SDK packages.
/// </summary>
public sealed class GraphServiceException : Exception
{
    public GraphServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
