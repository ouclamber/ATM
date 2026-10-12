namespace Bankomat.Application.Abstractions;

public interface IAdminAuthService
{
    bool Verify(string username, string password);
}