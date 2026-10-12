using Bankomat.Domain.Accounts;

namespace Bankomat.Presentation;

public class UserSession
{
    public BankAccount? CurrentAccount { get; set; }
    public bool IsAuthenticated => CurrentAccount is not null;

    public void Logout() => CurrentAccount = null;
}