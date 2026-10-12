using Bankomat.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Bankomat.Tests.Infrastructure;

public class AdminAuthServiceTests
{
    private static IConfiguration BuildConfig(string? username, string? hash)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Admin:Username"] = username,
            ["Admin:PasswordHash"] = hash
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public void Verify_CorrectCredentials_ReturnsTrue()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify("admin", "admin123").Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify("admin", "wrong").Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongUsername_ReturnsFalse()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify("hacker", "admin123").Should().BeFalse();
    }

    [Fact]
    public void Verify_CaseInsensitiveUsername()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify("ADMIN", "admin123").Should().BeTrue();
    }

    [Theory]
    [InlineData("", "admin123")]
    [InlineData("admin", "")]
    [InlineData("", "")]
    public void Verify_EmptyInput_ReturnsFalse(string username, string password)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify(username, password).Should().BeFalse();
    }

    [Fact]
    public void Constructor_MissingHash_Throws()
    {
        var act = () => new AdminAuthService(BuildConfig("admin", null));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PasswordHash*");
    }
}