using Bankomat.Domain.Common;

namespace Bankomat.Domain.ValueObjects;

public sealed class Money : ValueObject
{
    public decimal Amount { get; }

    private Money(decimal amount) => Amount = amount;

    public static Result<Money> Create(decimal amount)
    {
        if (amount < 0)
            return Error.Validation("Сумма не может быть отрицательной");
        if (decimal.Round(amount, 2) != amount)
            return Error.Validation("Сумма должна быть с точностью до копеек");
        return new Money(amount);
    }

    public static Money Zero => new(0m);
    public static Money Of(decimal amount) => new(amount);

    public bool IsMultipleOf(int value) => Amount % value == 0;

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
    }

    public override string ToString() => $"{Amount:C}";
}