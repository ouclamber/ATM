using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Domain.Accounts;

public class BankAccount : AggregateRoot
{
    public CardNumber CardNumber { get; private set; } = null!;
    public string PinHash { get; private set; } = string.Empty;
    public string HolderName { get; private set; } = string.Empty;
    public Money Balance { get; private set; } = Money.Zero;
    public bool IsBlocked { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastAccessAt { get; private set; }

    private const int MaxFailedAttempts = 3;

    private BankAccount() { } // EF

    // Фабричный метод — единственный способ создать счёт
    public static Result<BankAccount> Create(
        CardNumber cardNumber,
        string pinHash,
        string holderName,
        Money initialBalance)
    {
        if (string.IsNullOrWhiteSpace(pinHash))
            return Error.Validation("Хэш PIN не может быть пустым");
        if (string.IsNullOrWhiteSpace(holderName))
            return Error.Validation("Имя держателя не может быть пустым");

        return new BankAccount
        {
            CardNumber = cardNumber,
            PinHash = pinHash,
            HolderName = holderName,
            Balance = initialBalance,
            IsBlocked = false,
            FailedLoginAttempts = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    public Result RegisterFailedLogin()
    {
        if (IsBlocked)
            return Error.Forbidden("Карта уже заблокирована");

        FailedLoginAttempts++;

        if (FailedLoginAttempts >= MaxFailedAttempts)
        {
            IsBlocked = true;
            return Error.Forbidden($"Карта заблокирована после {MaxFailedAttempts} неудачных попыток");
        }

        return Result.Success();
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LastAccessAt = DateTime.UtcNow;
    }

    public Result Debit(Money amount)
    {
        if (IsBlocked)
            return Error.Forbidden("Карта заблокирована");
        if (amount <= Money.Zero)
            return Error.Validation("Сумма списания должна быть больше нуля");
        if (Balance < amount)
            return Error.Business("Недостаточно средств на счете");

        Balance -= amount;
        return Result.Success();
    }

    public Result Credit(Money amount)
    {
        if (amount <= Money.Zero)
            return Error.Validation("Сумма зачисления должна быть больше нуля");

        Balance += amount;
        return Result.Success();
    }

    public Result ChangePinHash(string newPinHash)
    {
        if (string.IsNullOrWhiteSpace(newPinHash))
            return Error.Validation("Хэш PIN не может быть пустым");

        PinHash = newPinHash;
        return Result.Success();
    }
}