using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Tests.Application.Helpers;

internal static class TestData
{
    public static BankAccount CreateAccount(
        string card = "1111222233334444",
        string pinHash = "hash",
        string holder = "Test User",
        decimal balance = 1000m,
        int id = 0,
        bool isBlocked = false)
    {
        var account = BankAccount.Create(
            CardNumber.Create(card).Value,
            pinHash,
            holder,
            Money.Of(balance)).Value;

        if (isBlocked)
        {
            account.RegisterFailedLogin();
            account.RegisterFailedLogin();
            account.RegisterFailedLogin();
        }

        if (id > 0)
        {
            // Id — protected set, ставим через рефлексию
            typeof(BankAccount)
                .GetProperty(nameof(BankAccount.Id))!
                .SetValue(account, id);
        }

        return account;
    }

    public static Atm CreateAtm(
        string name = "Test ATM",
        string location = "Test Location",
        int id = 0,
        bool isActive = true)
    {
        var atm = Atm.Create(name, location).Value;

        atm.AddCassette(5000, 100, 200);
        atm.AddCassette(2000, 100, 200);
        atm.AddCassette(1000, 100, 200);
        atm.AddCassette(500,  100, 200);
        atm.AddCassette(100,  100, 500);

        if (id > 0)
        {
            typeof(Atm)
                .GetProperty(nameof(Atm.Id))!
                .SetValue(atm, id);
        }

        if (!isActive)
            atm.Deactivate();

        return atm;
    }
}