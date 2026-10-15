using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>
/// Supplies the keys that may sign an E2E Service Principal token.
/// Kept behind an interface so tests can sign tokens with a local key instead of calling Entra.
/// </summary>
public interface IE2ETokenSigningKeySource
{
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken);

    /// <summary>Asks for fresh keys on the next read (e.g. a token names a key id we have not seen yet).</summary>
    void RequestRefresh();
}

/// <summary>
/// Reads the signing keys from the configured tenant's Entra OpenID configuration.
/// Registered as a singleton: <see cref="ConfigurationManager{T}"/> caches the metadata and refreshes it
/// on its own schedule. The same JWKS signs v1 and v2 tokens.
/// </summary>
public sealed class EntraE2ETokenSigningKeySource : IE2ETokenSigningKeySource
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;

    public EntraE2ETokenSigningKeySource(IConfiguration configuration)
    {
        var tenantId = configuration[ServicePrincipalTokenValidator.ExpectedTenantIdKey];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // The validator fails closed before asking for keys when the tenant is not configured.
            return;
        }

        var metadataAddress = $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration";
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken)
    {
        if (_configurationManager == null)
        {
            return Array.Empty<SecurityKey>();
        }

        var configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        return configuration.SigningKeys.ToList();
    }

    // ConfigurationManager rate-limits this to one real refetch per RefreshInterval (5 minutes by default).
    public void RequestRefresh() => _configurationManager?.RequestRefresh();
}
