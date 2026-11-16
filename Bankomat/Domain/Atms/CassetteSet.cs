using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Domain.Atms;

/// <summary>
/// Подбор купюр для выдачи с учётом ограниченного количества купюр в кассетах.
/// </summary>
/// <remarks>
/// Жадный алгоритм ошибается при ограниченных остатках: например, 6000 ₽ при
/// кассетах 5000×1 и 2000×3 жадный подход отклоняет (5000 + остаток 1000 не набрать),
/// хотя 2000×3 подходит. Здесь используется динамическое программирование
/// «ограниченный размен с минимальным числом купюр» (bounded coin change)
/// с бинарным разбиением количества купюр.
/// Сложность: O(U · Σ log₂(count)), где U = сумма / 100. Для лимитов банкомата
/// (до десятков тысяч рублей) это микросекунды.
/// </remarks>
internal static class CassetteSet
{
    /// <summary>Минимальный шаг суммы выдачи (наименьший номинал).</summary>
    public const int Step = 100;

    public static Result<WithdrawalPlan> PlanWithdrawal(IEnumerable<Cassette> cassettes, Money amount)
    {
        if (!amount.IsMultipleOf(Step))
            return Error.Validation($"Сумма должна быть кратна {Step}");

        var available = cassettes
            .Where(c => c.Count > 0)
            .OrderBy(c => c.Denomination) // меньшие номиналы первыми: при равенстве числа купюр побеждают крупные
            .ToList();

        // Быстрый отказ до любых вычислений (заодно защищает от переполнения int ниже)
        var totalCash = available.Sum(c => (decimal)c.Denomination * c.Count);
        if (amount.Amount > totalCash)
            return Error.Business("В банкомате недостаточно наличных для выдачи этой суммы");

        var units = (int)(amount.Amount / Step);

        // Бинарное разбиение: кассету с count купюрами превращаем в пакеты 1, 2, 4, ..., остаток.
        // Любое количество 0..count набирается подмножеством пакетов → задача сводится к рюкзаку 0/1.
        var packs = new List<(int Denomination, int Notes, int Units)>();
        foreach (var c in available)
        {
            var noteUnits = c.Denomination / Step;
            var maxUseful = Math.Min(c.Count, units / noteUnits);
            for (var size = 1; maxUseful > 0; size <<= 1)
            {
                var take = Math.Min(size, maxUseful);
                packs.Add((c.Denomination, take, take * noteUnits));
                maxUseful -= take;
            }
        }

        const int Inf = int.MaxValue;
        var best = new int[units + 1];        // best[u] = минимум купюр для суммы u·100
        Array.Fill(best, Inf);
        best[0] = 0;
        var taken = new bool[packs.Count, units + 1];

        for (var p = 0; p < packs.Count; p++)
        {
            var (_, notes, w) = packs[p];
            for (var u = units; u >= w; u--)
            {
                var prev = best[u - w];
                if (prev == Inf) continue;
                var candidate = prev + notes;
                if (candidate <= best[u]) // <= : при равенстве берём более поздний (крупный) номинал
                {
                    best[u] = candidate;
                    taken[p, u] = true;
                }
            }
        }

        if (best[units] == Inf)
            return Error.Business("Невозможно выдать указанную сумму имеющимися купюрами");

        // Восстановление ответа
        var plan = new Dictionary<int, int>();
        var rest = units;
        for (var p = packs.Count - 1; p >= 0 && rest > 0; p--)
        {
            if (!taken[p, rest]) continue;
            var (denom, notes, w) = packs[p];
            plan[denom] = plan.GetValueOrDefault(denom) + notes;
            rest -= w;
        }

        return new WithdrawalPlan(plan);
    }
}
