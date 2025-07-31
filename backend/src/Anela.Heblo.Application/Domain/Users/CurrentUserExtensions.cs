namespace Anela.Heblo.Application.Domain.Users;

public static class CurrentUserExtensions
{
    /// <summary>
    /// Gets the display name for the user, fallback to "System" if not authenticated
    /// </summary>
    public static string GetDisplayName(this CurrentUser user)
    {
        return user.IsAuthenticated ? user.Name ?? "Unknown User" : "System";
    }

    /// <summary>
    /// Gets the user identifier, fallback to "system" if not available
    /// </summary>
    public static string GetIdentifier(this CurrentUser user)
    {
        return user.Id ?? user.Email ?? "system";
    }
}