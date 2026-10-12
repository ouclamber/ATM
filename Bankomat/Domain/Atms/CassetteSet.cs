using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Domain.Atms;

internal static class CassetteSet
{
    public static Result<WithdrawalPlan> PlanWithdrawal(IEnumerable<Cassette> cassettes, Money amount)
    {
        if (!amount.IsMultipleOf(100))
            return Error.Validation("Сумма должна быть кратна 100");

        var remaining = (int)amount.Amount;
        var plan = new Dictionary<int, int>();

        var sorted = cassettes
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Denomination)
            .ToList();

        foreach (var cassette in sorted)
        {
            if (remaining <= 0) break;

            var denom = cassette.Denomination;
            if (denom > remaining) continue;

            var take = Math.Min(remaining / denom, cassette.Count);
            if (take <= 0) continue;

            plan[denom] = take;
            remaining -= denom * take;
        }

        if (remaining > 0)
            return Error.Business("Невозможно выдать указанную сумму имеющимися купюрами");

        return new WithdrawalPlan(plan);
    }
}