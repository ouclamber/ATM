using Bankomat.Domain.Atms;
using Bankomat.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Bankomat.Tests.Domain;

public class CassetteSetTests
{
    private static Atm CreateAtm()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 100, 200);
        atm.AddCassette(2000, 100, 200);
        atm.AddCassette(1000, 100, 200);
        atm.AddCassette(500,  100, 200);
        atm.AddCassette(100,  100, 500);
        return atm;
    }

    [Fact]
    public void PlanWithdrawal_GreedyTakesLargestFirst()
    {
        var atm = CreateAtm();

        var plan = atm.PlanWithdrawal(Money.Of(12000)).Value;

        plan.Banknotes[5000].Should().Be(2);
        plan.Banknotes[2000].Should().Be(1);
        plan.Total.Amount.Should().Be(12000);
    }

    [Fact]
    public void PlanWithdrawal_ExactSingleDenomination()
    {
        var atm = CreateAtm();

        var plan = atm.PlanWithdrawal(Money.Of(10000)).Value;

        plan.Banknotes[5000].Should().Be(2);
        plan.Banknotes.Should().HaveCount(1);
    }

    [Fact]
    public void PlanWithdrawal_SmallAmountUses100()
    {
        var atm = CreateAtm();

        var plan = atm.PlanWithdrawal(Money.Of(300)).Value;

        plan.Banknotes[100].Should().Be(3);
        plan.Total.Amount.Should().Be(300);
    }

    [Fact]
    public void PlanWithdrawal_AmountWithMixedDenominations()
    {
        var atm = CreateAtm();

        var plan = atm.PlanWithdrawal(Money.Of(8700)).Value;

        // 5000 + 2000 + 1000 + 500 + 2×100 = 8700
        plan.Banknotes[5000].Should().Be(1);
        plan.Banknotes[2000].Should().Be(1);
        plan.Banknotes[1000].Should().Be(1);
        plan.Banknotes[500].Should().Be(1);
        plan.Banknotes[100].Should().Be(2);
        plan.Total.Amount.Should().Be(8700);
    }

    [Fact]
    public void PlanWithdrawal_NotMultipleOf100_Fails()
    {
        var atm = CreateAtm();

        var result = atm.PlanWithdrawal(Money.Of(150));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public void PlanWithdrawal_Zero_Fails()
    {
        var atm = CreateAtm();

        var result = atm.PlanWithdrawal(Money.Of(0));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void PlanWithdrawal_OnInactiveAtm_Fails()
    {
        var atm = CreateAtm();
        atm.Deactivate();

        var result = atm.PlanWithdrawal(Money.Of(5000));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public void PlanWithdrawal_NotEnoughTotal_Fails()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(1000, 1, 100);

        var result = atm.PlanWithdrawal(Money.Of(5000));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void PlanWithdrawal_ImpossibleAmount_Fails()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 1, 100);
        atm.AddCassette(2000, 1, 100);

        var result = atm.PlanWithdrawal(Money.Of(3000));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public void PlanWithdrawal_ImpossibleAmount_WithoutSmallDenominations_Fails()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 3, 100);

        var result = atm.PlanWithdrawal(Money.Of(7000));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void PlanWithdrawal_SkipsTooLargeDenomination()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 1, 100);
        atm.AddCassette(2000, 2, 100);

        var result = atm.PlanWithdrawal(Money.Of(4000));

        result.IsSuccess.Should().BeTrue();
        result.Value.Banknotes[2000].Should().Be(2);
        result.Value.Banknotes.Should().NotContainKey(5000);
    }

    [Fact]
    public void PlanWithdrawal_GreedyLimitation_DocumentsKnownBehavior()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 1, 100);
        atm.AddCassette(2000, 3, 100);

        var result = atm.PlanWithdrawal(Money.Of(6000));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ApplyWithdrawal_DeductsFromCassettes()
    {
        var atm = CreateAtm();

        var plan = atm.PlanWithdrawal(Money.Of(12000)).Value;
        var apply = atm.ApplyWithdrawal(plan);

        apply.IsSuccess.Should().BeTrue();

        var cassette5k = atm.Cassettes.First(c => c.Denomination == 5000);
        cassette5k.Count.Should().Be(98);

        var cassette2k = atm.Cassettes.First(c => c.Denomination == 2000);
        cassette2k.Count.Should().Be(99);
    }

    [Fact]
    public void ApplyWithdrawal_MultipleCassettes_DeductsCorrectly()
    {
        var atm = CreateAtm();

        var plan = atm.PlanWithdrawal(Money.Of(8700)).Value;
        atm.ApplyWithdrawal(plan);

        atm.Cassettes.First(c => c.Denomination == 5000).Count.Should().Be(99);
        atm.Cassettes.First(c => c.Denomination == 2000).Count.Should().Be(99);
        atm.Cassettes.First(c => c.Denomination == 1000).Count.Should().Be(99);
        atm.Cassettes.First(c => c.Denomination == 500).Count.Should().Be(99);
        atm.Cassettes.First(c => c.Denomination == 100).Count.Should().Be(98);
    }

    [Fact]
    public void ApplyWithdrawal_DrainsCassette()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 2, 100);

        var plan = atm.PlanWithdrawal(Money.Of(10000)).Value;
        atm.ApplyWithdrawal(plan);

        atm.Cassettes.First(c => c.Denomination == 5000).Count.Should().Be(0);
    }

    [Fact]
    public void DepositBanknotes_ValidData_IncreasesCassettes()
    {
        var atm = CreateAtm();
        var banknotes = new Dictionary<int, int> { [5000] = 5, [1000] = 10 };

        var result = atm.DepositBanknotes(banknotes);

        result.IsSuccess.Should().BeTrue();
        atm.Cassettes.First(c => c.Denomination == 5000).Count.Should().Be(105);
        atm.Cassettes.First(c => c.Denomination == 1000).Count.Should().Be(110);
    }

    [Fact]
    public void DepositBanknotes_ExceedsCapacity_Fails()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 95, 100);
        var banknotes = new Dictionary<int, int> { [5000] = 10 };

        var result = atm.DepositBanknotes(banknotes);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void DepositBanknotes_MissingCassette_Fails()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 10, 100);
        var banknotes = new Dictionary<int, int> { [1000] = 10 };

        var result = atm.DepositBanknotes(banknotes);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void DepositBanknotes_IgnoresZeroCount()
    {
        var atm = CreateAtm();
        var before = atm.Cassettes.First(c => c.Denomination == 5000).Count;
        var banknotes = new Dictionary<int, int> { [5000] = 0 };

        var result = atm.DepositBanknotes(banknotes);

        result.IsSuccess.Should().BeTrue();
        atm.Cassettes.First(c => c.Denomination == 5000).Count.Should().Be(before);
    }

    [Fact]
    public void DepositBanknotes_ExactCapacity_Succeeds()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 90, 100);
        var banknotes = new Dictionary<int, int> { [5000] = 10 };

        var result = atm.DepositBanknotes(banknotes);

        result.IsSuccess.Should().BeTrue();
        atm.Cassettes.First(c => c.Denomination == 5000).Count.Should().Be(100);
    }
}