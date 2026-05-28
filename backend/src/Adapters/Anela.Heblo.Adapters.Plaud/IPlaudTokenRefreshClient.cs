namespace Anela.Heblo.Adapters.Plaud;

public interface IPlaudTokenRefreshClient
{
    Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default);
}
