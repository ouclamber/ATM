using Bankomat.Domain.Accounts;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Application.Accounts;

/// <summary>
/// Снимок счёта для слоя представления. UI не получает сущность-агрегат,
/// поэтому не может случайно изменить её в обход обработчиков.
/// </summary>
public sealed record AccountSummary(
    int Id,
    string CardNumber,
    string HolderName,
    Money Balance,
    bool IsBlocked)
{
    public string FormattedCard =>
        CardNumber.Length == 16
            ? $"{CardNumber[..4]} {CardNumber[4..8]} {CardNumber[8..12]} {CardNumber[12..]}"
            : CardNumber;

    public static AccountSummary From(BankAccount a) =>
        new(a.Id, a.CardNumber.Value, a.HolderName, a.Balance, a.IsBlocked);
}
