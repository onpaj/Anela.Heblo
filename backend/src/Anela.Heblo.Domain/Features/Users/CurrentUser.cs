namespace Anela.Heblo.Domain.Features.Users;

public record CurrentUser(
    string? Id,
    string? Name,
    string? Email,
    bool IsAuthenticated
);