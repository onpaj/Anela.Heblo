namespace Anela.Heblo.Application.Features.Authorization.Contracts;

/// <summary>
/// Thrown by <see cref="IEntraAccessUserSource"/> implementations when the underlying
/// identity provider could not be reached due to an authentication/configuration failure.
/// </summary>
public sealed class EntraAccessSourceAuthException : Exception
{
    public EntraAccessSourceAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}
