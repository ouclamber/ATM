using Bankomat.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Bankomat.Tests.Domain;

public class PinCodeTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("0000")]
    [InlineData("9999")]
    public void Create_ValidPin_Succeeds(string pin)
    {
        var result = PinCode.Create(pin);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(pin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("12 4")]
    public void Create_InvalidPin_Fails(string? pin)
    {
        var result = PinCode.Create(pin!);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ToString_MasksPin()
    {
        var pin = PinCode.Create("1234").Value;

        pin.ToString().Should().Be("****");
        pin.ToString().Should().NotContain("1234");
    }
}