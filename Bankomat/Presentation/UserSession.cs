using Bankomat.Application.Accounts;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Presentation;

/// <summary>
/// Сессия клиента. Хранит снимок счёта (DTO), а не EF-сущность:
/// раньше UI держал сущность из уже закрытого DbContext и вручную «синхронизировал» баланс
/// вызовами account.Debit(...) — дублирование бизнес-логики и источник расхождений.
/// </summary>
public class UserSession
{
    public AccountSummary? CurrentAccount { get; private set; }
    public bool IsAuthenticated => CurrentAccount is not null;

    public void Start(AccountSummary account) => CurrentAccount = account;

    public void Refresh(AccountSummary account)
    {
        if (CurrentAccount is not null && CurrentAccount.Id == account.Id)
            CurrentAccount = account;
    }

    public void UpdateBalance(Money balance)
    {
        if (CurrentAccount is not null)
            CurrentAccount = CurrentAccount with { Balance = balance };
    }

    public void Logout() => CurrentAccount = null;
}
