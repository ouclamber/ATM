using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;

namespace Bankomat.Domain.Atms;

public sealed class WithdrawalPlan : ValueObject
{
    public IReadOnlyDictionary<int, int> Banknotes { get; }
    public Money Total { get; }

    public WithdrawalPlan(IReadOnlyDictionary<int, int> banknotes)
    {
        Banknotes = banknotes;
        Total = Money.Of(banknotes.Sum(x => (decimal)x.Key * x.Value));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Total;
        foreach (var (k, v) in Banknotes.OrderBy(x => x.Key))
        {
            yield return k;
            yield return v;
        }
    }
}