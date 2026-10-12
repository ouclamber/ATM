using Bankomat.Infrastructure.Security;
using FluentAssertions;
using Xunit;

namespace Bankomat.Tests.Infrastructure;

public class BCryptPinHasherTests
{
    private readonly BCryptPinHasher _hasher = new();

    [Fact]
    public void Hash_ProducesBcryptFormat()
    {
        var hash = _hasher.Hash("1234");

        hash.Should().StartWith("$2a$");
        hash.Length.Should().Be(60);
    }

    [Fact]
    public void Hash_SamePinTwice_ProducesDifferentHashes()
    {
        var h1 = _hasher.Hash("1234");
        var h2 = _hasher.Hash("1234");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Verify_CorrectPin_ReturnsTrue()
    {
        var hash = _hasher.Hash("1234");

        _hasher.Verify("1234", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPin_ReturnsFalse()
    {
        var hash = _hasher.Hash("1234");

        _hasher.Verify("0000", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "hash")]
    [InlineData("1234", "")]
    [InlineData("", "")]
    [InlineData(null, "hash")]
    [InlineData("1234", null)]
    public void Verify_EmptyOrNull_ReturnsFalse(string? pin, string? hash)
    {
        _hasher.Verify(pin!, hash!).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse()
    {
        _hasher.Verify("1234", "not-a-valid-bcrypt-hash").Should().BeFalse();
    }
}