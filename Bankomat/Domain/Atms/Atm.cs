using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Domain.Atms;

public class Atm : AggregateRoot
{
    private readonly List<Cassette> _cassettes = new();
    public IReadOnlyCollection<Cassette> Cassettes => _cassettes.AsReadOnly();

    public string Name { get; private set; } = string.Empty;
    public string Location { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime LastMaintenanceAt { get; private set; }

    private Atm() { } // EF

    public static Result<Atm> Create(string name, string location)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Название банкомата не может быть пустым");
        if (string.IsNullOrWhiteSpace(location))
            return Error.Validation("Адрес не может быть пустым");

        return new Atm
        {
            Name = name,
            Location = location,
            IsActive = true,
            LastMaintenanceAt = DateTime.UtcNow
        };
    }

    public Money TotalCash() =>
        _cassettes.Aggregate(Money.Zero, (sum, c) => sum + c.TotalAmount());

    public Result AddCassette(int denomination, int count, int maxCapacity)
    {
        var denomResult = Denomination.Create(denomination);
        if (denomResult.IsFailure)
            return denomResult.Error;

        if (_cassettes.Any(c => c.Denomination == denomination))
            return Error.Conflict($"Кассета {denomination} ₽ уже существует");

        if (count < 0)
            return Error.Validation("Количество не может быть отрицательным");

        if (maxCapacity <= 0)
            return Error.Validation("Максимальная вместимость должна быть больше 0");

        if (count > maxCapacity)
            return Error.Validation($"Количество ({count}) превышает вместимость ({maxCapacity})");

        _cassettes.Add(new Cassette(Id, denomination, count, maxCapacity));
        return Result.Success();
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public Result<WithdrawalPlan> PlanWithdrawal(Money amount)
    {
        if (!IsActive)
            return Error.Business("Банкомат неактивен");
        if (amount <= Money.Zero)
            return Error.Validation("Сумма должна быть больше нуля");

        return CassetteSet.PlanWithdrawal(_cassettes, amount);
    }

    /// <summary>
    /// Списывает купюры по плану. Операция атомарна: сначала проверяются все кассеты,
    /// и только потом меняется состояние, поэтому при ошибке агрегат не остаётся «наполовину изменённым».
    /// </summary>
    public Result ApplyWithdrawal(WithdrawalPlan plan)
    {
        if (!IsActive)
            return Error.Business("Банкомат неактивен");

        foreach (var (denomination, count) in plan.Banknotes)
        {
            if (count < 0)
                return Error.Validation("Количество купюр не может быть отрицательным");

            var cassette = FindCassette(denomination);
            if (cassette is null)
                return Error.Business($"Кассета {denomination} ₽ не найдена");
            if (cassette.Count < count)
                return Error.Business(
                    $"В кассете {denomination} ₽ недостаточно купюр (есть {cassette.Count}, нужно {count})");
        }

        foreach (var (denomination, count) in plan.Banknotes)
            FindCassette(denomination)!.Debit(count);

        return Result.Success();
    }

    /// <summary>Приём наличных от клиента. Атомарно: либо принимаются все купюры, либо ни одной.</summary>
    public Result DepositBanknotes(IReadOnlyDictionary<int, int> banknotes)
    {
        if (!IsActive)
            return Error.Business("Банкомат неактивен");

        return AddBanknotes(banknotes);
    }

    /// <summary>Инкассация (пополнение кассет администратором). Обновляет дату обслуживания.</summary>
    public Result Refill(IReadOnlyDictionary<int, int> banknotes)
    {
        var result = AddBanknotes(banknotes);
        if (result.IsSuccess)
            LastMaintenanceAt = DateTime.UtcNow;
        return result;
    }

    private Result AddBanknotes(IReadOnlyDictionary<int, int> banknotes)
    {
        if (banknotes.Count == 0 || banknotes.Values.All(c => c == 0))
            return Error.Validation("Не указано ни одной купюры");

        foreach (var (denomination, count) in banknotes)
        {
            if (count < 0)
                return Error.Validation($"Количество купюр {denomination} ₽ не может быть отрицательным");
            if (count == 0) continue;

            var cassette = FindCassette(denomination);
            if (cassette is null)
                return Error.Business($"Кассета {denomination} ₽ отсутствует");
            if ((long)cassette.Count + count > cassette.MaxCapacity)
                return Error.Business($"Кассета {denomination} ₽ переполнена (макс {cassette.MaxCapacity})");
        }

        foreach (var (denomination, count) in banknotes)
        {
            if (count > 0)
                FindCassette(denomination)!.Credit(count);
        }

        return Result.Success();
    }

    private Cassette? FindCassette(int denomination) =>
        _cassettes.FirstOrDefault(c => c.Denomination == denomination);
}
