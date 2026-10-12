using Bankomat.Domain.Common;

namespace Bankomat.Domain.ValueObjects;

public sealed class PinCode : ValueObject
{
    public string Value { get; }

    private PinCode(string value) => Value = value;

    public static Result<PinCode> Create(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return Error.Validation("PIN не может быть пустым");
        if (input.Length != 4)
            return Error.Validation("PIN должен содержать 4 цифры");
        if (!input.All(char.IsDigit))
            return Error.Validation("PIN должен содержать только цифры");

        return new PinCode(input);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => "****";
}