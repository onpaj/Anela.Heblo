namespace Anela.Heblo.Application.Domain.Users;

public record CurrentUser(
    string? Id,
    string? Name,
    string? Email,
    bool IsAuthenticated
);