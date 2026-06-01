namespace Anela.Heblo.Application.Features.UserManagement.Contracts;

public class UserDto
{
    public string Id { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Email { get; set; } = null!;
}