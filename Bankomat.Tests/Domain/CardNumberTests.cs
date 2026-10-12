using Bankomat.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Bankomat.Tests.Domain;

public class CardNumberTests
{
    [Theory]
    [InlineData("1111222233334444")]
    [InlineData("1111 2222 3333 4444")]
    [InlineData("1111-2222-3333-4444")]
    public void Create_ValidFormats_Succeeds(string input)
    {
        var result = CardNumber.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("1111222233334444");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_Empty_Fails(string? input)
    {
        var result = CardNumber.Create(input!);

        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("11112222333344445")]
    [InlineData("111122223333444")]     // 15 цифр
    public void Create_WrongLength_Fails(string input)
    {
        var result = CardNumber.Create(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Theory]
    [InlineData("11112222333344aa")]
    [InlineData("1111-2222-3333-444a")]
    public void Create_NonDigits_Fails(string input)
    {
        var result = CardNumber.Create(input);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Formatted_Groups4Digits()
    {
        var card = CardNumber.Create("1111222233334444").Value;

        card.Formatted().Should().Be("1111 2222 3333 4444");
    }

    [Fact]
    public void Equality_ByValue()
    {
        var a = CardNumber.Create("1111222233334444").Value;
        var b = CardNumber.Create("1111 2222 3333 4444").Value;

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }
}