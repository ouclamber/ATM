using Bankomat.Domain.Atms;
using Bankomat.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Bankomat.Tests.Domain;

public class CassetteTests
{
    [Fact]
    public void AtmAddCassette_ValidData_AddsCassette()
    {
        var atm = Atm.Create("A", "L").Value;

        var result = atm.AddCassette(5000, 100, 200);

        result.IsSuccess.Should().BeTrue();
        atm.Cassettes.Should().HaveCount(1);
    }

    [Fact]
    public void AtmAddCassette_InvalidDenomination_Fails()
    {
        var atm = Atm.Create("A", "L").Value;

        var result = atm.AddCassette(300, 100, 200);   // 300 — не кратно 100, недопустимый

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public void AtmAddCassette_DuplicateDenomination_Fails()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 100, 200);

        var result = atm.AddCassette(5000, 50, 200);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CONFLICT");
    }

    [Fact]
    public void AtmAddCassette_CountExceedsCapacity_Fails()
    {
        var atm = Atm.Create("A", "L").Value;

        var result = atm.AddCassette(5000, 300, 200);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TotalCash_SumOfAllCassettes()
    {
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 10, 100);   // 50 000
        atm.AddCassette(1000, 20, 100);   // 20 000
        atm.AddCassette(100, 50, 500);    //  5 000

        atm.TotalCash().Amount.Should().Be(75_000);
    }
}