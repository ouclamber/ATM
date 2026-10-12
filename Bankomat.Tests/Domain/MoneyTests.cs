using Bankomat.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Bankomat.Tests.Domain;

public class MoneyTests
{

    [Fact]
    public void Create_PositiveAmount_Succeeds()
    {
        var result = Money.Create(100m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(100m);
    }

    [Fact]
    public void Create_Zero_Succeeds()
    {
        var result = Money.Create(0m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(0m);
    }

    [Fact]
    public void Create_NegativeAmount_Fails()
    {
        var result = Money.Create(-1m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public void Create_MoreThanTwoDecimals_Fails()
    {
        var result = Money.Create(100.123m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public void Create_TwoDecimals_Succeeds()
    {
        var result = Money.Create(100.99m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(100.99m);
    }

    [Fact]
    public void Of_CreatesMoneyWithoutValidation()
    {
        var money = Money.Of(500);

        money.Amount.Should().Be(500);
    }

    [Fact]
    public void Zero_ReturnsZeroMoney()
    {
        Money.Zero.Amount.Should().Be(0);
    }

    [Fact]
    public void Addition_Works()
    {
        var a = Money.Of(100);
        var b = Money.Of(50);

        (a + b).Amount.Should().Be(150);
    }

    [Fact]
    public void Addition_WithZero()
    {
        var a = Money.Of(100);

        (a + Money.Zero).Amount.Should().Be(100);
    }

    [Fact]
    public void Subtraction_Works()
    {
        var a = Money.Of(100);
        var b = Money.Of(30);

        (a - b).Amount.Should().Be(70);
    }

    [Fact]
    public void Subtraction_AllowsNegative_ValueObjectDoesNotGuard()
    {
        var a = Money.Of(30);
        var b = Money.Of(100);

        (a - b).Amount.Should().Be(-70);
    }

    [Fact]
    public void Operator_GreaterThan_Works()
    {
        (Money.Of(100) > Money.Of(50)).Should().BeTrue();
        (Money.Of(50) > Money.Of(100)).Should().BeFalse();
        (Money.Of(100) > Money.Of(100)).Should().BeFalse();
    }

    [Fact]
    public void Operator_LessThan_Works()
    {
        (Money.Of(50) < Money.Of(100)).Should().BeTrue();
        (Money.Of(100) < Money.Of(50)).Should().BeFalse();
        (Money.Of(100) < Money.Of(100)).Should().BeFalse();
    }

    [Fact]
    public void Operator_GreaterThanOrEqual_Works()
    {
        (Money.Of(100) >= Money.Of(100)).Should().BeTrue();
        (Money.Of(100) >= Money.Of(50)).Should().BeTrue();
        (Money.Of(50) >= Money.Of(100)).Should().BeFalse();
    }

    [Fact]
    public void Operator_LessThanOrEqual_Works()
    {
        (Money.Of(100) <= Money.Of(100)).Should().BeTrue();
        (Money.Of(50) <= Money.Of(100)).Should().BeTrue();
        (Money.Of(100) <= Money.Of(50)).Should().BeFalse();
    }

    [Fact]
    public void Equality_ByAmount()
    {
        var a = Money.Of(100);
        var b = Money.Of(100);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Operator_NotEqual_Works()
    {
        (Money.Of(100) != Money.Of(200)).Should().BeTrue();
        (Money.Of(100) != Money.Of(100)).Should().BeFalse();
    }

    [Fact]
    public void Equals_NullOrOtherType_ReturnsFalse()
    {
        var money = Money.Of(100);

        object.Equals(money, null).Should().BeFalse();
        money.Equals("not money").Should().BeFalse();
    }

    [Fact]
    public void Equals_SameReference_ReturnsTrue()
    {
        var money = Money.Of(100);

        money.Equals(money).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameAmount_SameHash()
    {
        var a = Money.Of(100);
        var b = Money.Of(100);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(101, 100, false)]
    [InlineData(0, 100, true)]
    [InlineData(200, 100, true)]
    [InlineData(50, 100, false)]
    [InlineData(1, 1, true)]
    public void IsMultipleOf_Works(decimal amount, int divisor, bool expected)
    {
        Money.Of(amount).IsMultipleOf(divisor).Should().Be(expected);
    }

    [Fact]
    public void ToString_FormatsAmount()
    {
        var s = Money.Of(1234.56m).ToString();

        s.Should().NotBeNullOrEmpty();
        s.Should().Contain("1");
    }
}