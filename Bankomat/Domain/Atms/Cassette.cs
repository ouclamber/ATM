using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Domain.Atms;

public class Cassette : Entity
{
    public int AtmId { get; private set; }
    public int Denomination { get; private set; }
    public int Count { get; private set; }
    public int MaxCapacity { get; private set; }

    private Cassette() { }

    internal Cassette(int atmId, int denomination, int count, int maxCapacity)
    {
        AtmId = atmId;
        Denomination = denomination;
        Count = count;
        MaxCapacity = maxCapacity;
    }

    public Money TotalAmount() => Money.Of(Denomination * Count);

    internal Result Debit(int count)
    {
        if (count < 0)
            return Error.Validation("Количество не может быть отрицательным");
        if (Count < count)
            return Error.Business($"В кассете {Denomination} ₽ недостаточно купюр (есть {Count}, нужно {count})");

        Count -= count;
        return Result.Success();
    }

    internal Result Credit(int count)
    {
        if (count < 0)
            return Error.Validation("Количество не может быть отрицательным");
        if (Count + count > MaxCapacity)
            return Error.Business($"Кассета {Denomination} ₽ переполнена (макс {MaxCapacity})");

        Count += count;
        return Result.Success();
    }
}