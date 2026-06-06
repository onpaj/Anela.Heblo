namespace Anela.Heblo.Application.Features.Authorization.UseCases;

public class AppUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<Guid> GroupIds { get; set; } = new();
}
