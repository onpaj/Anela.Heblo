namespace Anela.Heblo.Domain.Features.Users;

public interface ICurrentUserService
{
    CurrentUser GetCurrentUser();
    bool IsInRole(string role);

    /// <summary>Client application that obtained the caller's token: <c>azp</c> (v2) or
    /// <c>appid</c> (v1). Null when the token carries neither (cookie sessions, anonymous).</summary>
    string? ClientAppId { get; }

    /// <summary>True for an authenticated bearer request whose token was issued to an
    /// application itself (client credentials): <c>idtyp = app</c> or no <c>scp</c> claim.
    /// Such callers are service identities and may never approve anything.</summary>
    bool IsAppOnly { get; }

    /// <summary>True when the request authenticated with an <c>Authorization: Bearer</c>
    /// header; false for cookie sessions (web-app login, E2E session).</summary>
    bool IsBearerToken { get; }
}
