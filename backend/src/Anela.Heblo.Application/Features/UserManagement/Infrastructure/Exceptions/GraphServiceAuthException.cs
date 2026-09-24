namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;

/// <summary>
/// Thrown by <see cref="Anela.Heblo.Application.Features.UserManagement.Services.IGraphService"/> implementations when token acquisition
/// or authentication for the underlying identity provider fails.
/// Wraps infrastructure-specific auth exceptions (e.g. MsalException) so that
/// Application-layer consumers remain decoupled from SDK packages.
/// </summary>
public sealed class GraphServiceAuthException : Exception
{
    public GraphServiceAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}
