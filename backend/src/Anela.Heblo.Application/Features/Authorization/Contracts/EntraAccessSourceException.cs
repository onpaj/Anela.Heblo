namespace Anela.Heblo.Application.Features.Authorization.Contracts;

/// <summary>
/// Thrown by <see cref="IEntraAccessUserSource"/> implementations for unexpected failures
/// that are not an auth/configuration problem.
/// </summary>
public sealed class EntraAccessSourceException : Exception
{
    public EntraAccessSourceException(string message, Exception innerException)
        : base(message, innerException) { }
}
