using Bankomat.Domain.Common;

namespace Bankomat.Domain.ValueObjects;

public sealed class CardNumber : ValueObject
{
    public string Value { get; }

    private CardNumber(string value) => Value = value;

    public static Result<CardNumber> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Error.Validation("Номер карты не может быть пустым");

        var clean = input.Replace(" ", "").Replace("-", "");
        if (clean.Length != 16)
            return Error.Validation("Номер карты должен содержать 16 цифр");
        if (!clean.All(char.IsDigit))
            return Error.Validation("Номер карты должен содержать только цифры");

        return new CardNumber(clean);
    }

    public string Formatted() =>
        $"{Value[..4]} {Value[4..8]} {Value[8..12]} {Value[12..]}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}