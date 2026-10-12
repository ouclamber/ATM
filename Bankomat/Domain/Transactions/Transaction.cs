using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Domain.Transactions;

public class Transaction : AggregateRoot
{
    public int AccountId { get; private set; }
    public int AtmId { get; private set; }
    public TransactionType Type { get; private set; }
    public Money Amount { get; private set; } = Money.Zero;
    public Money BalanceBefore { get; private set; } = Money.Zero;
    public Money BalanceAfter { get; private set; } = Money.Zero;
    public string Description { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public string? CounterpartyCard { get; private set; }

    private Transaction() { }

    public static Transaction Record(
        int accountId,
        int atmId,
        TransactionType type,
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        string description,
        string? counterpartyCard = null)
    {
        return new Transaction
        {
            AccountId = accountId,
            AtmId = atmId,
            Type = type,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            CounterpartyCard = counterpartyCard
        };
    }
}