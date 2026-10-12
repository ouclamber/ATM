using Bankomat.Domain.Common;

namespace Bankomat.Domain.ValueObjects;

public sealed class Denomination : ValueObject
{
    private static readonly int[] Allowed = { 100, 500, 1000, 2000, 5000 };

    public int Value { get; }

    private Denomination(int value) => Value = value;

    public static Result<Denomination> Create(int value)
    {
        if (!Allowed.Contains(value))
            return Error.Validation($"Недопустимый номинал: {value}. Разрешены: {string.Join(", ", Allowed)}");
        return new Denomination(value);
    }

    public static IEnumerable<int> AllValues => Allowed;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value} ₽";
}