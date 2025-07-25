namespace Anela.Heblo.Application.Interfaces;

public interface IUserService
{
    Task<string> GetCurrentUserNameAsync();
}